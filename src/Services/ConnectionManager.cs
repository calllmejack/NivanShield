using System;
using System.Threading;
using System.Threading.Tasks;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class ConnectionManager : IDisposable
    {
        private readonly IConnectionProvider _provider;
        private readonly NekoRayService _nekoRay;
        private readonly SystemProxyService _proxy;
        private readonly AppLogger _logger;
        private readonly DnsService _dns;
        private readonly object _proxySync = new object();
        private CancellationTokenSource _nekoStart;
        private AppSettings _activeSettings;
        private ConnectionProfile _activeProfile;
        private ConnectionState _state = ConnectionState.Offline;
        private bool _wasConnected;
        private bool _cleanupApplied;
        private bool _manualDisconnect;
        private bool _disposed;

        public ConnectionManager(
            IConnectionProvider provider,
            NekoRayService nekoRay,
            SystemProxyService proxy,
            DnsService dns,
            AppLogger logger)
        {
            _provider = provider;
            _nekoRay = nekoRay;
            _proxy = proxy;
            _dns = dns;
            _logger = logger;
            _provider.StateChanged += OnProviderStateChanged;
            _nekoRay.RoutingStopped += OnRoutingStopped;
        }

        public event EventHandler<ConnectionStateChangedEventArgs> StateChanged;

        public ConnectionState State { get { return _state; } }
        public bool IsRunning { get { return _provider.IsRunning; } }

        public async Task ConnectAsync(AppSettings settings)
        {
            if (_disposed) throw new ObjectDisposedException("ConnectionManager");
            _activeSettings = settings;
            _activeProfile = settings.Profiles.Find(settings.Profiles.ActiveProfileId);
            if (_activeProfile == null) throw new InvalidOperationException("The active connection profile was not found.");
            _wasConnected = false;
            _cleanupApplied = false;
            _manualDisconnect = false;
            CancelPendingNekoStart();
            await _provider.ConnectAsync(_activeProfile, settings).ConfigureAwait(false);
        }

        public async Task DisconnectAsync()
        {
            await DisconnectInternalAsync(false).ConfigureAwait(false);
        }

        public async Task DisconnectForFailoverAsync()
        {
            await DisconnectInternalAsync(true).ConfigureAwait(false);
        }

        private async Task DisconnectInternalAsync(bool preserveNetworkLock)
        {
            _manualDisconnect = !preserveNetworkLock;
            CancelPendingNekoStart();
            try
            {
                await _provider.DisconnectAsync().ConfigureAwait(false);
            }
            finally
            {
                // Network cleanup must still run if one provider reports a shutdown error.
                ApplyDisconnectCleanup(preserveNetworkLock);
            }
        }

        private void OnProviderStateChanged(object sender, ConnectionStateChangedEventArgs eventArgs)
        {
            if (eventArgs.State == ConnectionState.Connected && !_wasConnected)
            {
                _wasConnected = true;
                if (ShouldAutoStartIntegratedRouting())
                {
                    RaiseState(
                        ConnectionState.Starting,
                        "Provider connected. Verifying browser and DNS routing..."
                    );
                    ScheduleNekoRay();
                    // Do not expose a false Connected state. ScheduleNekoRay
                    // raises Connected only after the mixed proxy can open a
                    // real website through the full routing chain.
                    return;
                }
                else if (_activeProfile != null
                    && _activeSettings != null
                    && _activeSettings.NekoRay.EnableSystemProxy
                    && _activeSettings.App.EnableLanProxyOnProxyConnect)
                {
                    try
                    {
                        // sing-box uses a mixed inbound, so the same local port accepts HTTP and SOCKS.
                        lock (_proxySync)
                        {
                            if (_provider.State == ConnectionState.Connected)
                                _proxy.EnableLanHttpProxy(
                                    "127.0.0.1",
                                    _activeProfile.LocalSocksPort,
                                    ActiveProxyBypassList()
                                );
                        }
                    }
                    catch (Exception exception)
                    {
                        _logger.Error("Windows LAN proxy could not be enabled: " + exception.Message);
                    }
                }
            }
            else if ((eventArgs.State == ConnectionState.Starting
                    || eventArgs.State == ConnectionState.Reconnecting)
                && _wasConnected && UsesIntegratedRoutingProfile())
            {
                // The upstream core disappeared. Stop TUN/System Proxy before
                // it can keep forwarding browser traffic to a closed port.
                SuspendIntegratedRouting();
                _wasConnected = false;
            }
            else if (eventArgs.State == ConnectionState.Offline || eventArgs.State == ConnectionState.Error)
            {
                if (_wasConnected || eventArgs.State == ConnectionState.Error)
                    ApplyDisconnectCleanup(!_manualDisconnect);
                _wasConnected = false;
            }

            RaiseState(eventArgs.State, eventArgs.Detail);
        }

        private async void ScheduleNekoRay()
        {
            CancelPendingNekoStart();
            if (_activeSettings == null) return;

            _nekoStart = new CancellationTokenSource();
            CancellationToken token = _nekoStart.Token;
            string failureDetail = null;
            try
            {
                await _nekoRay.StartAfterDelayAsync(
                    _activeSettings.NekoRay,
                    BuildUpstreamTunnel(),
                    _activeSettings.Network,
                    token
                ).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!_nekoRay.IsReady || _provider.State != ConnectionState.Connected)
                    throw new InvalidOperationException("Integrated routing stopped before verification completed.");

                ProxyConnectivityResult verification = await ProxyConnectivityProbe.TestSocks5Async(
                    _activeSettings.NekoRay.MixedPort,
                    10000,
                    token
                ).ConfigureAwait(false);
                if (!verification.Success)
                    throw new InvalidOperationException(
                        "The provider connected, but the final SOCKS routing path is unavailable. "
                        + verification.Error
                    );

                if (!token.IsCancellationRequested && _provider.State == ConnectionState.Connected)
                {
                    EnableSshSystemProxyIfNeeded();
                    _logger.Connected(
                        "Final browser proxy path verified through 127.0.0.1:"
                        + _activeSettings.NekoRay.MixedPort + " in "
                        + verification.Milliseconds + " ms."
                    );
                    RaiseState(ConnectionState.Connected, "Browser proxy and routed DNS are ready");
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception exception)
            {
                failureDetail = ShortStatus(exception.Message);
            }

            if (String.IsNullOrWhiteSpace(failureDetail)) return;
            _logger.Error("Integrated routing verification failed: " + failureDetail);
            try
            {
                _nekoRay.Stop(_activeSettings == null ? null : _activeSettings.NekoRay);
                await _provider.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception stopException)
            {
                _logger.Error("Failed routing cleanup: " + stopException.Message);
            }
            RaiseState(ConnectionState.Error, failureDetail);
        }

        private bool UsesIntegratedRoutingProfile()
        {
            return _activeProfile != null
                && (_activeProfile.IsSsh || _activeProfile.IsPsiphon || _activeProfile.IsXray);
        }

        private bool ShouldAutoStartIntegratedRouting()
        {
            return UsesIntegratedRoutingProfile()
                && _activeSettings != null
                && _activeSettings.NekoRay != null
                && _activeSettings.NekoRay.Enabled
                && _activeSettings.NekoRay.AutoStart;
        }

        private void SuspendIntegratedRouting()
        {
            CancelPendingNekoStart();
            if (_activeSettings == null) return;
            try { _nekoRay.Stop(_activeSettings.NekoRay); }
            catch (Exception exception)
            {
                _logger.Error("Integrated routing could not be suspended: " + exception.Message);
            }
        }

        private void RaiseState(ConnectionState state, string detail)
        {
            _state = state;
            EventHandler<ConnectionStateChangedEventArgs> handler = StateChanged;
            if (handler != null)
                handler(this, new ConnectionStateChangedEventArgs(state, detail));
        }

        private static string ShortStatus(string value)
        {
            string text = (value ?? String.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            while (text.Contains("  ")) text = text.Replace("  ", " ");
            return text.Length <= 360 ? text : text.Substring(0, 357) + "...";
        }

        public async Task StartSshRoutingAsync()
        {
            if (_activeSettings == null || _activeProfile == null
                || (!_activeProfile.IsSsh && !_activeProfile.IsPsiphon && !_activeProfile.IsXray))
                throw new InvalidOperationException("Connect an SSH, Psiphon, or XHTTP profile before starting integrated routing.");
            if (_provider.State != ConnectionState.Connected)
                throw new InvalidOperationException("The SSH tunnel must be connected first.");

            await _nekoRay.StartRoutingAsync(
                _activeSettings.NekoRay,
                BuildUpstreamTunnel(),
                _activeSettings.Network,
                CancellationToken.None
            ).ConfigureAwait(false);
            ProxyConnectivityResult verification = await ProxyConnectivityProbe.TestSocks5Async(
                _activeSettings.NekoRay.MixedPort,
                10000,
                CancellationToken.None
            ).ConfigureAwait(false);
            if (!verification.Success)
            {
                _nekoRay.Stop(_activeSettings.NekoRay);
                throw new InvalidOperationException(
                    "Integrated routing opened, but its final SOCKS path is unavailable. "
                    + verification.Error
                );
            }
            EnableSshSystemProxyIfNeeded();
            RaiseState(ConnectionState.Connected, "Browser proxy and routed DNS are ready");
        }

        public void StopSshRouting()
        {
            if (_activeSettings != null) _nekoRay.Stop(_activeSettings.NekoRay);
            try
            {
                if (_activeSettings != null && _activeSettings.App.DisableLanProxyOnDisconnect)
                {
                    lock (_proxySync) _proxy.DisableLanProxy();
                }
            }
            catch (Exception exception)
            {
                _logger.Error("Windows LAN proxy could not be disabled: " + exception.Message);
            }
        }

        private void EnableSshSystemProxyIfNeeded()
        {
            if (_activeSettings == null || _activeProfile == null
                || (!_activeProfile.IsSsh && !_activeProfile.IsPsiphon && !_activeProfile.IsXray)) return;
            if (!_nekoRay.IsReady || !_activeSettings.NekoRay.EnableSystemProxy) return;
            try
            {
                lock (_proxySync)
                {
                    if (_provider.State == ConnectionState.Connected && _nekoRay.IsReady)
                        _proxy.EnableLanHttpProxy(
                            "127.0.0.1",
                            _activeSettings.NekoRay.MixedPort,
                            ActiveProxyBypassList()
                        );
                }
            }
            catch (Exception exception)
            {
                _logger.Error("Windows LAN proxy could not be enabled for SSH routing: " + exception.Message);
            }
        }

        private void OnRoutingStopped(object sender, EventArgs eventArgs)
        {
            if (_activeSettings == null) return;
            try
            {
                if (_nekoRay.LastStopWasUnexpected
                    && _activeSettings.Network.EnableProxyNetworkLock
                    && !_manualDisconnect)
                {
                    lock (_proxySync)
                        _proxy.EnableNetworkLock(ActiveProxyBypassList());
                }
                else if (_activeSettings.App.DisableLanProxyOnDisconnect)
                {
                    lock (_proxySync) _proxy.DisableLanProxy();
                }
            }
            catch (Exception exception)
            {
                _logger.Error("Windows LAN proxy cleanup after routing stopped failed: " + exception.Message);
            }
            if (_nekoRay.LastStopWasUnexpected && !_manualDisconnect)
            {
                string detail = String.IsNullOrWhiteSpace(_nekoRay.LastError)
                    ? "Integrated routing stopped unexpectedly."
                    : _nekoRay.LastError;
                RaiseState(ConnectionState.Error, detail);
            }
        }

        private void ApplyDisconnectCleanup(bool unexpected)
        {
            if (_activeSettings == null) return;
            if (_cleanupApplied)
            {
                if (!unexpected && _activeSettings.App.DisableLanProxyOnDisconnect)
                {
                    try { lock (_proxySync) _proxy.DisableLanProxy(); }
                    catch (Exception exception)
                    {
                        _logger.Error("Windows LAN proxy could not be disabled: " + exception.Message);
                    }
                }
                return;
            }
            _cleanupApplied = true;

            // Integrated routing must never outlive the SSH tunnel it uses.
            if (_activeProfile != null && (_activeProfile.IsSsh || _activeProfile.IsPsiphon || _activeProfile.IsXray))
                _nekoRay.Stop(_activeSettings.NekoRay);

            try
            {
                if (unexpected && _activeSettings.Network.EnableProxyNetworkLock)
                {
                    lock (_proxySync)
                        _proxy.EnableNetworkLock(ActiveProxyBypassList());
                }
                else if (_activeSettings.App.DisableLanProxyOnDisconnect)
                {
                    lock (_proxySync) _proxy.DisableLanProxy();
                }
            }
            catch (Exception exception)
            {
                _logger.Error("Windows LAN proxy could not be disabled: " + exception.Message);
            }

            if (!unexpected && _activeSettings.Dns != null
                && _activeSettings.Dns.RestoreOnDisconnect && _dns.HasPendingRestore)
            {
                try { _dns.RestoreAsync().GetAwaiter().GetResult(); }
                catch (Exception exception)
                {
                    _logger.Error("Windows DNS could not be restored after disconnect: " + exception.Message);
                }
            }

        }

        private TunnelSettings BuildUpstreamTunnel()
        {
            if (_activeProfile == null) throw new InvalidOperationException("No active upstream profile is available.");
            if (_activeProfile.IsSsh) return _activeProfile.Tunnel;
            return new TunnelSettings
            {
                Host = "127.0.0.1",
                Port = _activeProfile.LocalSocksPort,
                Username = "psiphon",
                SocksPort = _activeProfile.LocalSocksPort,
                ProfileId = _activeProfile.Id,
                AuthMode = "None",
                PrivateKeyPath = String.Empty,
                AutoReconnect = true,
                ServerAliveInterval = 15,
                ServerAliveCountMax = 5,
                ReconnectDelaySeconds = 3
            };
        }

        private void CancelPendingNekoStart()
        {
            if (_nekoStart == null) return;
            _nekoStart.Cancel();
            _nekoStart.Dispose();
            _nekoStart = null;
        }

        private string ActiveProxyBypassList()
        {
            if (_activeSettings == null || _activeSettings.Network == null
                || !_activeSettings.Network.EnableSplitTunneling) return "<local>";
            return _activeSettings.Network.ProxyBypassList;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _manualDisconnect = true;
            CancelPendingNekoStart();
            try
            {
                _provider.DisconnectAsync().GetAwaiter().GetResult();
                ApplyDisconnectCleanup(false);
            }
            catch { }
            _provider.StateChanged -= OnProviderStateChanged;
            _nekoRay.RoutingStopped -= OnRoutingStopped;
            _provider.Dispose();
        }
    }
}
