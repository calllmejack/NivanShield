using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class SingBoxConnectionProvider : IConnectionProvider
    {
        private readonly AppLogger _logger;
        private readonly AppPaths _paths;
        private readonly ProxySecretService _secrets;
        private readonly SingBoxConfigBuilder _configBuilder;
        private readonly BinaryIntegrityService _integrity;
        private readonly object _sync = new object();
        private CancellationTokenSource _lifetime;
        private Task _loopTask;
        private Process _process;
        private string _runtimeConfigPath;
        private ConnectionState _state = ConnectionState.Offline;
        private bool _disposed;

        public SingBoxConnectionProvider(
            AppLogger logger,
            AppPaths paths,
            ProxySecretService secrets,
            SingBoxConfigBuilder configBuilder,
            BinaryIntegrityService integrity)
        {
            _logger = logger;
            _paths = paths;
            _secrets = secrets;
            _configBuilder = configBuilder;
            _integrity = integrity;
            RemoveAbandonedRuntimeConfigs();
        }

        public event EventHandler<ConnectionStateChangedEventArgs> StateChanged;

        public string Id { get { return "sing-box"; } }
        public string DisplayName { get { return "sing-box proxy"; } }
        public ConnectionState State { get { return _state; } }
        public bool IsRunning { get { return _loopTask != null && !_loopTask.IsCompleted; } }

        public async Task ConnectAsync(ConnectionProfile profile, AppSettings settings)
        {
            if (_disposed) throw new ObjectDisposedException("SingBoxConnectionProvider");
            if (profile == null || profile.IsSsh || profile.IsPsiphon || profile.Proxy == null)
                throw new ArgumentException("A valid imported proxy profile is required.", "profile");
            if (settings == null || settings.SingBox == null)
                throw new ArgumentException("sing-box settings are missing.", "settings");

            lock (_sync)
            {
                if (IsRunning) return;
            }

            if (await NetworkProbe.IsLocalPortOpenAsync(profile.Proxy.LocalSocksPort).ConfigureAwait(false))
                throw new InvalidOperationException("Local port " + profile.Proxy.LocalSocksPort + " is already in use.");
            bool secretRequired = RequiresSecret(profile.Proxy);
            if (secretRequired && !_secrets.Exists(profile.Id))
                throw new InvalidOperationException("The encrypted credential for this proxy profile is missing. Import the config again.");

            if (!settings.SingBox.UseBundledCore)
                throw new InvalidOperationException("Custom connection cores are disabled in secure mode. Use the reviewed core included with Nivan Shield.");
            string executable = ResolveExecutablePath(_paths, _paths.BundledNekoCorePath, true);
            _integrity.VerifyBundled(
                executable,
                _paths.IntegrityManifestPath,
                "tools/nekoray/nekobox_core.exe"
            );
            string secret = null;
            string json = null;
            string configPath = _paths.GetSingBoxConfigPath(profile.Id);
            try
            {
                secret = _secrets.Exists(profile.Id) ? _secrets.Read(profile.Id) : String.Empty;
                json = _configBuilder.Build(profile, secret, settings.NekoRay, settings.Network);
                PrivateFileService.WriteUtf8(configPath, json);

                ProcessResult check = await ProcessTools.RunHiddenAsync(
                    executable,
                    "check -c " + ProcessTools.Quote(configPath)
                ).ConfigureAwait(false);
                if (check.ExitCode != 0)
                {
                    string detail = (check.StandardError + Environment.NewLine + check.StandardOutput).Trim();
                    throw new InvalidOperationException("sing-box rejected this configuration.\n\n" + detail);
                }
            }
            catch
            {
                DeleteRuntimeConfig(configPath);
                throw;
            }
            finally
            {
                secret = null;
                json = null;
            }

            CancellationTokenSource lifetime = new CancellationTokenSource();
            lock (_sync)
            {
                _runtimeConfigPath = configPath;
                _lifetime = lifetime;
                _loopTask = RunLoopAsync(profile, settings.SingBox, executable, configPath, lifetime.Token);
            }
        }

        private static bool RequiresSecret(ProxySettings proxy)
        {
            string protocol = proxy == null ? String.Empty : (proxy.Protocol ?? String.Empty).Trim().ToLowerInvariant();
            return protocol == "vmess" || protocol == "vless" || protocol == "trojan" || protocol == "shadowsocks";
        }

        public async Task DisconnectAsync()
        {
            Task loop;
            lock (_sync)
            {
                loop = _loopTask;
                if (_lifetime != null) _lifetime.Cancel();
                SetState(ConnectionState.Stopping, "Stopping sing-box...");
                ProcessTools.KillProcessTree(_process);
            }
            if (loop != null) await Task.WhenAny(loop, Task.Delay(3500)).ConfigureAwait(false);
            DeleteRuntimeConfig(_runtimeConfigPath);
            SetState(ConnectionState.Offline, "Disconnected");
        }

        public static string ResolveExecutablePath(AppPaths paths, string configuredPath, bool preferBundled)
        {
            if (preferBundled && paths != null && File.Exists(paths.BundledNekoCorePath))
                return paths.BundledNekoCorePath;

            string configured = (configuredPath ?? String.Empty).Trim();
            if (!String.IsNullOrWhiteSpace(configured))
            {
                if (File.Exists(configured)) return Path.GetFullPath(configured);
            }
            if (paths != null && File.Exists(paths.BundledSingBoxPath)) return paths.BundledSingBoxPath;
            if (paths != null && File.Exists(paths.BundledNekoCorePath)) return paths.BundledNekoCorePath;

            string environmentPath = Environment.GetEnvironmentVariable("PATH") ?? String.Empty;
            foreach (string directory in environmentPath.Split(Path.PathSeparator))
            {
                try
                {
                    string candidate = Path.Combine(directory.Trim().Trim('"'), "sing-box.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            throw new FileNotFoundException(
                !String.IsNullOrWhiteSpace(configured)
                    ? "The selected proxy core was not found and the portable core is missing. Extract the complete Nivan Shield package again."
                    : "The portable proxy core is missing. Extract the complete Nivan Shield package again or select sing-box.exe manually.",
                configured
            );
        }

        public static async Task<string> ReadVersionAsync(AppPaths paths, string configuredPath, bool preferBundled)
        {
            string executable = ResolveExecutablePath(paths, configuredPath, preferBundled);
            ProcessResult result = await ProcessTools.RunHiddenAsync(executable, "version").ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new InvalidOperationException((result.StandardError + Environment.NewLine + result.StandardOutput).Trim());
            string text = (result.StandardOutput + Environment.NewLine + result.StandardError).Trim();
            return String.IsNullOrWhiteSpace(text) ? "sing-box executable is valid." : text;
        }

        private async Task RunLoopAsync(
            ConnectionProfile profile,
            SingBoxSettings settings,
            string executable,
            string configPath,
            CancellationToken cancellationToken)
        {
            bool firstAttempt = true;
            string lastFailure = null;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    SetState(
                        firstAttempt ? ConnectionState.Starting : ConnectionState.Reconnecting,
                        firstAttempt ? "Starting sing-box..." : "Restarting sing-box..."
                    );
                    Process process = null;
                    bool localProxyReady = false;
                    bool internetVerified = false;
                    ProcessOutputTail outputTail = new ProcessOutputTail();
                    try
                    {
                        process = StartProcess(executable, configPath, profile, outputTail);
                        lock (_sync) _process = process;
                        Stopwatch startup = Stopwatch.StartNew();
                        while (!cancellationToken.IsCancellationRequested && !process.HasExited)
                        {
                            if (!localProxyReady
                                && await NetworkProbe.IsLocalPortOpenAsync(profile.Proxy.LocalSocksPort).ConfigureAwait(false))
                            {
                                localProxyReady = true;
                                SetState(ConnectionState.Starting, "Local proxy ready. Verifying internet access...");
                                _logger.Info(
                                    "Local proxy opened for " + profile.Name + "; verifying real internet access through it."
                                );
                                ProxyConnectivityResult verification = await ProxyConnectivityProbe.TestAsync(
                                    profile.Proxy.LocalSocksPort,
                                    12000,
                                    cancellationToken
                                ).ConfigureAwait(false);
                                if (!verification.Success)
                                {
                                    ProcessTools.KillProcessTree(process);
                                    throw new InvalidOperationException(
                                        AppendProcessOutput(
                                            "The local proxy opened, but no website was reachable through this V2Ray profile. "
                                            + verification.Error,
                                            outputTail
                                        )
                                    );
                                }
                                internetVerified = true;
                                _logger.Connected(
                                    "Verified " + profile.ProtocolLabel + " internet access through 127.0.0.1:"
                                    + profile.Proxy.LocalSocksPort + " in " + verification.Milliseconds + " ms."
                                );
                                SetState(ConnectionState.Connected, profile.ProtocolLabel + " internet access verified");
                            }
                            if (!localProxyReady && startup.Elapsed > TimeSpan.FromSeconds(10))
                            {
                                ProcessTools.KillProcessTree(process);
                                throw new InvalidOperationException(
                                    AppendProcessOutput(
                                        "The proxy core did not open local port " + profile.Proxy.LocalSocksPort + " within 10 seconds.",
                                        outputTail
                                    )
                                );
                            }
                            if (localProxyReady && !internetVerified)
                                throw new InvalidOperationException("V2Ray internet verification did not complete.");
                            await DelaySafe(300, cancellationToken).ConfigureAwait(false);
                        }
                        startup.Stop();

                        if (cancellationToken.IsCancellationRequested)
                        {
                            ProcessTools.KillProcessTree(process);
                            break;
                        }
                        process.WaitForExit();
                        throw new InvalidOperationException(
                            AppendProcessOutput("The proxy core exited with code " + process.ExitCode + ".", outputTail)
                        );
                    }
                    catch (Exception exception)
                    {
                        lastFailure = exception.Message;
                        _logger.Error("sing-box process failed: " + lastFailure);
                    }
                    finally
                    {
                        lock (_sync)
                        {
                            if (Object.ReferenceEquals(_process, process)) _process = null;
                        }
                        if (process != null) process.Dispose();
                    }

                    if (cancellationToken.IsCancellationRequested) break;
                    if (!settings.AutoReconnect)
                    {
                        SetState(
                            ConnectionState.Error,
                            String.IsNullOrWhiteSpace(lastFailure) ? "The proxy core stopped unexpectedly." : ShortStatus(lastFailure)
                        );
                        return;
                    }

                    firstAttempt = false;
                    SetState(
                        ConnectionState.Reconnecting,
                        (String.IsNullOrWhiteSpace(lastFailure) ? "The proxy core stopped." : ShortStatus(lastFailure))
                        + " Retrying in " + settings.ReconnectDelaySeconds + " seconds..."
                    );
                    await DelaySafe(settings.ReconnectDelaySeconds * 1000, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                lock (_sync)
                {
                    _process = null;
                    _loopTask = null;
                    if (_lifetime != null)
                    {
                        _lifetime.Dispose();
                        _lifetime = null;
                    }
                }
                DeleteRuntimeConfig(configPath);
                _runtimeConfigPath = null;
                if (_state != ConnectionState.Error) SetState(ConnectionState.Offline, "Disconnected");
                _logger.Info("sing-box connection provider stopped.");
            }
        }

        private Process StartProcess(
            string executable,
            string configPath,
            ConnectionProfile profile,
            ProcessOutputTail outputTail)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = executable;
            info.Arguments = "run -c " + ProcessTools.Quote(configPath);
            info.WorkingDirectory = Path.GetDirectoryName(executable);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;

            Process process = new Process();
            process.StartInfo = info;
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
            {
                if (!String.IsNullOrWhiteSpace(eventArgs.Data))
                {
                    outputTail.Add(eventArgs.Data);
                    _logger.Write("SING-BOX", eventArgs.Data);
                }
            };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
            {
                if (!String.IsNullOrWhiteSpace(eventArgs.Data))
                {
                    outputTail.Add(eventArgs.Data);
                    _logger.Write("SING-BOX", eventArgs.Data);
                }
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _logger.Info(
                "sing-box started for " + profile.ProtocolLabel + " profile " + profile.Name + "."
            );
            return process;
        }

        private static string AppendProcessOutput(string message, ProcessOutputTail outputTail)
        {
            string output = outputTail == null ? String.Empty : outputTail.ToStatusText();
            return String.IsNullOrWhiteSpace(output) ? message : message + " " + output;
        }

        private static string ShortStatus(string value)
        {
            string text = (value ?? String.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
            while (text.Contains("  ")) text = text.Replace("  ", " ");
            return text.Length <= 360 ? text : text.Substring(0, 357) + "...";
        }

        private sealed class ProcessOutputTail
        {
            private readonly object _tailSync = new object();
            private readonly string[] _lines = new string[8];
            private int _next;
            private int _count;

            public void Add(string line)
            {
                string value = ShortStatus(line);
                if (String.IsNullOrWhiteSpace(value)) return;
                lock (_tailSync)
                {
                    _lines[_next] = value;
                    _next = (_next + 1) % _lines.Length;
                    if (_count < _lines.Length) _count++;
                }
            }

            public string ToStatusText()
            {
                lock (_tailSync)
                {
                    if (_count == 0) return String.Empty;
                    StringBuilder builder = new StringBuilder();
                    int start = (_next - _count + _lines.Length) % _lines.Length;
                    for (int index = 0; index < _count; index++)
                    {
                        string line = _lines[(start + index) % _lines.Length];
                        if (String.IsNullOrWhiteSpace(line)) continue;
                        if (builder.Length > 0) builder.Append(" | ");
                        builder.Append(line);
                    }
                    return ShortStatus(builder.ToString());
                }
            }
        }

        private void RemoveAbandonedRuntimeConfigs()
        {
            try
            {
                foreach (string path in Directory.GetFiles(_paths.RuntimeRoot, "sing-box-*.json"))
                    DeleteRuntimeConfig(path);
            }
            catch (Exception exception)
            {
                _logger.Warning("Old sing-box runtime configs could not be cleaned: " + exception.Message);
            }
        }

        private static void DeleteRuntimeConfig(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        private void SetState(ConnectionState state, string detail)
        {
            _state = state;
            EventHandler<ConnectionStateChangedEventArgs> handler = StateChanged;
            if (handler != null) handler(this, new ConnectionStateChangedEventArgs(state, detail));
        }

        private static async Task DelaySafe(int milliseconds, CancellationToken token)
        {
            try { await Task.Delay(Math.Max(1, milliseconds), token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
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
