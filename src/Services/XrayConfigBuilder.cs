using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    /// <summary>
    /// Builds the smallest possible local Xray client configuration for
    /// transports that are native to Xray, currently XHTTP. The generated
    /// file exists only for the lifetime of the connection.
    /// </summary>
    public sealed class XrayConfigBuilder
    {
        private const int MaximumExtraCharacters = 32 * 1024;

        public string Build(ConnectionProfile profile, string secret)
        {
            if (profile == null || profile.Proxy == null || !profile.IsXray)
                throw new InvalidOperationException("Select a valid XHTTP profile first.");
            ProxySettings proxy = profile.Proxy;
            if (!String.Equals(proxy.Transport, "xhttp", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Xray provider is reserved for XHTTP profiles.");
            if (String.IsNullOrWhiteSpace(secret))
                throw new InvalidOperationException("The encrypted credential for this XHTTP profile is empty.");

            Dictionary<string, object> root = new Dictionary<string, object>
            {
                { "log", new Dictionary<string, object> { { "loglevel", "warning" } } },
                { "inbounds", new object[] { BuildSocksInbound(proxy.LocalSocksPort) } },
                { "outbounds", new object[]
                    {
                        BuildProxyOutbound(proxy, secret),
                        new Dictionary<string, object> { { "protocol", "freedom" }, { "tag", "direct" } },
                        new Dictionary<string, object> { { "protocol", "blackhole" }, { "tag", "block" } }
                    }
                },
                { "routing", new Dictionary<string, object>
                    {
                        // Keep domain names intact until they reach the proxy.
                        // IPIfNonMatch asks Windows DNS to resolve every domain
                        // before evaluating the private-address rule below. On
                        // filtered networks a poisoned 10.x/172.16.x response
                        // then incorrectly selects the direct outbound (seen as
                        // "local-socks -> direct" for blocked sites). AsIs still
                        // permits literal LAN IP destinations to use the direct
                        // rule without trusting the local DNS result for domains.
                        { "domainStrategy", "AsIs" },
                        { "rules", new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    { "type", "field" },
                                    { "ip", new string[]
                                        {
                                            "127.0.0.0/8",
                                            "10.0.0.0/8",
                                            "172.16.0.0/12",
                                            "192.168.0.0/16",
                                            "::1/128",
                                            "fc00::/7"
                                        }
                                    },
                                    { "outboundTag", "direct" }
                                }
                            }
                        }
                    }
                }
            };

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 4 * 1024 * 1024;
            return serializer.Serialize(root);
        }

        private static Dictionary<string, object> BuildSocksInbound(int port)
        {
            if (port < 1 || port > 65535)
                throw new InvalidOperationException("The local SOCKS port is invalid.");
            return new Dictionary<string, object>
            {
                { "tag", "local-socks" },
                { "listen", "127.0.0.1" },
                { "port", port },
                { "protocol", "socks" },
                { "settings", new Dictionary<string, object>
                    {
                        { "auth", "noauth" },
                        { "udp", true },
                        { "ip", "127.0.0.1" }
                    }
                },
                { "sniffing", new Dictionary<string, object>
                    {
                        { "enabled", true },
                        { "routeOnly", false },
                        { "destOverride", new string[] { "http", "tls", "quic" } }
                    }
                }
            };
        }

        private Dictionary<string, object> BuildProxyOutbound(ProxySettings proxy, string secret)
        {
            string protocol = (proxy.Protocol ?? String.Empty).Trim().ToLowerInvariant();
            Dictionary<string, object> outbound = new Dictionary<string, object>
            {
                { "tag", "proxy" },
                { "protocol", protocol },
                { "streamSettings", BuildStreamSettings(proxy) }
            };

            if (protocol == "vless" || protocol == "vmess")
            {
                Dictionary<string, object> user = new Dictionary<string, object>
                {
                    { "id", secret }
                };
                if (protocol == "vless")
                {
                    user["encryption"] = String.IsNullOrWhiteSpace(proxy.Encryption) ? "none" : proxy.Encryption;
                    if (!String.IsNullOrWhiteSpace(proxy.Flow)) user["flow"] = proxy.Flow;
                }
                else
                {
                    user["alterId"] = Math.Max(0, proxy.AlterId);
                    user["security"] = String.IsNullOrWhiteSpace(proxy.Encryption) ? "auto" : proxy.Encryption;
                }
                outbound["settings"] = new Dictionary<string, object>
                {
                    { "vnext", new object[]
                        {
                            new Dictionary<string, object>
                            {
                                { "address", proxy.Server },
                                { "port", proxy.ServerPort },
                                { "users", new object[] { user } }
                            }
                        }
                    }
                };
            }
            else if (protocol == "trojan")
            {
                outbound["settings"] = new Dictionary<string, object>
                {
                    { "servers", new object[]
                        {
                            new Dictionary<string, object>
                            {
                                { "address", proxy.Server },
                                { "port", proxy.ServerPort },
                                { "password", secret }
                            }
                        }
                    }
                };
            }
            else
            {
                throw new InvalidOperationException("XHTTP is supported for VLESS, VMess, and Trojan profiles.");
            }
            return outbound;
        }

        private Dictionary<string, object> BuildStreamSettings(ProxySettings proxy)
        {
            Dictionary<string, object> stream = new Dictionary<string, object>
            {
                { "network", "xhttp" },
                { "security", NormalizeSecurity(proxy.TlsMode) },
                { "xhttpSettings", BuildXHttpSettings(proxy) }
            };

            string security = NormalizeSecurity(proxy.TlsMode);
            if (security == "tls") stream["tlsSettings"] = BuildTlsSettings(proxy);
            else if (security == "reality") stream["realitySettings"] = BuildRealitySettings(proxy);
            return stream;
        }

        private Dictionary<string, object> BuildXHttpSettings(ProxySettings proxy)
        {
            Dictionary<string, object> settings = new Dictionary<string, object>();
            if (!String.IsNullOrWhiteSpace(proxy.TransportHost)) settings["host"] = proxy.TransportHost;
            if (!String.IsNullOrWhiteSpace(proxy.Path)) settings["path"] = proxy.Path;
            if (!String.IsNullOrWhiteSpace(proxy.XHttpMode)) settings["mode"] = proxy.XHttpMode;
            object extra = ParseExtra(proxy.XHttpExtra);
            if (extra != null) settings["extra"] = extra;
            return settings;
        }

        private static Dictionary<string, object> BuildTlsSettings(ProxySettings proxy)
        {
            Dictionary<string, object> tls = new Dictionary<string, object>
            {
                { "serverName", String.IsNullOrWhiteSpace(proxy.ServerName) ? proxy.Server : proxy.ServerName },
                { "allowInsecure", proxy.AllowInsecure }
            };
            if (!String.IsNullOrWhiteSpace(proxy.Fingerprint)
                && !String.Equals(proxy.Fingerprint, "none", StringComparison.OrdinalIgnoreCase))
                tls["fingerprint"] = proxy.Fingerprint;
            string[] alpn = SplitList(proxy.Alpn);
            if (alpn.Length > 0) tls["alpn"] = alpn;
            return tls;
        }

        private static Dictionary<string, object> BuildRealitySettings(ProxySettings proxy)
        {
            if (String.IsNullOrWhiteSpace(proxy.RealityPublicKey))
                throw new InvalidOperationException("The Reality public key is missing from this XHTTP config.");
            Dictionary<string, object> reality = new Dictionary<string, object>
            {
                { "serverName", String.IsNullOrWhiteSpace(proxy.ServerName) ? proxy.Server : proxy.ServerName },
                { "publicKey", proxy.RealityPublicKey },
                { "shortId", proxy.RealityShortId ?? String.Empty },
                { "spiderX", String.Empty },
                { "fingerprint", String.IsNullOrWhiteSpace(proxy.Fingerprint) ? "chrome" : proxy.Fingerprint }
            };
            string[] alpn = SplitList(proxy.Alpn);
            if (alpn.Length > 0) reality["alpn"] = alpn;
            return reality;
        }

        private object ParseExtra(string value)
        {
            string clean = (value ?? String.Empty).Trim();
            if (clean.Length == 0) return null;
            if (clean.Length > MaximumExtraCharacters)
                throw new InvalidOperationException("The XHTTP extra settings exceed the 32 KB safety limit.");

            string json = clean;
            if (!clean.StartsWith("{", StringComparison.Ordinal))
            {
                string decoded;
                if (TryDecodeBase64(clean, out decoded)) json = decoded;
            }
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = MaximumExtraCharacters;
                object parsed = serializer.DeserializeObject(json);
                if (!(parsed is Dictionary<string, object>))
                    throw new InvalidOperationException("XHTTP extra settings must be a JSON object.");
                return parsed;
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception exception)
            {
                throw new InvalidOperationException("The XHTTP extra settings are not valid JSON: " + exception.Message);
            }
        }

        private static bool TryDecodeBase64(string value, out string decoded)
        {
            decoded = null;
            try
            {
                string clean = (value ?? String.Empty).Replace('-', '+').Replace('_', '/');
                int padding = clean.Length % 4;
                if (padding > 0) clean = clean.PadRight(clean.Length + 4 - padding, '=');
                byte[] bytes = Convert.FromBase64String(clean);
                decoded = Encoding.UTF8.GetString(bytes);
                Array.Clear(bytes, 0, bytes.Length);
                return !String.IsNullOrWhiteSpace(decoded);
            }
            catch { return false; }
        }

        private static string NormalizeSecurity(string value)
        {
            string security = (value ?? String.Empty).Trim().ToLowerInvariant();
            return security == "tls" || security == "reality" ? security : "none";
        }

        private static string[] SplitList(string value)
        {
            return (value ?? String.Empty)
                .Split(new char[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(delegate(string item) { return item.Trim(); })
                .Where(delegate(string item) { return item.Length > 0; })
                .ToArray();
        }
    }
}
