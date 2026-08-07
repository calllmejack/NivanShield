using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class DnsProviderInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Primary { get; set; }
        public string Secondary { get; set; }
        public string SourceUrl { get; set; }
        public override string ToString() { return Name + "  —  " + Category; }
    }

    public sealed class DnsProbeResult
    {
        public DnsProviderInfo Provider { get; set; }
        public bool Success { get; set; }
        public long Milliseconds { get; set; }
        public string Detail { get; set; }
    }

    [DataContract]
    internal sealed class DnsRestoreSnapshot
    {
        [DataMember(Order = 1)] public DateTime CreatedUtc { get; set; }
        [DataMember(Order = 2)] public List<DnsAdapterSnapshot> Adapters { get; set; }
    }

    [DataContract]
    internal sealed class DnsAdapterSnapshot
    {
        [DataMember(Order = 1)] public string Id { get; set; }
        [DataMember(Order = 2)] public string Name { get; set; }
        [DataMember(Order = 3)] public bool Automatic { get; set; }
        [DataMember(Order = 4)] public List<string> Servers { get; set; }
    }

    public sealed class DnsService
    {
        private readonly AppPaths _paths;
        private readonly AppLogger _logger;

        public DnsService(AppPaths paths, AppLogger logger)
        {
            _paths = paths;
            _logger = logger;
        }

        public IList<DnsProviderInfo> GetProviders(DnsSettings settings)
        {
            List<DnsProviderInfo> providers = new List<DnsProviderInfo>
            {
                new DnsProviderInfo { Id = "automatic", Name = "Automatic (ISP)", Category = "Restore Windows default", Primary = String.Empty, Secondary = String.Empty },
                new DnsProviderInfo { Id = "shecan", Name = "Shecan / شکن", Category = "Iran anti-sanction", Primary = "178.22.122.100", Secondary = "185.51.200.2", SourceUrl = "https://shecan.ir/" },
                new DnsProviderInfo { Id = "403", Name = "403.online", Category = "Iran developer access", Primary = "10.202.10.202", Secondary = "10.202.10.102", SourceUrl = "https://403.online/" },
                new DnsProviderInfo { Id = "radar", Name = "Radar Game", Category = "Iran gaming", Primary = "10.202.10.10", Secondary = "10.202.10.11", SourceUrl = "https://radar.game/" },
                new DnsProviderInfo { Id = "electro", Name = "Electro", Category = "Iran gaming", Primary = "78.157.42.100", Secondary = "78.157.42.101", SourceUrl = "https://electrotm.org/" },
                new DnsProviderInfo { Id = "begzar", Name = "Begzar / بگذر", Category = "Iran anti-sanction", Primary = "185.55.226.26", Secondary = "185.55.225.25", SourceUrl = "https://begzar.ir/" },
                new DnsProviderInfo { Id = "quad9", Name = "Quad9", Category = "Security and privacy", Primary = "9.9.9.9", Secondary = "149.112.112.112", SourceUrl = "https://quad9.net/" },
                new DnsProviderInfo { Id = "cloudflare", Name = "Cloudflare", Category = "Public DNS", Primary = "1.1.1.1", Secondary = "1.0.0.1", SourceUrl = "https://1.1.1.1/" }
            };
            if (settings != null && !String.IsNullOrWhiteSpace(settings.CustomPrimary))
            {
                providers.Add(new DnsProviderInfo
                {
                    Id = "custom",
                    Name = String.IsNullOrWhiteSpace(settings.CustomName) ? "Custom DNS" : settings.CustomName.Trim(),
                    Category = "User supplied",
                    Primary = settings.CustomPrimary.Trim(),
                    Secondary = (settings.CustomSecondary ?? String.Empty).Trim()
                });
            }
            return providers;
        }

        public async Task ApplyAsync(DnsProviderInfo provider)
        {
            if (provider == null) throw new ArgumentNullException("provider");
            if (String.Equals(provider.Id, "automatic", StringComparison.OrdinalIgnoreCase))
            {
                await RestoreAsync().ConfigureAwait(false);
                return;
            }
            ValidateServer(provider.Primary, "Primary DNS");
            if (!String.IsNullOrWhiteSpace(provider.Secondary)) ValidateServer(provider.Secondary, "Secondary DNS");

            List<NetworkInterface> adapters = ActiveAdapters();
            if (adapters.Count == 0) throw new InvalidOperationException("No active physical network adapter was found.");
            if (!File.Exists(_paths.DnsSnapshotPath)) SaveSnapshot(Capture(adapters));

            foreach (NetworkInterface adapter in adapters)
            {
                await RunNetshAsync("interface ipv4 set dnsservers name=" + ProcessTools.Quote(adapter.Name)
                    + " source=static address=" + provider.Primary + " register=primary validate=no").ConfigureAwait(false);
                if (!String.IsNullOrWhiteSpace(provider.Secondary))
                    await RunNetshAsync("interface ipv4 add dnsservers name=" + ProcessTools.Quote(adapter.Name)
                        + " address=" + provider.Secondary + " index=2 validate=no").ConfigureAwait(false);
            }
            await ProcessTools.RunHiddenAsync("ipconfig.exe", "/flushdns").ConfigureAwait(false);
            _logger.Info("DNS provider applied to active adapters: " + provider.Name + ".");
        }

        public async Task RestoreAsync()
        {
            DnsRestoreSnapshot snapshot = LoadSnapshot();
            if (snapshot == null || snapshot.Adapters == null || snapshot.Adapters.Count == 0)
            {
                foreach (NetworkInterface adapter in ActiveAdapters())
                    await RunNetshAsync("interface ipv4 set dnsservers name=" + ProcessTools.Quote(adapter.Name) + " source=dhcp").ConfigureAwait(false);
            }
            else
            {
                Dictionary<string, NetworkInterface> installed = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(delegate(NetworkInterface item) { return item != null && !String.IsNullOrWhiteSpace(item.Id); })
                    .GroupBy(delegate(NetworkInterface item) { return item.Id.Trim().Trim('{', '}'); }, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(delegate(IGrouping<string, NetworkInterface> group) { return group.Key; }, delegate(IGrouping<string, NetworkInterface> group) { return group.First(); }, StringComparer.OrdinalIgnoreCase);
                foreach (DnsAdapterSnapshot adapter in snapshot.Adapters)
                {
                    if (adapter == null || String.IsNullOrWhiteSpace(adapter.Id)) continue;
                    NetworkInterface liveAdapter;
                    if (!installed.TryGetValue(adapter.Id.Trim().Trim('{', '}'), out liveAdapter)) continue;
                    string adapterName = liveAdapter.Name;
                    if (adapter.Automatic || adapter.Servers == null || adapter.Servers.Count == 0)
                    {
                        await RunNetshAsync("interface ipv4 set dnsservers name=" + ProcessTools.Quote(adapterName) + " source=dhcp").ConfigureAwait(false);
                    }
                    else
                    {
                        string primary = adapter.Servers.FirstOrDefault(IsIpv4);
                        if (String.IsNullOrWhiteSpace(primary))
                        {
                            await RunNetshAsync("interface ipv4 set dnsservers name=" + ProcessTools.Quote(adapterName) + " source=dhcp").ConfigureAwait(false);
                            continue;
                        }
                        await RunNetshAsync("interface ipv4 set dnsservers name=" + ProcessTools.Quote(adapterName)
                            + " source=static address=" + primary + " register=primary validate=no").ConfigureAwait(false);
                        int index = 2;
                        foreach (string server in adapter.Servers.Where(IsIpv4).Skip(1).Take(2))
                        {
                            await RunNetshAsync("interface ipv4 add dnsservers name=" + ProcessTools.Quote(adapterName)
                                + " address=" + server + " index=" + index + " validate=no").ConfigureAwait(false);
                            index++;
                        }
                    }
                }
            }
            try { if (File.Exists(_paths.DnsSnapshotPath)) File.Delete(_paths.DnsSnapshotPath); }
            catch { }
            await ProcessTools.RunHiddenAsync("ipconfig.exe", "/flushdns").ConfigureAwait(false);
            _logger.Info("Previous Windows DNS settings restored.");
        }

        public async Task<DnsProbeResult> TestAsync(DnsProviderInfo provider)
        {
            if (provider == null || String.IsNullOrWhiteSpace(provider.Primary))
                return new DnsProbeResult { Provider = provider, Success = true, Milliseconds = 0, Detail = "Managed by Windows" };
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                ValidateServer(provider.Primary, "DNS server");
                byte[] query = BuildQuery();
                using (UdpClient client = new UdpClient(AddressFamily.InterNetwork))
                {
                    client.Connect(provider.Primary, 53);
                    await client.SendAsync(query, query.Length).ConfigureAwait(false);
                    Task<UdpReceiveResult> receive = client.ReceiveAsync();
                    Task completed = await Task.WhenAny(receive, Task.Delay(2500)).ConfigureAwait(false);
                    if (completed != receive) throw new TimeoutException("No DNS response within 2.5 seconds");
                    UdpReceiveResult result = await receive.ConfigureAwait(false);
                    if (result.Buffer == null || result.Buffer.Length < 12 || result.Buffer[0] != query[0] || result.Buffer[1] != query[1])
                        throw new InvalidOperationException("Invalid DNS response");
                    int responseCode = result.Buffer[3] & 0x0F;
                    if (responseCode != 0) throw new InvalidOperationException("DNS response code " + responseCode);
                }
                stopwatch.Stop();
                return new DnsProbeResult { Provider = provider, Success = true, Milliseconds = stopwatch.ElapsedMilliseconds, Detail = "Reachable" };
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                return new DnsProbeResult { Provider = provider, Success = false, Milliseconds = stopwatch.ElapsedMilliseconds, Detail = Short(exception.Message) };
            }
        }

        public bool HasPendingRestore { get { return File.Exists(_paths.DnsSnapshotPath); } }

        private static List<NetworkInterface> ActiveAdapters()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(delegate(NetworkInterface item)
                {
                    if (item.OperationalStatus != OperationalStatus.Up) return false;
                    if (item.NetworkInterfaceType == NetworkInterfaceType.Loopback
                        || item.NetworkInterfaceType == NetworkInterfaceType.Tunnel) return false;
                    try { return item.GetIPProperties().GatewayAddresses.Count > 0; }
                    catch { return false; }
                })
                .ToList();
        }

        private static DnsRestoreSnapshot Capture(IEnumerable<NetworkInterface> adapters)
        {
            DnsRestoreSnapshot snapshot = new DnsRestoreSnapshot
            {
                CreatedUtc = DateTime.UtcNow,
                Adapters = new List<DnsAdapterSnapshot>()
            };
            foreach (NetworkInterface adapter in adapters)
            {
                List<string> servers = adapter.GetIPProperties().DnsAddresses.Select(delegate(IPAddress ip) { return ip.ToString(); }).ToList();
                snapshot.Adapters.Add(new DnsAdapterSnapshot
                {
                    Id = adapter.Id,
                    Name = adapter.Name,
                    Automatic = IsAutomatic(adapter.Id),
                    Servers = servers
                });
            }
            return snapshot;
        }

        private static bool IsAutomatic(string adapterId)
        {
            string clean = (adapterId ?? String.Empty).Trim().Trim('{', '}');
            string keyPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{" + clean + "}";
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath, false))
                {
                    string value = key == null ? String.Empty : Convert.ToString(key.GetValue("NameServer", String.Empty));
                    return String.IsNullOrWhiteSpace(value);
                }
            }
            catch { return true; }
        }

        private void SaveSnapshot(DnsRestoreSnapshot snapshot)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(DnsRestoreSnapshot));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, snapshot);
                if (stream.Length > 256 * 1024) throw new InvalidOperationException("The DNS restore snapshot is unexpectedly large.");
                PrivateFileService.WriteUtf8(_paths.DnsSnapshotPath, Encoding.UTF8.GetString(stream.ToArray()));
            }
        }

        private DnsRestoreSnapshot LoadSnapshot()
        {
            if (!File.Exists(_paths.DnsSnapshotPath)) return null;
            try
            {
                if (new FileInfo(_paths.DnsSnapshotPath).Length > 256 * 1024)
                    throw new InvalidOperationException("The DNS restore snapshot is too large.");
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(DnsRestoreSnapshot));
                using (FileStream stream = File.OpenRead(_paths.DnsSnapshotPath))
                    return serializer.ReadObject(stream) as DnsRestoreSnapshot;
            }
            catch (Exception exception)
            {
                _logger.Warning("Saved DNS restore state could not be read: " + exception.Message);
                return null;
            }
        }

        private static async Task RunNetshAsync(string arguments)
        {
            ProcessResult result = await ProcessTools.RunHiddenAsync("netsh.exe", arguments).ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new InvalidOperationException("Windows rejected the DNS change: " + Short(result.StandardError + " " + result.StandardOutput));
        }

        private static void ValidateServer(string value, string label)
        {
            IPAddress address;
            if (!IPAddress.TryParse((value ?? String.Empty).Trim(), out address)
                || address.AddressFamily != AddressFamily.InterNetwork
                || IPAddress.IsLoopback(address))
                throw new InvalidOperationException(label + " must be a valid public or provider IPv4 address.");
            byte[] bytes = address.GetAddressBytes();
            if (bytes[0] == 0 || bytes[0] >= 224)
                throw new InvalidOperationException(label + " cannot be an unspecified or multicast address.");
        }

        private static bool IsIpv4(string value)
        {
            IPAddress address;
            return IPAddress.TryParse(value, out address) && address.AddressFamily == AddressFamily.InterNetwork;
        }

        private static byte[] BuildQuery()
        {
            ushort id = (ushort)new Random().Next(1, UInt16.MaxValue);
            List<byte> bytes = new List<byte>
            {
                (byte)(id >> 8), (byte)(id & 0xFF), 1, 0, 0, 1, 0, 0, 0, 0, 0, 0
            };
            foreach (string label in "example.com".Split('.'))
            {
                byte[] text = Encoding.ASCII.GetBytes(label);
                bytes.Add((byte)text.Length);
                bytes.AddRange(text);
            }
            bytes.Add(0);
            bytes.Add(0); bytes.Add(1);
            bytes.Add(0); bytes.Add(1);
            return bytes.ToArray();
        }

        private static string Short(string value)
        {
            string text = (value ?? String.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= 180 ? text : text.Substring(0, 177) + "...";
        }
    }
}
