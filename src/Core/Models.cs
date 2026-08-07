using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Nivan.Shield.Core
{
    public enum ConnectionState
    {
        Offline,
        Starting,
        Connected,
        Reconnecting,
        Stopping,
        Error
    }

    public static class RoutingModes
    {
        public const string BrowserOnly = "BrowserOnly";
        public const string SelectedApps = "SelectedApps";
        public const string WholeDevice = "WholeDevice";
        public const string SystemProxy = "SystemProxy";

        public static bool IsValid(string value)
        {
            return String.Equals(value, BrowserOnly, StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, SelectedApps, StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, WholeDevice, StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, SystemProxy, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class ConnectionStateChangedEventArgs : EventArgs
    {
        public ConnectionStateChangedEventArgs(ConnectionState state, string detail)
        {
            State = state;
            Detail = detail ?? String.Empty;
        }

        public ConnectionState State { get; private set; }
        public string Detail { get; private set; }
    }

    [DataContract]
    public sealed class AppSettings
    {
        [DataMember(Order = 1)] public TunnelSettings Tunnel { get; set; }
        [DataMember(Order = 2)] public NekoRaySettings NekoRay { get; set; }
        [DataMember(Order = 3)] public UiSettings App { get; set; }
        [DataMember(Order = 4)] public ProfileCatalog Profiles { get; set; }
        [DataMember(Order = 5)] public SingBoxSettings SingBox { get; set; }
        [DataMember(Order = 6)] public int SettingsVersion { get; set; }
        [DataMember(Order = 7)] public HealthSettings Health { get; set; }
        [DataMember(Order = 8)] public AutomationSettings Automation { get; set; }
        [DataMember(Order = 9)] public NetworkProtectionSettings Network { get; set; }
        [DataMember(Order = 10)] public SubscriptionCatalog Subscriptions { get; set; }
        [DataMember(Order = 11)] public UpdateSettings Updates { get; set; }
        [DataMember(Order = 12)] public DnsSettings Dns { get; set; }
        [DataMember(Order = 13)] public PsiphonSettings Psiphon { get; set; }
        [DataMember(Order = 14)] public SecuritySettings Security { get; set; }
        [DataMember(Order = 15)] public ShortcutSettings Shortcuts { get; set; }

        public static AppSettings CreateDefault()
        {
            AppSettings settings = new AppSettings();
            settings.Tunnel = CreateDefaultTunnel();
            settings.Profiles = new ProfileCatalog
            {
                ActiveProfileId = "ssh-empty",
                Items = new List<ConnectionProfile>
                {
                    new ConnectionProfile
                    {
                        Id = "ssh-empty",
                        Name = "New SSH connection",
                        Category = "SSH",
                        Engine = "SSH",
                        IsFavorite = false,
                        Tunnel = settings.Tunnel,
                        LastLatencyMilliseconds = -1,
                        LastTestStatus = "Not tested"
                    }
                }
            };
            settings.NekoRay = new NekoRaySettings
            {
                Enabled = true,
                ExecutablePath = String.Empty,
                Arguments = String.Empty,
                AutoStart = true,
                StartDelaySeconds = 0.5,
                CloseWithTunnel = true,
                UseBundledPortable = true,
                EnableSystemProxy = true,
                EnableTunMode = true,
                MixedPort = 2080,
                RoutingMode = RoutingModes.WholeDevice,
                SelectedAppProcesses = String.Empty
            };
            settings.App = new UiSettings
            {
                MinimizeToTray = true,
                StartMinimized = false,
                ConfirmExit = true,
                DisableLanProxyOnDisconnect = true,
                EnableLanProxyOnProxyConnect = true,
                Language = "en"
            };
            settings.SingBox = new SingBoxSettings
            {
                ExecutablePath = String.Empty,
                AutoReconnect = true,
                ReconnectDelaySeconds = 3,
                UseBundledCore = true,
                ApprovedExecutableSha256 = String.Empty
            };
            settings.Health = new HealthSettings
            {
                AutoCheckAfterConnect = true,
                LatencySamples = 5,
                QuickDownloadMegabytes = 5,
                FullDownloadMegabytes = 25,
                FullUploadMegabytes = 10,
                History = new List<ConnectionTestRecord>()
            };
            settings.Automation = new AutomationSettings
            {
                EnableAutoFailover = true,
                FailoverDelaySeconds = 15,
                PreferFavorites = false,
                MaximumFailoverAttempts = 3
            };
            settings.Network = new NetworkProtectionSettings
            {
                RecoverLanProxyAfterCrash = true,
                EnableProxyNetworkLock = false,
                EnableSplitTunneling = false,
                ProxyBypassList = "<local>;localhost;127.*;10.*;172.16.*;192.168.*",
                TunBypassProcesses = String.Empty,
                TunBypassDomains = String.Empty,
                TunBypassIpCidrs = "127.0.0.0/8;10.0.0.0/8;172.16.0.0/12;192.168.0.0/16"
            };
            settings.Subscriptions = new SubscriptionCatalog
            {
                Items = new List<SubscriptionEntry>()
            };
            settings.Updates = new UpdateSettings
            {
                CheckOnStartup = false,
                ManifestUrl = String.Empty,
                LastStatus = "Not checked"
            };
            settings.Dns = new DnsSettings
            {
                ActiveProviderId = "automatic",
                RestoreOnDisconnect = false,
                RestoreAfterCrash = true,
                CustomName = "Custom DNS",
                CustomPrimary = String.Empty,
                CustomSecondary = String.Empty
            };
            settings.Psiphon = new PsiphonSettings
            {
                Enabled = true,
                ExecutablePath = String.Empty,
                ConfigPath = String.Empty,
                ApprovedExecutableSha256 = String.Empty,
                ApprovedConfigSha256 = String.Empty,
                LocalSocksPort = 1090,
                LocalHttpPort = 8090,
                AutoReconnect = true,
                ReconnectDelaySeconds = 5,
                Region = String.Empty
            };
            settings.Security = new SecuritySettings
            {
                RequireHttpsSubscriptions = true,
                BlockPrivateDownloadTargets = true,
                VerifyBundledCoreIntegrity = true,
                RedactSensitiveLogs = true
            };
            settings.Shortcuts = ShortcutSettings.CreateDefault();
            settings.SettingsVersion = 600;
            return settings;
        }

        private static TunnelSettings CreateDefaultTunnel()
        {
            return new TunnelSettings
            {
                Host = String.Empty,
                Port = 22,
                Username = String.Empty,
                SocksPort = 1080,
                AuthMode = "Password",
                PrivateKeyPath = String.Empty,
                ProfileId = "ssh-empty",
                UseSavedPassword = false,
                ServerAliveInterval = 15,
                ServerAliveCountMax = 5,
                TcpKeepAlive = true,
                AutoReconnect = true,
                ReconnectDelaySeconds = 3,
                ClearOldHostKeyOnConnect = true,
                AutoAuthMaxAttempts = 3
            };
        }

        public void Normalize()
        {
            AppSettings defaults = CreateDefault();
            bool migratePortableEngines = SettingsVersion < 310;
            bool migrateIntegratedRouting = SettingsVersion < 320;
            if (Tunnel == null) Tunnel = defaults.Tunnel;
            if (NekoRay == null) NekoRay = defaults.NekoRay;
            if (App == null) App = defaults.App;
            if (!String.Equals(App.Language, "fa", StringComparison.OrdinalIgnoreCase)
                && !String.Equals(App.Language, "en", StringComparison.OrdinalIgnoreCase))
                App.Language = defaults.App.Language;
            if (SingBox == null) SingBox = defaults.SingBox;
            if (Health == null) Health = defaults.Health;
            if (Automation == null) Automation = defaults.Automation;
            if (Network == null) Network = defaults.Network;
            if (Subscriptions == null) Subscriptions = defaults.Subscriptions;
            if (Updates == null) Updates = defaults.Updates;
            if (Dns == null) Dns = defaults.Dns;
            if (Psiphon == null) Psiphon = defaults.Psiphon;
            if (Security == null) Security = defaults.Security;
            if (Shortcuts == null) Shortcuts = defaults.Shortcuts;
            Shortcuts.Normalize();
            NormalizeTunnel(Tunnel, defaults.Tunnel);

            if (Profiles != null && Profiles.Items != null)
                Profiles.Items.RemoveAll(delegate(ConnectionProfile profile) { return profile == null; });

            if (Profiles == null || Profiles.Items == null || Profiles.Items.Count == 0)
            {
                string migratedId = String.IsNullOrWhiteSpace(Tunnel.ProfileId) ? "ssh-primary" : Tunnel.ProfileId;
                Tunnel.ProfileId = migratedId;
                Profiles = new ProfileCatalog
                {
                    ActiveProfileId = migratedId,
                    Items = new List<ConnectionProfile>
                    {
                        new ConnectionProfile
                        {
                            Id = migratedId,
                            Name = "New SSH connection",
                            Category = "SSH",
                            Engine = "SSH",
                            IsFavorite = true,
                            Tunnel = Tunnel,
                            LastLatencyMilliseconds = -1,
                            LastTestStatus = "Not tested"
                        }
                    }
                };
            }

            HashSet<string> identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ConnectionProfile profile in Profiles.Items)
            {
                bool proxyProfile = profile.Proxy != null;
                if (String.IsNullOrWhiteSpace(profile.Engine))
                    profile.Engine = proxyProfile ? "sing-box" : "SSH";
                if (String.IsNullOrWhiteSpace(profile.Id) || identifiers.Contains(profile.Id))
                    profile.Id = (profile.IsSsh ? "ssh-" : "proxy-") + Guid.NewGuid().ToString("N");
                identifiers.Add(profile.Id);

                if (String.IsNullOrWhiteSpace(profile.Name))
                    profile.Name = profile.IsSsh ? "SSH profile" : "Proxy profile";
                if (String.IsNullOrWhiteSpace(profile.Category))
                    profile.Category = profile.IsSsh ? "SSH" : "Imported";

                if (profile.IsSsh)
                {
                    if (profile.Tunnel == null) profile.Tunnel = CloneTunnel(defaults.Tunnel);
                    profile.Tunnel.ProfileId = profile.Id;
                    NormalizeTunnel(profile.Tunnel, defaults.Tunnel);
                }
                else if (profile.IsPsiphon)
                {
                    if (profile.Psiphon == null) profile.Psiphon = new PsiphonProfileSettings();
                    if (profile.Psiphon.LocalSocksPort < 1 || profile.Psiphon.LocalSocksPort > 65535)
                        profile.Psiphon.LocalSocksPort = Psiphon.LocalSocksPort;
                    if (profile.Psiphon.LocalHttpPort < 1 || profile.Psiphon.LocalHttpPort > 65535)
                        profile.Psiphon.LocalHttpPort = Psiphon.LocalHttpPort;
                    if (profile.Psiphon.Region == null) profile.Psiphon.Region = String.Empty;
                }
                else
                {
                    if (profile.Proxy == null) profile.Proxy = new ProxySettings();
                    NormalizeProxy(profile.Proxy);
                    if (String.Equals(profile.Proxy.Transport, "xhttp", StringComparison.OrdinalIgnoreCase))
                        profile.Engine = "xray";
                }

                if (profile.LastTestStatus == null) profile.LastTestStatus = "Not tested";
                if (profile.LastLatencyMilliseconds == 0 && !profile.LastTestedUtc.HasValue)
                    profile.LastLatencyMilliseconds = -1;
                if (profile.SubscriptionId == null) profile.SubscriptionId = String.Empty;
            }

            ConnectionProfile activeProfile = Profiles.Find(Profiles.ActiveProfileId);
            if (activeProfile == null)
            {
                activeProfile = Profiles.Items[0];
                Profiles.ActiveProfileId = activeProfile.Id;
            }

            if (activeProfile.IsSsh)
            {
                Tunnel = activeProfile.Tunnel;
            }
            else
            {
                ConnectionProfile sshFallback = Profiles.Items.FirstOrDefault(
                    delegate(ConnectionProfile profile) { return profile.IsSsh; }
                );
                if (sshFallback != null) Tunnel = sshFallback.Tunnel;
            }

            if (NekoRay.ExecutablePath == null) NekoRay.ExecutablePath = defaults.NekoRay.ExecutablePath;
            if (NekoRay.Arguments == null) NekoRay.Arguments = String.Empty;
            if (NekoRay.StartDelaySeconds < 0) NekoRay.StartDelaySeconds = defaults.NekoRay.StartDelaySeconds;
            if (NekoRay.MixedPort < 1 || NekoRay.MixedPort > 65535) NekoRay.MixedPort = defaults.NekoRay.MixedPort;
            if (!RoutingModes.IsValid(NekoRay.RoutingMode)) NekoRay.RoutingMode = defaults.NekoRay.RoutingMode;
            if (NekoRay.SelectedAppProcesses == null) NekoRay.SelectedAppProcesses = String.Empty;
            if (SingBox.ExecutablePath == null) SingBox.ExecutablePath = String.Empty;
            if (SingBox.ApprovedExecutableSha256 == null) SingBox.ApprovedExecutableSha256 = String.Empty;
            SingBox.UseBundledCore = true;
            if (SingBox.ReconnectDelaySeconds < 1) SingBox.ReconnectDelaySeconds = 3;
            if (Health.LatencySamples < 3 || Health.LatencySamples > 10)
                Health.LatencySamples = defaults.Health.LatencySamples;
            if (Health.QuickDownloadMegabytes < 1 || Health.QuickDownloadMegabytes > 20)
                Health.QuickDownloadMegabytes = defaults.Health.QuickDownloadMegabytes;
            if (Health.FullDownloadMegabytes < 5 || Health.FullDownloadMegabytes > 100)
                Health.FullDownloadMegabytes = defaults.Health.FullDownloadMegabytes;
            if (Health.FullUploadMegabytes < 1 || Health.FullUploadMegabytes > 50)
                Health.FullUploadMegabytes = defaults.Health.FullUploadMegabytes;
            if (Health.History == null) Health.History = new List<ConnectionTestRecord>();
            Health.History.RemoveAll(delegate(ConnectionTestRecord item) { return item == null; });
            if (Health.History.Count > 20)
                Health.History = Health.History.OrderByDescending(
                    delegate(ConnectionTestRecord item) { return item.TestedUtc; }
                ).Take(20).ToList();
            if (Automation.FailoverDelaySeconds < 5 || Automation.FailoverDelaySeconds > 300)
                Automation.FailoverDelaySeconds = defaults.Automation.FailoverDelaySeconds;
            if (Automation.MaximumFailoverAttempts < 1 || Automation.MaximumFailoverAttempts > 20)
                Automation.MaximumFailoverAttempts = defaults.Automation.MaximumFailoverAttempts;
            if (Network.ProxyBypassList == null) Network.ProxyBypassList = defaults.Network.ProxyBypassList;
            if (Network.TunBypassProcesses == null) Network.TunBypassProcesses = String.Empty;
            if (Network.TunBypassDomains == null) Network.TunBypassDomains = String.Empty;
            if (Network.TunBypassIpCidrs == null) Network.TunBypassIpCidrs = defaults.Network.TunBypassIpCidrs;
            if (Subscriptions.Items == null) Subscriptions.Items = new List<SubscriptionEntry>();
            Subscriptions.Items.RemoveAll(delegate(SubscriptionEntry item) { return item == null; });
            HashSet<string> subscriptionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SubscriptionEntry subscription in Subscriptions.Items)
            {
                if (String.IsNullOrWhiteSpace(subscription.Id) || subscriptionIds.Contains(subscription.Id))
                    subscription.Id = "subscription-" + Guid.NewGuid().ToString("N");
                subscriptionIds.Add(subscription.Id);
                if (String.IsNullOrWhiteSpace(subscription.Name)) subscription.Name = "Proxy subscription";
                if (String.IsNullOrWhiteSpace(subscription.Category)) subscription.Category = "Subscription";
                if (subscription.RefreshIntervalHours < 1 || subscription.RefreshIntervalHours > 720)
                    subscription.RefreshIntervalHours = 24;
                if (subscription.LastStatus == null) subscription.LastStatus = "Not updated";
            }
            if (Updates.ManifestUrl == null) Updates.ManifestUrl = String.Empty;
            if (Updates.LastStatus == null) Updates.LastStatus = "Not checked";
            if (String.IsNullOrWhiteSpace(Dns.ActiveProviderId)) Dns.ActiveProviderId = "automatic";
            if (Dns.CustomName == null) Dns.CustomName = "Custom DNS";
            if (Dns.CustomPrimary == null) Dns.CustomPrimary = String.Empty;
            if (Dns.CustomSecondary == null) Dns.CustomSecondary = String.Empty;
            if (Psiphon.ExecutablePath == null) Psiphon.ExecutablePath = String.Empty;
            if (Psiphon.ConfigPath == null) Psiphon.ConfigPath = String.Empty;
            if (Psiphon.ApprovedExecutableSha256 == null) Psiphon.ApprovedExecutableSha256 = String.Empty;
            if (Psiphon.ApprovedConfigSha256 == null) Psiphon.ApprovedConfigSha256 = String.Empty;
            if (Psiphon.LocalSocksPort < 1 || Psiphon.LocalSocksPort > 65535) Psiphon.LocalSocksPort = 1090;
            if (Psiphon.LocalHttpPort < 1 || Psiphon.LocalHttpPort > 65535) Psiphon.LocalHttpPort = 8090;
            if (Psiphon.LocalHttpPort == Psiphon.LocalSocksPort) Psiphon.LocalHttpPort = 8090;
            if (Psiphon.ReconnectDelaySeconds < 2 || Psiphon.ReconnectDelaySeconds > 300)
                Psiphon.ReconnectDelaySeconds = 5;
            if (Psiphon.Region == null) Psiphon.Region = String.Empty;

            // Version 3.1 migrates old machine-specific paths to the portable engine.
            // Secure mode keeps the reviewed bundled cores mandatory.
            if (migratePortableEngines)
            {
                NekoRay.UseBundledPortable = true;
                SingBox.UseBundledCore = true;
                App.EnableLanProxyOnProxyConnect = true;
            }
            if (migrateIntegratedRouting)
            {
                NekoRay.Enabled = true;
                NekoRay.AutoStart = true;
                NekoRay.CloseWithTunnel = true;
                NekoRay.UseBundledPortable = true;
                NekoRay.EnableSystemProxy = true;
                NekoRay.EnableTunMode = true;
                NekoRay.MixedPort = 2080;
                NekoRay.StartDelaySeconds = 0.5;
            }
            ApplyRoutingMode(NekoRay);
            SettingsVersion = 600;

            // Stage 3 keeps the known-good SSH host-key flow exactly as requested.
            Tunnel.ClearOldHostKeyOnConnect = true;
        }

        private static void ApplyRoutingMode(NekoRaySettings routing)
        {
            if (routing == null) return;
            if (String.Equals(routing.RoutingMode, RoutingModes.BrowserOnly, StringComparison.OrdinalIgnoreCase))
            {
                routing.EnableSystemProxy = false;
                routing.EnableTunMode = false;
            }
            else if (String.Equals(routing.RoutingMode, RoutingModes.SystemProxy, StringComparison.OrdinalIgnoreCase))
            {
                routing.EnableSystemProxy = true;
                routing.EnableTunMode = false;
            }
            else if (String.Equals(routing.RoutingMode, RoutingModes.WholeDevice, StringComparison.OrdinalIgnoreCase))
            {
                // Compatibility mode mirrors the original NekoRay workflow:
                // TUN covers all Windows traffic while System Proxy remains
                // available to browsers and proxy-aware applications.
                routing.EnableSystemProxy = true;
                routing.EnableTunMode = true;
            }
            else
            {
                routing.EnableSystemProxy = false;
                routing.EnableTunMode = true;
            }
        }

        private static void NormalizeTunnel(TunnelSettings tunnel, TunnelSettings defaults)
        {
            if (String.IsNullOrWhiteSpace(tunnel.Host)) tunnel.Host = defaults.Host;
            if (String.IsNullOrWhiteSpace(tunnel.Username)) tunnel.Username = defaults.Username;
            if (tunnel.Port < 1 || tunnel.Port > 65535) tunnel.Port = defaults.Port;
            if (tunnel.SocksPort < 1 || tunnel.SocksPort > 65535) tunnel.SocksPort = defaults.SocksPort;
            if (tunnel.ServerAliveInterval < 1) tunnel.ServerAliveInterval = defaults.ServerAliveInterval;
            if (tunnel.ServerAliveCountMax < 1) tunnel.ServerAliveCountMax = defaults.ServerAliveCountMax;
            if (tunnel.ReconnectDelaySeconds < 1) tunnel.ReconnectDelaySeconds = defaults.ReconnectDelaySeconds;
            if (tunnel.AutoAuthMaxAttempts < 1) tunnel.AutoAuthMaxAttempts = defaults.AutoAuthMaxAttempts;
            if (String.IsNullOrWhiteSpace(tunnel.AuthMode)) tunnel.AuthMode = defaults.AuthMode;
            if (tunnel.PrivateKeyPath == null) tunnel.PrivateKeyPath = String.Empty;
            tunnel.ClearOldHostKeyOnConnect = true;
        }

        private static void NormalizeProxy(ProxySettings proxy)
        {
            if (String.IsNullOrWhiteSpace(proxy.Protocol)) proxy.Protocol = "vless";
            proxy.Protocol = proxy.Protocol.Trim().ToLowerInvariant();
            if (proxy.Server == null) proxy.Server = String.Empty;
            if (proxy.ServerPort < 1 || proxy.ServerPort > 65535) proxy.ServerPort = 443;
            if (proxy.LocalSocksPort < 1 || proxy.LocalSocksPort > 65535) proxy.LocalSocksPort = 1081;
            if (String.IsNullOrWhiteSpace(proxy.Encryption)) proxy.Encryption = "auto";
            if (String.IsNullOrWhiteSpace(proxy.Transport)) proxy.Transport = "tcp";
            if (String.IsNullOrWhiteSpace(proxy.TlsMode)) proxy.TlsMode = "none";
            if (proxy.TransportHost == null) proxy.TransportHost = String.Empty;
            if (proxy.Path == null) proxy.Path = String.Empty;
            if (proxy.ServiceName == null) proxy.ServiceName = String.Empty;
            if (proxy.ServerName == null) proxy.ServerName = String.Empty;
            if (proxy.Fingerprint == null) proxy.Fingerprint = String.Empty;
            if (proxy.RealityPublicKey == null) proxy.RealityPublicKey = String.Empty;
            if (proxy.RealityShortId == null) proxy.RealityShortId = String.Empty;
            if (proxy.Alpn == null) proxy.Alpn = String.Empty;
            if (proxy.Flow == null) proxy.Flow = String.Empty;
            if (proxy.PacketEncoding == null) proxy.PacketEncoding = String.Empty;
            if (proxy.Plugin == null) proxy.Plugin = String.Empty;
            if (proxy.PluginOptions == null) proxy.PluginOptions = String.Empty;
            if (proxy.EarlyDataHeaderName == null) proxy.EarlyDataHeaderName = String.Empty;
            if (proxy.ImportFingerprint == null) proxy.ImportFingerprint = String.Empty;
            if (proxy.Username == null) proxy.Username = String.Empty;
            if (proxy.XHttpMode == null) proxy.XHttpMode = String.Empty;
            if (proxy.XHttpExtra == null) proxy.XHttpExtra = String.Empty;
        }

        public static TunnelSettings CloneTunnel(TunnelSettings source)
        {
            return new TunnelSettings
            {
                Host = source.Host,
                Port = source.Port,
                Username = source.Username,
                SocksPort = source.SocksPort,
                AuthMode = source.AuthMode,
                PrivateKeyPath = source.PrivateKeyPath,
                ProfileId = source.ProfileId,
                UseSavedPassword = source.UseSavedPassword,
                ServerAliveInterval = source.ServerAliveInterval,
                ServerAliveCountMax = source.ServerAliveCountMax,
                TcpKeepAlive = source.TcpKeepAlive,
                AutoReconnect = source.AutoReconnect,
                ReconnectDelaySeconds = source.ReconnectDelaySeconds,
                ClearOldHostKeyOnConnect = true,
                AutoAuthMaxAttempts = source.AutoAuthMaxAttempts
            };
        }

        public static ProxySettings CloneProxy(ProxySettings source)
        {
            return new ProxySettings
            {
                Protocol = source.Protocol,
                Server = source.Server,
                ServerPort = source.ServerPort,
                LocalSocksPort = source.LocalSocksPort,
                Encryption = source.Encryption,
                AlterId = source.AlterId,
                Flow = source.Flow,
                Transport = source.Transport,
                TransportHost = source.TransportHost,
                Path = source.Path,
                ServiceName = source.ServiceName,
                TlsMode = source.TlsMode,
                ServerName = source.ServerName,
                AllowInsecure = source.AllowInsecure,
                Fingerprint = source.Fingerprint,
                RealityPublicKey = source.RealityPublicKey,
                RealityShortId = source.RealityShortId,
                Alpn = source.Alpn,
                PacketEncoding = source.PacketEncoding,
                Plugin = source.Plugin,
                PluginOptions = source.PluginOptions,
                WebSocketEarlyData = source.WebSocketEarlyData,
                EarlyDataHeaderName = source.EarlyDataHeaderName,
                ImportFingerprint = source.ImportFingerprint,
                Username = source.Username,
                XHttpMode = source.XHttpMode,
                XHttpExtra = source.XHttpExtra
            };
        }
    }

    [DataContract]
    public sealed class TunnelSettings
    {
        [DataMember(Order = 1)] public string Host { get; set; }
        [DataMember(Order = 2)] public int Port { get; set; }
        [DataMember(Order = 3)] public string Username { get; set; }
        [DataMember(Order = 4)] public int SocksPort { get; set; }
        [DataMember(Order = 5)] public string AuthMode { get; set; }
        [DataMember(Order = 6)] public string PrivateKeyPath { get; set; }
        [DataMember(Order = 7)] public string ProfileId { get; set; }
        [DataMember(Order = 8)] public bool UseSavedPassword { get; set; }
        [DataMember(Order = 9)] public int ServerAliveInterval { get; set; }
        [DataMember(Order = 10)] public int ServerAliveCountMax { get; set; }
        [DataMember(Order = 11)] public bool TcpKeepAlive { get; set; }
        [DataMember(Order = 12)] public bool AutoReconnect { get; set; }
        [DataMember(Order = 13)] public int ReconnectDelaySeconds { get; set; }
        [DataMember(Order = 14)] public bool ClearOldHostKeyOnConnect { get; set; }
        [DataMember(Order = 15)] public int AutoAuthMaxAttempts { get; set; }
    }

    [DataContract]
    public sealed class ProxySettings
    {
        [DataMember(Order = 1)] public string Protocol { get; set; }
        [DataMember(Order = 2)] public string Server { get; set; }
        [DataMember(Order = 3)] public int ServerPort { get; set; }
        [DataMember(Order = 4)] public int LocalSocksPort { get; set; }
        [DataMember(Order = 5)] public string Encryption { get; set; }
        [DataMember(Order = 6)] public int AlterId { get; set; }
        [DataMember(Order = 7)] public string Flow { get; set; }
        [DataMember(Order = 8)] public string Transport { get; set; }
        [DataMember(Order = 9)] public string TransportHost { get; set; }
        [DataMember(Order = 10)] public string Path { get; set; }
        [DataMember(Order = 11)] public string ServiceName { get; set; }
        [DataMember(Order = 12)] public string TlsMode { get; set; }
        [DataMember(Order = 13)] public string ServerName { get; set; }
        [DataMember(Order = 14)] public bool AllowInsecure { get; set; }
        [DataMember(Order = 15)] public string Fingerprint { get; set; }
        [DataMember(Order = 16)] public string RealityPublicKey { get; set; }
        [DataMember(Order = 17)] public string RealityShortId { get; set; }
        [DataMember(Order = 18)] public string Alpn { get; set; }
        [DataMember(Order = 19)] public string PacketEncoding { get; set; }
        [DataMember(Order = 20)] public string Plugin { get; set; }
        [DataMember(Order = 21)] public string PluginOptions { get; set; }
        [DataMember(Order = 22)] public int WebSocketEarlyData { get; set; }
        [DataMember(Order = 23)] public string EarlyDataHeaderName { get; set; }
        [DataMember(Order = 24)] public string ImportFingerprint { get; set; }
        [DataMember(Order = 25)] public string Username { get; set; }
        [DataMember(Order = 26)] public string XHttpMode { get; set; }
        [DataMember(Order = 27)] public string XHttpExtra { get; set; }
    }

    [DataContract]
    public sealed class ProfileCatalog
    {
        [DataMember(Order = 1)] public string ActiveProfileId { get; set; }
        [DataMember(Order = 2)] public List<ConnectionProfile> Items { get; set; }

        public ConnectionProfile Find(string id)
        {
            if (Items == null || String.IsNullOrWhiteSpace(id)) return null;
            foreach (ConnectionProfile profile in Items)
            {
                if (String.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase)) return profile;
            }
            return null;
        }
    }

    [DataContract]
    public sealed class ConnectionProfile
    {
        [DataMember(Order = 1)] public string Id { get; set; }
        [DataMember(Order = 2)] public string Name { get; set; }
        [DataMember(Order = 3)] public string Category { get; set; }
        [DataMember(Order = 4)] public bool IsFavorite { get; set; }
        [DataMember(Order = 5)] public TunnelSettings Tunnel { get; set; }
        [DataMember(Order = 6)] public ProxySettings Proxy { get; set; }
        [DataMember(Order = 7)] public string Engine { get; set; }
        [DataMember(Order = 8)] public long LastLatencyMilliseconds { get; set; }
        [DataMember(Order = 9)] public DateTime? LastTestedUtc { get; set; }
        [DataMember(Order = 10)] public string LastTestStatus { get; set; }
        [DataMember(Order = 11)] public string SubscriptionId { get; set; }
        [DataMember(Order = 12)] public PsiphonProfileSettings Psiphon { get; set; }

        public bool IsSsh
        {
            get { return String.Equals(Engine, "SSH", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsPsiphon
        {
            get { return String.Equals(Engine, "psiphon", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsExternalProxy
        {
            get { return String.Equals(Engine, "external", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsXray
        {
            get { return String.Equals(Engine, "xray", StringComparison.OrdinalIgnoreCase); }
        }

        public string ProtocolLabel
        {
            get
            {
                if (IsSsh) return "SSH";
                if (IsPsiphon) return "PSIPHON";
                return Proxy == null || String.IsNullOrWhiteSpace(Proxy.Protocol)
                    ? "PROXY"
                    : Proxy.Protocol.ToUpperInvariant();
            }
        }

        public string ServerHost
        {
            get
            {
                if (IsSsh) return Tunnel.Host;
                if (IsPsiphon) return "Psiphon network";
                return Proxy == null ? String.Empty : Proxy.Server;
            }
        }

        public int ServerPort
        {
            get
            {
                if (IsSsh) return Tunnel.Port;
                if (IsPsiphon) return 443;
                return Proxy == null ? 0 : Proxy.ServerPort;
            }
        }

        public int LocalSocksPort
        {
            get
            {
                if (IsSsh) return Tunnel.SocksPort;
                if (IsPsiphon) return Psiphon == null ? 1090 : Psiphon.LocalSocksPort;
                return Proxy == null ? 0 : Proxy.LocalSocksPort;
            }
            set
            {
                if (IsSsh) Tunnel.SocksPort = value;
                else if (IsPsiphon)
                {
                    if (Psiphon == null) Psiphon = new PsiphonProfileSettings();
                    Psiphon.LocalSocksPort = value;
                }
                else if (Proxy != null) Proxy.LocalSocksPort = value;
            }
        }

        public string EndpointDisplay
        {
            get
            {
                if (IsSsh) return Tunnel.Username + "@" + Tunnel.Host + ":" + Tunnel.Port;
                if (IsPsiphon) return "psiphon://automatic";
                return ProtocolLabel.ToLowerInvariant() + "://" + Proxy.Server + ":" + Proxy.ServerPort;
            }
        }

        public ConnectionProfile Clone(string newId)
        {
            ConnectionProfile clone = new ConnectionProfile
            {
                Id = newId,
                Name = Name + " copy",
                Category = Category,
                Engine = Engine,
                IsFavorite = false,
                LastLatencyMilliseconds = -1,
                LastTestStatus = "Not tested",
                SubscriptionId = String.Empty
            };
            if (IsSsh)
            {
                clone.Tunnel = AppSettings.CloneTunnel(Tunnel);
                clone.Tunnel.ProfileId = newId;
                clone.Tunnel.UseSavedPassword = false;
            }
            else if (IsPsiphon)
            {
                clone.Psiphon = new PsiphonProfileSettings
                {
                    LocalSocksPort = Psiphon == null ? 1090 : Psiphon.LocalSocksPort,
                    LocalHttpPort = Psiphon == null ? 8090 : Psiphon.LocalHttpPort,
                    Region = Psiphon == null ? String.Empty : Psiphon.Region
                };
            }
            else
            {
                clone.Proxy = AppSettings.CloneProxy(Proxy);
                clone.Proxy.ImportFingerprint = String.Empty;
            }
            return clone;
        }
    }

    [DataContract]
    public sealed class SingBoxSettings
    {
        [DataMember(Order = 1)] public string ExecutablePath { get; set; }
        [DataMember(Order = 2)] public bool AutoReconnect { get; set; }
        [DataMember(Order = 3)] public int ReconnectDelaySeconds { get; set; }
        [DataMember(Order = 4)] public bool UseBundledCore { get; set; }
        [DataMember(Order = 5)] public string ApprovedExecutableSha256 { get; set; }
    }

    [DataContract]
    public sealed class NekoRaySettings
    {
        [DataMember(Order = 1)] public bool Enabled { get; set; }
        [DataMember(Order = 2)] public string ExecutablePath { get; set; }
        [DataMember(Order = 3)] public string Arguments { get; set; }
        [DataMember(Order = 4)] public bool AutoStart { get; set; }
        [DataMember(Order = 5)] public double StartDelaySeconds { get; set; }
        [DataMember(Order = 6)] public bool CloseWithTunnel { get; set; }
        [DataMember(Order = 7)] public bool UseBundledPortable { get; set; }
        [DataMember(Order = 8)] public bool EnableSystemProxy { get; set; }
        [DataMember(Order = 9)] public bool EnableTunMode { get; set; }
        [DataMember(Order = 10)] public int MixedPort { get; set; }
        [DataMember(Order = 11)] public string RoutingMode { get; set; }
        [DataMember(Order = 12)] public string SelectedAppProcesses { get; set; }
    }

    [DataContract]
    public sealed class UiSettings
    {
        [DataMember(Order = 1)] public bool MinimizeToTray { get; set; }
        [DataMember(Order = 2)] public bool StartMinimized { get; set; }
        [DataMember(Order = 3)] public bool ConfirmExit { get; set; }
        [DataMember(Order = 4)] public bool DisableLanProxyOnDisconnect { get; set; }
        [DataMember(Order = 5)] public bool EnableLanProxyOnProxyConnect { get; set; }
        [DataMember(Order = 6)] public string Language { get; set; }
    }

    [DataContract]
    public sealed class ShortcutSettings
    {
        [DataMember(Order = 1)] public string ImportConfig { get; set; }
        [DataMember(Order = 2)] public string ImportQr { get; set; }
        [DataMember(Order = 3)] public string SelectAllProfiles { get; set; }
        [DataMember(Order = 4)] public string DeleteProfiles { get; set; }
        [DataMember(Order = 5)] public string DuplicateProfile { get; set; }
        [DataMember(Order = 6)] public string NewConnection { get; set; }

        public static ShortcutSettings CreateDefault()
        {
            return new ShortcutSettings
            {
                ImportConfig = "Ctrl+I",
                ImportQr = "Ctrl+Shift+I",
                SelectAllProfiles = "Ctrl+A",
                DeleteProfiles = "Delete",
                DuplicateProfile = "Ctrl+D",
                NewConnection = "Ctrl+N"
            };
        }

        public void Normalize()
        {
            ShortcutSettings defaults = CreateDefault();
            if (String.IsNullOrWhiteSpace(ImportConfig)) ImportConfig = defaults.ImportConfig;
            if (String.IsNullOrWhiteSpace(ImportQr)) ImportQr = defaults.ImportQr;
            if (String.IsNullOrWhiteSpace(SelectAllProfiles)) SelectAllProfiles = defaults.SelectAllProfiles;
            if (String.IsNullOrWhiteSpace(DeleteProfiles)) DeleteProfiles = defaults.DeleteProfiles;
            if (String.IsNullOrWhiteSpace(DuplicateProfile)) DuplicateProfile = defaults.DuplicateProfile;
            if (String.IsNullOrWhiteSpace(NewConnection)) NewConnection = defaults.NewConnection;
        }
    }

    [DataContract]
    public sealed class HealthSettings
    {
        [DataMember(Order = 1)] public bool AutoCheckAfterConnect { get; set; }
        [DataMember(Order = 2)] public int LatencySamples { get; set; }
        [DataMember(Order = 3)] public int QuickDownloadMegabytes { get; set; }
        [DataMember(Order = 4)] public int FullDownloadMegabytes { get; set; }
        [DataMember(Order = 5)] public int FullUploadMegabytes { get; set; }
        [DataMember(Order = 6)] public List<ConnectionTestRecord> History { get; set; }
    }

    [DataContract]
    public sealed class ConnectionTestRecord
    {
        [DataMember(Order = 1)] public DateTime TestedUtc { get; set; }
        [DataMember(Order = 2)] public string ProfileId { get; set; }
        [DataMember(Order = 3)] public string ProfileName { get; set; }
        [DataMember(Order = 4)] public string TestKind { get; set; }
        [DataMember(Order = 5)] public long ServerLatencyMilliseconds { get; set; }
        [DataMember(Order = 6)] public double TunnelLatencyMilliseconds { get; set; }
        [DataMember(Order = 7)] public double JitterMilliseconds { get; set; }
        [DataMember(Order = 8)] public double FailureRatePercent { get; set; }
        [DataMember(Order = 9)] public double DownloadMegabitsPerSecond { get; set; }
        [DataMember(Order = 10)] public double UploadMegabitsPerSecond { get; set; }
        [DataMember(Order = 11)] public int QualityScore { get; set; }
        [DataMember(Order = 12)] public string QualityLabel { get; set; }
    }

    [DataContract]
    public sealed class AutomationSettings
    {
        [DataMember(Order = 1)] public bool EnableAutoFailover { get; set; }
        [DataMember(Order = 2)] public int FailoverDelaySeconds { get; set; }
        [DataMember(Order = 3)] public bool PreferFavorites { get; set; }
        [DataMember(Order = 4)] public int MaximumFailoverAttempts { get; set; }
    }

    [DataContract]
    public sealed class NetworkProtectionSettings
    {
        [DataMember(Order = 1)] public bool RecoverLanProxyAfterCrash { get; set; }
        [DataMember(Order = 2)] public bool EnableProxyNetworkLock { get; set; }
        [DataMember(Order = 3)] public bool EnableSplitTunneling { get; set; }
        [DataMember(Order = 4)] public string ProxyBypassList { get; set; }
        [DataMember(Order = 5)] public string TunBypassProcesses { get; set; }
        [DataMember(Order = 6)] public string TunBypassDomains { get; set; }
        [DataMember(Order = 7)] public string TunBypassIpCidrs { get; set; }
    }

    [DataContract]
    public sealed class SubscriptionCatalog
    {
        [DataMember(Order = 1)] public List<SubscriptionEntry> Items { get; set; }

        public SubscriptionEntry Find(string id)
        {
            if (Items == null || String.IsNullOrWhiteSpace(id)) return null;
            return Items.FirstOrDefault(delegate(SubscriptionEntry item)
            {
                return String.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase);
            });
        }
    }

    [DataContract]
    public sealed class SubscriptionEntry
    {
        [DataMember(Order = 1)] public string Id { get; set; }
        [DataMember(Order = 2)] public string Name { get; set; }
        [DataMember(Order = 3)] public string Category { get; set; }
        [DataMember(Order = 4)] public bool AutoUpdate { get; set; }
        [DataMember(Order = 5)] public int RefreshIntervalHours { get; set; }
        [DataMember(Order = 6)] public DateTime? LastUpdatedUtc { get; set; }
        [DataMember(Order = 7)] public string LastStatus { get; set; }
        [DataMember(Order = 8)] public int ProfileCount { get; set; }
    }

    [DataContract]
    public sealed class UpdateSettings
    {
        [DataMember(Order = 1)] public bool CheckOnStartup { get; set; }
        [DataMember(Order = 2)] public string ManifestUrl { get; set; }
        [DataMember(Order = 3)] public DateTime? LastCheckedUtc { get; set; }
        [DataMember(Order = 4)] public string LastStatus { get; set; }
    }

    [DataContract]
    public sealed class PsiphonProfileSettings
    {
        [DataMember(Order = 1)] public int LocalSocksPort { get; set; }
        [DataMember(Order = 2)] public int LocalHttpPort { get; set; }
        [DataMember(Order = 3)] public string Region { get; set; }
    }

    [DataContract]
    public sealed class PsiphonSettings
    {
        [DataMember(Order = 1)] public bool Enabled { get; set; }
        [DataMember(Order = 2)] public string ExecutablePath { get; set; }
        [DataMember(Order = 3)] public string ConfigPath { get; set; }
        [DataMember(Order = 4)] public string ApprovedExecutableSha256 { get; set; }
        [DataMember(Order = 5)] public int LocalSocksPort { get; set; }
        [DataMember(Order = 6)] public int LocalHttpPort { get; set; }
        [DataMember(Order = 7)] public bool AutoReconnect { get; set; }
        [DataMember(Order = 8)] public int ReconnectDelaySeconds { get; set; }
        [DataMember(Order = 9)] public string Region { get; set; }
        [DataMember(Order = 10)] public string ApprovedConfigSha256 { get; set; }
    }

    [DataContract]
    public sealed class DnsSettings
    {
        [DataMember(Order = 1)] public string ActiveProviderId { get; set; }
        [DataMember(Order = 2)] public bool RestoreOnDisconnect { get; set; }
        [DataMember(Order = 3)] public bool RestoreAfterCrash { get; set; }
        [DataMember(Order = 4)] public string CustomName { get; set; }
        [DataMember(Order = 5)] public string CustomPrimary { get; set; }
        [DataMember(Order = 6)] public string CustomSecondary { get; set; }
    }

    [DataContract]
    public sealed class SecuritySettings
    {
        [DataMember(Order = 1)] public bool RequireHttpsSubscriptions { get; set; }
        [DataMember(Order = 2)] public bool BlockPrivateDownloadTargets { get; set; }
        [DataMember(Order = 3)] public bool VerifyBundledCoreIntegrity { get; set; }
        [DataMember(Order = 4)] public bool RedactSensitiveLogs { get; set; }
    }
}
