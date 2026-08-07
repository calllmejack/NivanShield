using System;
using System.Net;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class ExternalProxyService
    {
        private readonly ProxySecretService _secrets;

        public ExternalProxyService(ProxySecretService secrets)
        {
            _secrets = secrets;
        }

        public ConnectionProfile Create(
            string name,
            string protocol,
            string host,
            int port,
            string username,
            string password,
            int localPort)
        {
            string normalizedProtocol = (protocol ?? String.Empty).Trim().ToLowerInvariant();
            if (normalizedProtocol == "socks5") normalizedProtocol = "socks";
            if (normalizedProtocol != "socks" && normalizedProtocol != "http" && normalizedProtocol != "https")
                throw new InvalidOperationException("External proxy protocol must be SOCKS5, HTTP, or HTTPS.");
            string normalizedHost = (host ?? String.Empty).Trim();
            if (String.IsNullOrWhiteSpace(normalizedHost) || Uri.CheckHostName(normalizedHost) == UriHostNameType.Unknown)
                throw new InvalidOperationException("Enter a valid external proxy host or IP address.");
            if (port < 1 || port > 65535 || localPort < 1024 || localPort > 65535)
                throw new InvalidOperationException("The external or local proxy port is invalid.");

            IPAddress address;
            if (IPAddress.TryParse(normalizedHost, out address))
            {
                byte[] bytes = address.GetAddressBytes();
                if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
                    || (bytes.Length > 0 && bytes[0] >= 224))
                    throw new InvalidOperationException("The external proxy address cannot be unspecified or multicast.");
            }

            ConnectionProfile profile = new ConnectionProfile
            {
                Id = "external-" + Guid.NewGuid().ToString("N"),
                Name = String.IsNullOrWhiteSpace(name) ? "External proxy" : name.Trim(),
                Category = "External proxy",
                Engine = "external",
                IsFavorite = false,
                Proxy = new ProxySettings
                {
                    Protocol = normalizedProtocol,
                    Server = normalizedHost,
                    ServerPort = port,
                    LocalSocksPort = localPort,
                    Username = (username ?? String.Empty).Trim(),
                    Encryption = "none",
                    Transport = "tcp",
                    TlsMode = normalizedProtocol == "https" ? "tls" : "none",
                    ServerName = normalizedHost,
                    AllowInsecure = false,
                    ImportFingerprint = String.Empty
                },
                LastLatencyMilliseconds = -1,
                LastTestStatus = "Not tested",
                SubscriptionId = String.Empty
            };
            if (!String.IsNullOrEmpty(password)) _secrets.Save(profile.Id, password);
            return profile;
        }
    }
}
