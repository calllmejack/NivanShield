using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    // Keeps the historical service name for settings compatibility. The app
    // uses only the bundled Neko core and presents all routing controls in Nivan.
    public sealed class NekoRayService : IDisposable
    {
        private static readonly Regex AnsiEscape = new Regex(
            "\\x1B\\[[0-?]*[ -/]*[@-~]",
            RegexOptions.Compiled
        );
        private readonly AppLogger _logger;
        private readonly AppPaths _paths;
        private readonly SshRoutingConfigBuilder _configBuilder;
        private readonly BinaryIntegrityService _integrity;
        private readonly object _sync = new object();
        private Process _managedProcess;
        private bool _starting;
        private bool _stopping;
        private bool _ready;
        private bool _lastStopUnexpected;
        private string _lastError = String.Empty;

        public NekoRayService(AppLogger logger, AppPaths paths, BinaryIntegrityService integrity)
        {
            _logger = logger;
            _paths = paths;
            _integrity = integrity;
            _configBuilder = new SshRoutingConfigBuilder();
            DeleteRuntimeConfig();
        }

        public event EventHandler RoutingStopped;

        public Process CurrentProcess
        {
            get
            {
                lock (_sync)
                {
                    if (IsAlive(_managedProcess)) return _managedProcess;
                    return null;
                }
            }
        }

        public bool IsReady
        {
            get { lock (_sync) return _ready && IsAlive(_managedProcess); }
        }

        public string LastError
        {
            get { lock (_sync) return _lastError ?? String.Empty; }
        }

        public bool LastStopWasUnexpected
        {
            get { lock (_sync) return _lastStopUnexpected; }
        }

        public bool IsRunning(NekoRaySettings settings)
        {
            return CurrentProcess != null;
        }

        public async Task StartAfterDelayAsync(
            NekoRaySettings settings,
            TunnelSettings tunnel,
            NetworkProtectionSettings network,
            CancellationToken cancellationToken)
        {
            if (settings == null || !settings.Enabled || !settings.AutoStart) return;
            int delay = (int)Math.Round(Math.Max(0.1, settings.StartDelaySeconds) * 1000.0);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await StartRoutingAsync(settings, tunnel, network, cancellationToken).ConfigureAwait(false);
        }

        public async Task StartRoutingAsync(
            NekoRaySettings settings,
            TunnelSettings tunnel,
            NetworkProtectionSettings network,
            CancellationToken cancellationToken)
        {
            if (settings == null || !settings.Enabled) return;
            lock (_sync)
            {
                if (_ready && IsAlive(_managedProcess)) return;
                if (_starting) throw new InvalidOperationException("Integrated SSH routing is already starting.");
                _starting = true;
                _stopping = false;
                _ready = false;
                _lastStopUnexpected = false;
                _lastError = String.Empty;
            }

            Process process = null;
            ProcessOutputTail outputTail = new ProcessOutputTail();
            try
            {
                if (!await NetworkProbe.IsLocalPortOpenAsync(tunnel.SocksPort).ConfigureAwait(false))
                    throw new InvalidOperationException("The SSH SOCKS port is not ready yet.");
                if (await NetworkProbe.IsLocalPortOpenAsync(settings.MixedPort).ConfigureAwait(false))
                    throw new InvalidOperationException("Integrated proxy port " + settings.MixedPort + " is already in use.");

                string executable = ResolveExecutablePath(settings);
                _integrity.VerifyBundled(
                    executable,
                    _paths.IntegrityManifestPath,
                    "tools/nekoray/nekobox_core.exe"
                );
                string json = _configBuilder.Build(settings, tunnel, network);
                PrivateFileService.WriteUtf8(_paths.SshRoutingConfigPath, json);

                ProcessResult check = await ProcessTools.RunHiddenAsync(
                    executable,
                    "check -c " + ProcessTools.Quote(_paths.SshRoutingConfigPath)
                ).ConfigureAwait(false);
                if (check.ExitCode != 0)
                {
                    string checkDetail = ShortStatus(check.StandardError + Environment.NewLine + check.StandardOutput);
                    throw new InvalidOperationException("The integrated routing configuration was rejected. " + checkDetail);
                }

                cancellationToken.ThrowIfCancellationRequested();
                process = StartCore(executable, outputTail);
                lock (_sync) _managedProcess = process;

                Stopwatch timeout = Stopwatch.StartNew();
                while (!cancellationToken.IsCancellationRequested && IsAlive(process))
                {
                    if (await NetworkProbe.IsLocalPortOpenAsync(settings.MixedPort).ConfigureAwait(false))
                    {
                        lock (_sync)
                        {
                            _ready = true;
                            _lastError = String.Empty;
                        }
                        _logger.Connected(
                            "Integrated SSH routing is active on 127.0.0.1:" + settings.MixedPort
                            + (settings.EnableTunMode ? " with TUN." : ".")
                        );
                        return;
                    }
                    if (timeout.Elapsed > TimeSpan.FromSeconds(12))
                        throw new InvalidOperationException(
                            AppendOutput("The integrated proxy port did not open within 12 seconds.", outputTail)
                        );
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                int exitCode = process == null || IsAlive(process) ? -1 : process.ExitCode;
                throw new InvalidOperationException(
                    AppendOutput("The integrated Neko core exited with code " + exitCode + ".", outputTail)
                );
            }
            catch (OperationCanceledException)
            {
                lock (_sync)
                {
                    _stopping = true;
                    _ready = false;
                    _lastError = String.Empty;
                }
                if (process != null) ProcessTools.KillProcessTree(process);
                DeleteRuntimeConfig();
                lock (_sync) _stopping = false;
                throw;
            }
            catch (Exception exception)
            {
                lock (_sync) _lastError = ShortStatus(exception.Message);
                if (process != null) ProcessTools.KillProcessTree(process);
                DeleteRuntimeConfig();
                _logger.Error("Integrated SSH routing failed: " + exception.Message);
                throw;
            }
            finally
            {
                lock (_sync) _starting = false;
            }
        }

        public void Stop(NekoRaySettings settings)
        {
            Process process;
            lock (_sync)
            {
                _stopping = true;
                _lastStopUnexpected = false;
                process = _managedProcess;
                _ready = false;
                _lastError = String.Empty;
            }

            try
            {
                if (IsAlive(process)) ProcessTools.KillProcessTree(process);
                if (process != null && IsAlive(process)) process.WaitForExit(3000);
                _logger.Info("Integrated SSH routing stopped.");
            }
            catch (Exception exception)
            {
                _logger.Warning("Integrated SSH routing could not be stopped cleanly: " + exception.Message);
            }
            finally
            {
                lock (_sync)
                {
                    if (Object.ReferenceEquals(_managedProcess, process)) _managedProcess = null;
                    _stopping = false;
                }
                if (process != null) process.Dispose();
                DeleteRuntimeConfig();
            }
        }

        public string ResolveExecutablePath(NekoRaySettings settings)
        {
            if (_paths != null && File.Exists(_paths.BundledNekoCorePath))
                return _paths.BundledNekoCorePath;
            throw new FileNotFoundException(
                "The bundled integrated Neko core is missing. Extract the complete Nivan Shield package again.",
                _paths == null ? String.Empty : _paths.BundledNekoCorePath
            );
        }

        private Process StartCore(string executable, ProcessOutputTail outputTail)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = executable;
            info.Arguments = "run -c " + ProcessTools.Quote(_paths.SshRoutingConfigPath);
            info.WorkingDirectory = Path.GetDirectoryName(executable);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = new UTF8Encoding(false);
            info.StandardErrorEncoding = new UTF8Encoding(false);

            Process process = new Process();
            process.StartInfo = info;
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
            {
                CaptureCoreLine(outputTail, eventArgs.Data);
            };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
            {
                CaptureCoreLine(outputTail, eventArgs.Data);
            };
            process.Exited += delegate
            {
                bool unexpected;
                lock (_sync)
                {
                    unexpected = !_stopping;
                    _lastStopUnexpected = unexpected;
                    _ready = false;
                    if (unexpected && String.IsNullOrWhiteSpace(_lastError))
                        _lastError = AppendOutput("The integrated Neko core stopped unexpectedly.", outputTail);
                }
                DeleteRuntimeConfig();
                EventHandler handler = RoutingStopped;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _logger.Info("Bundled Neko core started as an integrated SSH routing service.");
            return process;
        }

        private void CaptureCoreLine(ProcessOutputTail outputTail, string line)
        {
            if (String.IsNullOrWhiteSpace(line)) return;
            string safeLine = ShortStatus(line);
            outputTail.Add(safeLine);
            _logger.Write("SSH-ROUTING", safeLine);
        }

        private void DeleteRuntimeConfig()
        {
            try
            {
                if (File.Exists(_paths.SshRoutingConfigPath)) File.Delete(_paths.SshRoutingConfigPath);
            }
            catch { }
        }

        private static bool IsAlive(Process process)
        {
            if (process == null) return false;
            try { return !process.HasExited; }
            catch { return false; }
        }

        private static string AppendOutput(string message, ProcessOutputTail outputTail)
        {
            string output = outputTail == null ? String.Empty : outputTail.ToStatusText();
            return String.IsNullOrWhiteSpace(output) ? message : message + " " + output;
        }

        private static string ShortStatus(string value)
        {
            string text = AnsiEscape.Replace(value ?? String.Empty, String.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
            while (text.Contains("  ")) text = text.Replace("  ", " ");
            return text.Length <= 420 ? text : text.Substring(0, 417) + "...";
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

        public void Dispose()
        {
            Stop(null);
        }
    }
}
