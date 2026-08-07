using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Script.Serialization;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class SingBoxConfigBuilder
    {
        public string Build(
            ConnectionProfile profile,
            string secret,
            NekoRaySettings routing,
            NetworkProtectionSettings network)
        {
            if (profile == null || profile.IsSsh || profile.Proxy == null)
                throw new InvalidOperationException("Select a valid imported proxy profile.");
            string protocol = (profile.Proxy.Protocol ?? String.Empty).Trim().ToLowerInvariant();
            bool credentialRequired = protocol == "vmess" || protocol == "vless"
                || protocol == "trojan" || protocol == "shadowsocks";
            if (credentialRequired && String.IsNullOrWhiteSpace(secret))
                throw new InvalidOperationException("The encrypted credential for this proxy profile is empty.");

            ProxySettings proxy = profile.Proxy;
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["log"] = new Dictionary<string, object>
            {
                { "disabled", false },
                { "level", "info" },
                { "timestamp", true }
            };
            List<object> inbounds = new List<object>();
            inbounds.Add(
                new Dictionary<string, object>
                {
                    { "type", "mixed" },
                    { "tag", "mixed-in" },
                    { "listen", "127.0.0.1" },
                    { "listen_port", proxy.LocalSocksPort },
                    { "sniff", true },
                    { "sniff_override_destination", true }
                }
            );
            bool tunEnabled = routing != null && routing.EnableTunMode;
            bool selectedAppsMode = routing != null && String.Equals(
                routing.RoutingMode,
                RoutingModes.SelectedApps,
                StringComparison.OrdinalIgnoreCase
            );
            if (tunEnabled)
            {
                inbounds.Add(new Dictionary<string, object>
                {
                    { "type", "tun" },
                    { "tag", "tun-in" },
                    { "interface_name", "NivanShield-TUN" },
                    { "inet4_address", "172.19.0.1/28" },
                    { "mtu", 9000 },
                    { "auto_route", true },
                    { "strict_route", false },
                    { "stack", "mixed" },
                    { "endpoint_independent_nat", true },
                    { "sniff", false }
                });
            }
            root["inbounds"] = inbounds.ToArray();

            List<object> outbounds = new List<object>();
            outbounds.Add(BuildOutbound(proxy, secret));
            outbounds.Add(
                new Dictionary<string, object>
                {
                    { "type", "direct" },
                    { "tag", "direct" }
                }
            );
            if (tunEnabled)
            {
                outbounds.Add(new Dictionary<string, object>
                {
                    { "type", "dns" },
                    { "tag", "dns-out" }
                });
                outbounds.Add(new Dictionary<string, object>
                {
                    { "type", "block" },
                    { "tag", "block" }
                });
            }
            Dictionary<string, object> dns = new Dictionary<string, object>
            {
                { "servers", new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "tag", "dns-bootstrap" },
                            { "address", "local" },
                            { "detour", "direct" }
                        },
                        new Dictionary<string, object>
                        {
                            { "tag", "dns-remote" },
                            // Avoid a poisoned/circular Windows DNS lookup for
                            // the DoH provider itself. The proxy server domain
                            // still uses dns-bootstrap through the exact rule
                            // below, while all user DNS uses this routed IP DoH.
                            { "address", "https://1.1.1.1/dns-query" },
                            { "address_strategy", "prefer_ipv4" },
                            { "detour", "proxy" }
                        }
                    }
                },
                { "final", "dns-remote" },
                { "strategy", "prefer_ipv4" }
            };
            IPAddress serverAddress;
            if (!IPAddress.TryParse(proxy.Server, out serverAddress))
            {
                // The VPN server must be resolved before the VPN outbound can
                // carry remote DoH. Resolving only this exact host with the
                // bootstrap resolver prevents a circular DNS dependency.
                dns["rules"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        { "domain", new string[] { proxy.Server } },
                        { "server", "dns-bootstrap" }
                    }
                };
            }
            root["dns"] = dns;
            root["outbounds"] = outbounds.ToArray();

            List<object> routeRules = new List<object>();
            if (tunEnabled)
            {
                // Explicit mixed-proxy clients take precedence over TUN
                // process bypasses. This makes the browser-path verification
                // exercise the real proxy instead of the direct connection.
                routeRules.Add(new Dictionary<string, object>
                {
                    { "inbound", new string[] { "mixed-in" } },
                    { "outbound", "proxy" }
                });
            }
            routeRules.AddRange(SplitTunnelRuleBuilder.BuildDirectRules(
                network,
                new string[] { "NivanShield.exe", "nekobox_core.exe", "sing-box.exe" }
            ));
            if (tunEnabled)
            {
                object selectedProcessRule = selectedAppsMode
                    ? SplitTunnelRuleBuilder.BuildProxyProcessRule(routing.SelectedAppProcesses, "proxy")
                    : null;
                if (selectedProcessRule != null) routeRules.Add(selectedProcessRule);
                routeRules.Add(new Dictionary<string, object>
                {
                    { "protocol", "dns" },
                    { "outbound", "dns-out" }
                });
                routeRules.Add(new Dictionary<string, object>
                {
                    { "port", 53 },
                    { "outbound", "dns-out" }
                });
                routeRules.Add(new Dictionary<string, object>
                {
                    { "network", "udp" },
                    { "port", 443 },
                    { "outbound", "block" }
                });
                routeRules.Add(new Dictionary<string, object>
                {
                    { "ip_cidr", new string[]
                        {
                            "127.0.0.0/8",
                            "10.0.0.0/8",
                            "172.16.0.0/12",
                            "192.168.0.0/16"
                        }
                    },
                    { "outbound", "direct" }
                });
            }
            Dictionary<string, object> route = new Dictionary<string, object>
            {
                { "final", selectedAppsMode ? "direct" : "proxy" },
                { "auto_detect_interface", true }
            };
            if (routeRules.Count > 0) route["rules"] = routeRules.ToArray();
            root["route"] = route;

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 4 * 1024 * 1024;
            return serializer.Serialize(root);
        }

        private static Dictionary<string, object> BuildOutbound(ProxySettings proxy, string secret)
        {
            string protocol = (proxy.Protocol ?? String.Empty).Trim().ToLowerInvariant();
            Dictionary<string, object> outbound = new Dictionary<string, object>
            {
                { "type", protocol },
                { "tag", "proxy" },
                { "server", proxy.Server },
                { "server_port", proxy.ServerPort },
                { "domain_strategy", "prefer_ipv4" }
            };

            if (protocol == "vmess")
            {
                outbound["uuid"] = secret;
                outbound["security"] = String.IsNullOrWhiteSpace(proxy.Encryption) ? "auto" : proxy.Encryption;
                outbound["alter_id"] = Math.Max(0, proxy.AlterId);
                AddTlsAndTransport(outbound, proxy);
            }
            else if (protocol == "vless")
            {
                outbound["uuid"] = secret;
                if (!String.IsNullOrWhiteSpace(proxy.Flow)) outbound["flow"] = proxy.Flow;
                if (!String.IsNullOrWhiteSpace(proxy.PacketEncoding)) outbound["packet_encoding"] = proxy.PacketEncoding;
                AddTlsAndTransport(outbound, proxy);
            }
            else if (protocol == "trojan")
            {
                outbound["password"] = secret;
                AddTlsAndTransport(outbound, proxy);
            }
            else if (protocol == "shadowsocks")
            {
                outbound["method"] = proxy.Encryption;
                outbound["password"] = secret;
                if (!String.IsNullOrWhiteSpace(proxy.Plugin)) outbound["plugin"] = proxy.Plugin;
                if (!String.IsNullOrWhiteSpace(proxy.PluginOptions)) outbound["plugin_opts"] = proxy.PluginOptions;
            }
            else if (protocol == "socks" || protocol == "socks5")
            {
                outbound["type"] = "socks";
                outbound["version"] = "5";
                if (!String.IsNullOrWhiteSpace(proxy.Username)) outbound["username"] = proxy.Username;
                if (!String.IsNullOrWhiteSpace(secret)) outbound["password"] = secret;
            }
            else if (protocol == "http" || protocol == "https")
            {
                outbound["type"] = "http";
                if (!String.IsNullOrWhiteSpace(proxy.Username)) outbound["username"] = proxy.Username;
                if (!String.IsNullOrWhiteSpace(secret)) outbound["password"] = secret;
                if (protocol == "https")
                {
                    outbound["tls"] = new Dictionary<string, object>
                    {
                        { "enabled", true },
                        { "server_name", String.IsNullOrWhiteSpace(proxy.ServerName) ? proxy.Server : proxy.ServerName },
                        { "insecure", false }
                    };
                }
            }
            else
            {
                throw new InvalidOperationException("Unsupported sing-box protocol: " + protocol);
            }
            return outbound;
        }

        private static void AddTlsAndTransport(Dictionary<string, object> outbound, ProxySettings proxy)
        {
            Dictionary<string, object> tls = BuildTls(proxy);
            if (tls != null) outbound["tls"] = tls;
            Dictionary<string, object> transport = BuildTransport(proxy);
            if (transport != null) outbound["transport"] = transport;
        }

        private static Dictionary<string, object> BuildTls(ProxySettings proxy)
        {
            string mode = (proxy.TlsMode ?? String.Empty).Trim().ToLowerInvariant();
            if (mode != "tls" && mode != "reality") return null;

            Dictionary<string, object> tls = new Dictionary<string, object>
            {
                { "enabled", true }
            };
            if (!String.IsNullOrWhiteSpace(proxy.ServerName)) tls["server_name"] = proxy.ServerName;
            if (proxy.AllowInsecure) tls["insecure"] = true;

            string[] alpn = SplitList(proxy.Alpn);
            if (alpn.Length > 0) tls["alpn"] = alpn;

            if (!String.IsNullOrWhiteSpace(proxy.Fingerprint)
                && !String.Equals(proxy.Fingerprint, "none", StringComparison.OrdinalIgnoreCase))
            {
                tls["utls"] = new Dictionary<string, object>
                {
                    { "enabled", true },
                    { "fingerprint", proxy.Fingerprint }
                };
            }

            if (mode == "reality")
            {
                if (String.IsNullOrWhiteSpace(proxy.RealityPublicKey))
                    throw new InvalidOperationException("The Reality public key is missing.");
                tls["reality"] = new Dictionary<string, object>
                {
                    { "enabled", true },
                    { "public_key", proxy.RealityPublicKey },
                    { "short_id", proxy.RealityShortId ?? String.Empty }
                };
            }
            return tls;
        }

        private static Dictionary<string, object> BuildTransport(ProxySettings proxy)
        {
            string type = (proxy.Transport ?? "tcp").Trim().ToLowerInvariant();
            if (type == "tcp" || type == "none" || type.Length == 0) return null;

            Dictionary<string, object> transport = new Dictionary<string, object>
            {
                { "type", type }
            };
            if (type == "ws")
            {
                if (!String.IsNullOrWhiteSpace(proxy.Path)) transport["path"] = proxy.Path;
                if (!String.IsNullOrWhiteSpace(proxy.TransportHost))
                {
                    transport["headers"] = new Dictionary<string, object>
                    {
                        { "Host", proxy.TransportHost }
                    };
                }
                if (proxy.WebSocketEarlyData > 0)
                {
                    transport["max_early_data"] = proxy.WebSocketEarlyData;
                    if (!String.IsNullOrWhiteSpace(proxy.EarlyDataHeaderName))
                        transport["early_data_header_name"] = proxy.EarlyDataHeaderName;
                }
            }
            else if (type == "grpc")
            {
                if (!String.IsNullOrWhiteSpace(proxy.ServiceName))
                    transport["service_name"] = proxy.ServiceName;
            }
            else if (type == "http")
            {
                if (!String.IsNullOrWhiteSpace(proxy.TransportHost))
                    transport["host"] = SplitList(proxy.TransportHost);
                if (!String.IsNullOrWhiteSpace(proxy.Path)) transport["path"] = proxy.Path;
            }
            else if (type == "httpupgrade")
            {
                if (!String.IsNullOrWhiteSpace(proxy.TransportHost)) transport["host"] = proxy.TransportHost;
                if (!String.IsNullOrWhiteSpace(proxy.Path)) transport["path"] = proxy.Path;
            }
            else if (type != "quic")
            {
                throw new InvalidOperationException("Unsupported V2Ray transport for sing-box: " + type);
            }
            return transport;
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
