using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class PsiphonConnectionProvider : IConnectionProvider
    {
        private readonly AppLogger _logger;
        private readonly AppPaths _paths;
        private readonly BinaryIntegrityService _integrity;
        private readonly object _sync = new object();
        private CancellationTokenSource _lifetime;
        private Task _loopTask;
        private Process _process;
        private ConnectionState _state = ConnectionState.Offline;
        private bool _disposed;

        public PsiphonConnectionProvider(AppLogger logger, AppPaths paths, BinaryIntegrityService integrity)
        {
            _logger = logger;
            _paths = paths;
            _integrity = integrity;
            DeleteRuntimeConfig();
        }

        public event EventHandler<ConnectionStateChangedEventArgs> StateChanged;
        public string Id { get { return "psiphon"; } }
        public string DisplayName { get { return "Psiphon"; } }
        public ConnectionState State { get { return _state; } }
        public bool IsRunning { get { return _loopTask != null && !_loopTask.IsCompleted; } }

        public Task ConnectAsync(ConnectionProfile profile, AppSettings settings)
        {
            if (_disposed) throw new ObjectDisposedException("PsiphonConnectionProvider");
            if (profile == null || !profile.IsPsiphon)
                throw new ArgumentException("A Psiphon profile is required.", "profile");
            if (settings == null || settings.Psiphon == null || !settings.Psiphon.Enabled)
                throw new InvalidOperationException("Psiphon is disabled in settings.");
            lock (_sync) { if (IsRunning) return Task.FromResult(0); }

            string executable = ResolveExecutable(settings.Psiphon);
            string config = ResolveConfig(settings.Psiphon);
            _integrity.VerifyPsiphonPublisherAsync(executable).GetAwaiter().GetResult();
            _integrity.VerifyPinned(executable, settings.Psiphon.ApprovedExecutableSha256, "The Psiphon core");
            _integrity.VerifyFilePinned(config, settings.Psiphon.ApprovedConfigSha256, "The Psiphon client config");
            BuildRuntimeConfig(config, profile, settings.Psiphon);

            CancellationTokenSource lifetime = new CancellationTokenSource();
            lock (_sync)
            {
                _lifetime = lifetime;
                _loopTask = RunLoopAsync(profile, settings.Psiphon, executable, lifetime.Token);
            }
            return Task.FromResult(0);
        }

        public async Task DisconnectAsync()
        {
            Task loop;
            lock (_sync)
            {
                loop = _loopTask;
                if (_lifetime != null) _lifetime.Cancel();
                SetState(ConnectionState.Stopping, "Stopping Psiphon...");
                ProcessTools.KillProcessTree(_process);
            }
            if (loop != null) await Task.WhenAny(loop, Task.Delay(4000)).ConfigureAwait(false);
            DeleteRuntimeConfig();
            SetState(ConnectionState.Offline, "Disconnected");
        }

        private async Task RunLoopAsync(
            ConnectionProfile profile,
            PsiphonSettings settings,
            string executable,
            CancellationToken token)
        {
            bool first = true;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    Process process = null;
                    try
                    {
                        SetState(first ? ConnectionState.Starting : ConnectionState.Reconnecting,
                            first ? "Starting official Psiphon core..." : "Restarting Psiphon...");
                        process = StartProcess(executable);
                        lock (_sync) _process = process;
                        Task stdout = PumpAsync(process.StandardOutput, false, token);
                        Task stderr = PumpAsync(process.StandardError, true, token);

                        bool connected = false;
                        DateTime deadline = DateTime.UtcNow.AddSeconds(75);
                        while (!token.IsCancellationRequested && !process.HasExited)
                        {
                            bool open = await NetworkProbe.IsLocalPortOpenAsync(profile.LocalSocksPort).ConfigureAwait(false);
                            if (open && !connected)
                            {
                                connected = true;
                                SetState(ConnectionState.Connected,
                                    "Psiphon connected. SOCKS5 is listening on 127.0.0.1:" + profile.LocalSocksPort + ".");
                                _logger.Info("Psiphon local proxy is ready.");
                            }
                            else if (!open && connected)
                            {
                                connected = false;
                                SetState(ConnectionState.Reconnecting, "Psiphon is reconnecting...");
                            }
                            if (!connected && DateTime.UtcNow > deadline)
                                throw new TimeoutException("Psiphon did not create its local proxy within 75 seconds.");
                            await Task.Delay(900, token).ConfigureAwait(false);
                        }
                        if (token.IsCancellationRequested) break;
                        await Task.WhenAny(Task.WhenAll(stdout, stderr), Task.Delay(800)).ConfigureAwait(false);
                        throw new InvalidOperationException("The Psiphon core exited unexpectedly.");
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception exception)
                    {
                        _logger.Error("Psiphon provider failed: " + Short(exception.Message));
                        if (!settings.AutoReconnect)
                        {
                            SetState(ConnectionState.Error, Short(exception.Message));
                            return;
                        }
                    }
                    finally
                    {
                        ProcessTools.KillProcessTree(process);
                        lock (_sync) { if (Object.ReferenceEquals(_process, process)) _process = null; }
                        if (process != null) process.Dispose();
                    }
                    if (token.IsCancellationRequested) break;
                    first = false;
                    SetState(ConnectionState.Reconnecting,
                        "Psiphon stopped. Retrying in " + settings.ReconnectDelaySeconds + " seconds...");
                    await Task.Delay(settings.ReconnectDelaySeconds * 1000, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                lock (_sync)
                {
                    _process = null;
                    _loopTask = null;
                    if (_lifetime != null) { _lifetime.Dispose(); _lifetime = null; }
                }
                DeleteRuntimeConfig();
                if (_state != ConnectionState.Error) SetState(ConnectionState.Offline, "Disconnected");
                _logger.Info("Psiphon connection provider stopped.");
            }
        }

        private Process StartProcess(string executable)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = executable;
            info.Arguments = "-config " + ProcessTools.Quote(_paths.PsiphonRuntimeConfigPath);
            info.WorkingDirectory = Path.GetDirectoryName(executable);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
            Process process = new Process();
            process.StartInfo = info;
            if (!process.Start()) throw new InvalidOperationException("The Psiphon core could not be started.");
            return process;
        }

        private async Task PumpAsync(StreamReader reader, bool error, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                string line;
                try { line = await reader.ReadLineAsync().ConfigureAwait(false); }
                catch { return; }
                if (line == null) return;
                if (line.IndexOf("ListeningSocksProxyPort", StringComparison.OrdinalIgnoreCase) >= 0)
                    _logger.Info("Psiphon reported that its SOCKS proxy is listening.");
                else if (line.IndexOf("Tunnels", StringComparison.OrdinalIgnoreCase) >= 0)
                    _logger.Info("Psiphon tunnel status changed.");
                else if (error && line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                    _logger.Warning("Psiphon reported a connection error. See the official core diagnostics if it persists.");
            }
        }

        private void BuildRuntimeConfig(string templatePath, ConnectionProfile profile, PsiphonSettings settings)
        {
            FileInfo file = new FileInfo(templatePath);
            if (file.Length > 1024 * 1024) throw new InvalidOperationException("The Psiphon client config exceeds the 1 MB safety limit.");
            string json = File.ReadAllText(templatePath, Encoding.UTF8);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 1024 * 1024;
            Dictionary<string, object> config = serializer.Deserialize<Dictionary<string, object>>(json);
            if (config == null) throw new InvalidOperationException("The Psiphon client config is not valid JSON.");
            config["DataRootDirectory"] = _paths.PsiphonDataRoot;
            config["LocalSocksProxyPort"] = profile.LocalSocksPort;
            config["LocalHttpProxyPort"] = profile.Psiphon == null ? settings.LocalHttpPort : profile.Psiphon.LocalHttpPort;
            config["ListenInterface"] = "127.0.0.1";
            config["DisableLocalSocksProxy"] = false;
            config["DisableLocalHTTPProxy"] = false;
            string region = profile.Psiphon == null ? settings.Region : profile.Psiphon.Region;
            if (!String.IsNullOrWhiteSpace(region)) config["EgressRegion"] = region.Trim().ToUpperInvariant();
            PrivateFileService.WriteUtf8(_paths.PsiphonRuntimeConfigPath, serializer.Serialize(config));
            json = null;
        }

        private string ResolveExecutable(PsiphonSettings settings)
        {
            string path = String.IsNullOrWhiteSpace(settings.ExecutablePath)
                ? _paths.BundledPsiphonPath
                : Path.GetFullPath(settings.ExecutablePath);
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "The official Psiphon ConsoleClient is not included yet. Select an official signed Psiphon executable in Psiphon settings.",
                    path
                );
            return path;
        }

        private string ResolveConfig(PsiphonSettings settings)
        {
            string path = String.IsNullOrWhiteSpace(settings.ConfigPath)
                ? _paths.BundledPsiphonConfigPath
                : Path.GetFullPath(settings.ConfigPath);
            if (!File.Exists(path))
                throw new FileNotFoundException("The official Psiphon client.config file was not found.", path);
            if (!String.Equals(Path.GetFileName(path), "client.config", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Psiphon configuration file must be named client.config.");
            return path;
        }

        private void SetState(ConnectionState state, string detail)
        {
            _state = state;
            EventHandler<ConnectionStateChangedEventArgs> handler = StateChanged;
            if (handler != null) handler(this, new ConnectionStateChangedEventArgs(state, detail));
        }

        private void DeleteRuntimeConfig()
        {
            try { if (File.Exists(_paths.PsiphonRuntimeConfigPath)) File.Delete(_paths.PsiphonRuntimeConfigPath); }
            catch { }
        }

        private static string Short(string value)
        {
            string text = (value ?? String.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= 220 ? text : text.Substring(0, 217) + "...";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { DisconnectAsync().GetAwaiter().GetResult(); }
            catch { }
        }
    }
}
