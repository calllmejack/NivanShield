using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class ConnectionProviderRouter : IConnectionProvider
    {
        private readonly IDictionary<string, IConnectionProvider> _providers;
        private readonly IConnectionProvider[] _uniqueProviders;
        private readonly object _sync = new object();
        private IConnectionProvider _active;
        private bool _disposed;

        public ConnectionProviderRouter(
            IConnectionProvider ssh,
            IConnectionProvider singBox,
            IConnectionProvider xray,
            IConnectionProvider psiphon)
        {
            if (ssh == null) throw new ArgumentNullException("ssh");
            if (singBox == null) throw new ArgumentNullException("singBox");
            if (xray == null) throw new ArgumentNullException("xray");
            if (psiphon == null) throw new ArgumentNullException("psiphon");
            _providers = new Dictionary<string, IConnectionProvider>(StringComparer.OrdinalIgnoreCase)
            {
                { "SSH", ssh },
                { "sing-box", singBox },
                { "external", singBox },
                { "xray", xray },
                { "psiphon", psiphon }
            };
            _uniqueProviders = _providers.Values.Distinct().ToArray();
            foreach (IConnectionProvider provider in _uniqueProviders)
                provider.StateChanged += OnProviderStateChanged;
        }

        public event EventHandler<ConnectionStateChangedEventArgs> StateChanged;

        public string Id { get { return "router"; } }
        public string DisplayName { get { return _active == null ? "Connection router" : _active.DisplayName; } }
        public ConnectionState State { get { return _active == null ? ConnectionState.Offline : _active.State; } }
        public bool IsRunning { get { return _uniqueProviders.Any(delegate(IConnectionProvider provider) { return provider.IsRunning; }); } }

        public async Task ConnectAsync(ConnectionProfile profile, AppSettings settings)
        {
            if (_disposed) throw new ObjectDisposedException("ConnectionProviderRouter");
            if (profile == null) throw new ArgumentNullException("profile");
            if (IsRunning) return;

            IConnectionProvider selected;
            string engine = String.IsNullOrWhiteSpace(profile.Engine) ? "SSH" : profile.Engine.Trim();
            if (!_providers.TryGetValue(engine, out selected))
                throw new InvalidOperationException("Unsupported connection provider: " + engine);
            lock (_sync) _active = selected;
            try
            {
                await selected.ConnectAsync(profile, settings).ConfigureAwait(false);
            }
            catch
            {
                lock (_sync) _active = null;
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            IConnectionProvider active;
            lock (_sync) active = _active;
            Exception firstError = null;
            foreach (IConnectionProvider provider in _uniqueProviders)
            {
                if (!provider.IsRunning && !Object.ReferenceEquals(provider, active)) continue;
                try { await provider.DisconnectAsync().ConfigureAwait(false); }
                catch (Exception exception)
                {
                    if (firstError == null) firstError = exception;
                }
            }
            lock (_sync) _active = null;
            if (firstError != null)
                throw new InvalidOperationException("One or more connection providers could not be stopped.", firstError);
        }

        private void OnProviderStateChanged(object sender, ConnectionStateChangedEventArgs eventArgs)
        {
            IConnectionProvider active;
            lock (_sync) active = _active;
            if (!Object.ReferenceEquals(sender, active)) return;
            EventHandler<ConnectionStateChangedEventArgs> handler = StateChanged;
            if (handler != null) handler(this, eventArgs);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (IConnectionProvider provider in _uniqueProviders)
                provider.StateChanged -= OnProviderStateChanged;
            try { DisconnectAsync().GetAwaiter().GetResult(); }
            catch { }
            foreach (IConnectionProvider provider in _uniqueProviders) provider.Dispose();
        }
    }
}
