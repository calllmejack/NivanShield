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
    public sealed class SshConnectionProvider : IConnectionProvider
    {
        private readonly AppLogger _logger;
        private readonly AppPaths _paths;
        private readonly CredentialService _credentials;
        private readonly object _sync = new object();
        private CancellationTokenSource _lifetime;
        private Task _loopTask;
        private Process _sshProcess;
        private ConnectionState _state = ConnectionState.Offline;
        private bool _disposed;

        public SshConnectionProvider(AppLogger logger, AppPaths paths, CredentialService credentials)
        {
            _logger = logger;
            _paths = paths;
            _credentials = credentials;
        }

        public event EventHandler<ConnectionStateChangedEventArgs> StateChanged;

        public string Id { get { return "ssh"; } }
        public string DisplayName { get { return "SSH SOCKS5"; } }
        public ConnectionState State { get { return _state; } }
        public bool IsRunning { get { return _loopTask != null && !_loopTask.IsCompleted; } }

        public async Task ConnectAsync(ConnectionProfile profile, AppSettings appSettings)
        {
            if (_disposed) throw new ObjectDisposedException("SshConnectionProvider");
            if (profile == null || !profile.IsSsh || profile.Tunnel == null)
                throw new ArgumentException("A valid SSH profile is required.", "profile");
            TunnelSettings settings = profile.Tunnel;
            ValidateEndpoint(settings);

            lock (_sync)
            {
                if (IsRunning) return;
            }

            bool portInUse = await NetworkProbe.IsLocalPortOpenAsync(settings.SocksPort).ConfigureAwait(false);
            if (portInUse)
                throw new InvalidOperationException("Local port " + settings.SocksPort + " is already in use.");

            ValidateAuthentication(settings);
            await RemoveStoredHostKeyAsync(settings).ConfigureAwait(false);

            CancellationTokenSource lifetime = new CancellationTokenSource();
            lock (_sync)
            {
                _lifetime = lifetime;
                _loopTask = RunLoopAsync(settings, lifetime.Token);
            }
        }

        public async Task DisconnectAsync()
        {
            Task loop;
            lock (_sync)
            {
                loop = _loopTask;
                if (_lifetime != null) _lifetime.Cancel();
                SetState(ConnectionState.Stopping, "Stopping the SSH tunnel...");
                ProcessTools.KillProcessTree(_sshProcess);
            }

            if (loop != null)
            {
                await Task.WhenAny(loop, Task.Delay(3500)).ConfigureAwait(false);
            }
            SetState(ConnectionState.Offline, "Disconnected");
        }

        private async Task RunLoopAsync(TunnelSettings settings, CancellationToken cancellationToken)
        {
            int failedAttempts = 0;
            bool firstAttempt = true;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    SetState(
                        firstAttempt ? ConnectionState.Starting : ConnectionState.Reconnecting,
                        firstAttempt ? "Starting the original SSH workflow..." : "Reconnecting SSH..."
                    );

                    bool connectedDuringAttempt = false;
                    int exitCode = -1;
                    Process process = null;
                    try
                    {
                        process = StartSshProcess(settings);
                        lock (_sync) _sshProcess = process;
                        _logger.Info("SSH process started for " + settings.Host + ":" + settings.Port + ".");

                        while (!cancellationToken.IsCancellationRequested && !process.HasExited)
                        {
                            if (!connectedDuringAttempt && await NetworkProbe.IsLocalPortOpenAsync(settings.SocksPort).ConfigureAwait(false))
                            {
                                connectedDuringAttempt = true;
                                failedAttempts = 0;
                                _logger.Connected("SOCKS5 tunnel is listening on 127.0.0.1:" + settings.SocksPort + ".");
                                SetState(ConnectionState.Connected, "Secure SOCKS5 tunnel is active");
                            }
                            await DelaySafe(400, cancellationToken).ConfigureAwait(false);
                        }

                        if (cancellationToken.IsCancellationRequested)
                        {
                            ProcessTools.KillProcessTree(process);
                            break;
                        }

                        process.WaitForExit();
                        exitCode = process.ExitCode;
                    }
                    catch (Exception exception)
                    {
                        _logger.Error("SSH process failed: " + exception.Message);
                    }
                    finally
                    {
                        lock (_sync)
                        {
                            if (Object.ReferenceEquals(_sshProcess, process)) _sshProcess = null;
                        }
                        if (process != null) process.Dispose();
                    }

                    if (cancellationToken.IsCancellationRequested) break;

                    _logger.Warning("SSH process exited with code " + exitCode + ".");
                    if (!connectedDuringAttempt)
                    {
                        failedAttempts++;
                        if (settings.UseSavedPassword && failedAttempts >= Math.Max(1, settings.AutoAuthMaxAttempts))
                        {
                            SetState(ConnectionState.Error, "Automatic login failed. Re-enter the SSH password.");
                            _logger.Error("Automatic login stopped after " + failedAttempts + " unsuccessful attempts.");
                            return;
                        }
                    }

                    if (!settings.AutoReconnect) break;
                    firstAttempt = false;
                    SetState(ConnectionState.Reconnecting, "Disconnected. Reconnecting in " + settings.ReconnectDelaySeconds + " seconds...");
                    await DelaySafe(settings.ReconnectDelaySeconds * 1000, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                lock (_sync)
                {
                    _sshProcess = null;
                    _loopTask = null;
                    if (_lifetime != null)
                    {
                        _lifetime.Dispose();
                        _lifetime = null;
                    }
                }
                if (_state != ConnectionState.Error) SetState(ConnectionState.Offline, "Disconnected");
                _logger.Info("SSH connection provider stopped.");
            }
        }

        private Process StartSshProcess(TunnelSettings settings)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = "ssh.exe";
            info.Arguments = BuildOriginalArguments(settings);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = new UTF8Encoding(false);
            info.StandardErrorEncoding = new UTF8Encoding(false);

            if (String.Equals(settings.AuthMode, "Password", StringComparison.OrdinalIgnoreCase))
            {
                info.EnvironmentVariables["SSH_ASKPASS"] = _paths.AskPassPath;
                info.EnvironmentVariables["SSH_ASKPASS_REQUIRE"] = "force";
                info.EnvironmentVariables["DISPLAY"] = "NivanShield";
                info.EnvironmentVariables["NIVAN_SHIELD_ASKPASS"] = "1";
                info.EnvironmentVariables["NIVAN_SHIELD_PASSWORD_FILE"] = _credentials.GetPath(settings.ProfileId);
            }

            Process process = new Process();
            process.StartInfo = info;
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
            {
                if (!String.IsNullOrWhiteSpace(eventArgs.Data)) _logger.Write("SSH", eventArgs.Data);
            };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
            {
                if (!String.IsNullOrWhiteSpace(eventArgs.Data)) _logger.Write("SSH", eventArgs.Data);
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        private static string BuildOriginalArguments(TunnelSettings settings)
        {
            StringBuilder arguments = new StringBuilder();
            arguments.Append("-o ServerAliveInterval=").Append(settings.ServerAliveInterval).Append(' ');
            arguments.Append("-o ServerAliveCountMax=").Append(settings.ServerAliveCountMax).Append(' ');
            arguments.Append("-o TCPKeepAlive=").Append(settings.TcpKeepAlive ? "yes" : "no").Append(' ');
            arguments.Append("-D ").Append(settings.SocksPort).Append(' ');
            arguments.Append("-N -p ").Append(settings.Port).Append(' ');
            if (String.Equals(settings.AuthMode, "Private key", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Append("-i ").Append(ProcessTools.Quote(settings.PrivateKeyPath)).Append(' ');
            }
            arguments.Append(ProcessTools.Quote(settings.Username + "@" + settings.Host));
            return arguments.ToString();
        }

        private async Task RemoveStoredHostKeyAsync(TunnelSettings settings)
        {
            string target = "[" + settings.Host + "]:" + settings.Port;
            ProcessResult result = await ProcessTools.RunHiddenAsync("ssh-keygen.exe", "-R " + ProcessTools.Quote(target)).ConfigureAwait(false);
            string detail = (result.StandardOutput + " " + result.StandardError).Trim();
            _logger.Info("Removed stored SSH host key for " + target + ". " + detail);
        }

        private void ValidateAuthentication(TunnelSettings settings)
        {
            if (String.Equals(settings.AuthMode, "Password", StringComparison.OrdinalIgnoreCase))
            {
                if (!settings.UseSavedPassword || !_credentials.Exists(settings.ProfileId))
                    throw new InvalidOperationException("Save the SSH password before connecting.");
                if (!File.Exists(_paths.AskPassPath))
                    throw new FileNotFoundException("The Nivan executable used for automatic SSH authentication is missing.", _paths.AskPassPath);
            }
            else if (!File.Exists(settings.PrivateKeyPath))
            {
                throw new FileNotFoundException("Select a valid SSH private key.", settings.PrivateKeyPath);
            }
        }

        private static void ValidateEndpoint(TunnelSettings settings)
        {
            if (settings.Port < 1 || settings.Port > 65535
                || settings.SocksPort < 1 || settings.SocksPort > 65535)
                throw new InvalidOperationException("The SSH server or local SOCKS port is invalid.");
            if (!Regex.IsMatch(settings.Host ?? String.Empty, "^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,252}$"))
                throw new InvalidOperationException("The SSH host contains unsafe characters.");
            if (!Regex.IsMatch(settings.Username ?? String.Empty, "^[a-zA-Z0-9_][a-zA-Z0-9._-]{0,63}$"))
                throw new InvalidOperationException("The SSH username contains unsafe characters.");
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
            try { DisconnectAsync().GetAwaiter().GetResult(); } catch { }
        }
    }
}
