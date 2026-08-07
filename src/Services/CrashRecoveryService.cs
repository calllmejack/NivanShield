using System;
using System.IO;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class CrashRecoveryService
    {
        private readonly AppPaths _paths;
        private readonly SystemProxyService _proxy;
        private readonly AppLogger _logger;
        private readonly DnsService _dns;
        private bool _sessionStarted;

        public CrashRecoveryService(AppPaths paths, SystemProxyService proxy, DnsService dns, AppLogger logger)
        {
            _paths = paths;
            _proxy = proxy;
            _dns = dns;
            _logger = logger;
        }

        public void BeginSession(NetworkProtectionSettings settings, DnsSettings dnsSettings)
        {
            if (_sessionStarted) return;
            bool abandonedSession = File.Exists(_paths.SessionMarkerPath);
            if (abandonedSession && settings != null && settings.RecoverLanProxyAfterCrash)
            {
                try
                {
                    _proxy.DisableLanProxy();
                    _logger.Warning(
                        "A previous unclean shutdown was detected. Windows LAN Proxy was safely disabled."
                    );
                }
                catch (Exception exception)
                {
                    _logger.Error("Crash recovery could not reset Windows LAN Proxy: " + exception.Message);
                }
            }
            if (abandonedSession && dnsSettings != null && dnsSettings.RestoreAfterCrash && _dns.HasPendingRestore)
            {
                try
                {
                    _dns.RestoreAsync().GetAwaiter().GetResult();
                    _logger.Warning("A previous unclean shutdown was detected. Windows DNS settings were restored.");
                }
                catch (Exception exception)
                {
                    _logger.Error("Crash recovery could not restore Windows DNS: " + exception.Message);
                }
            }

            SessionMarkerFile.Write(_paths.SessionMarkerPath);
            _sessionStarted = true;
        }

        public void EndSession()
        {
            if (!_sessionStarted) return;
            try
            {
                if (File.Exists(_paths.SessionMarkerPath)) File.Delete(_paths.SessionMarkerPath);
            }
            catch (Exception exception)
            {
                _logger.Warning("The clean-shutdown marker could not be removed: " + exception.Message);
            }
            _sessionStarted = false;
        }
    }
}
