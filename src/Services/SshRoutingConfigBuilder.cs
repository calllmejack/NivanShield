using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class SshRoutingConfigBuilder
    {
        public string Build(
            NekoRaySettings routing,
            TunnelSettings tunnel,
            NetworkProtectionSettings network)
        {
            if (routing == null) throw new ArgumentNullException("routing");
            if (tunnel == null) throw new ArgumentNullException("tunnel");
            if (routing.MixedPort < 1 || routing.MixedPort > 65535)
                throw new InvalidOperationException("The integrated proxy port is invalid.");
            if (tunnel.SocksPort < 1 || tunnel.SocksPort > 65535)
                throw new InvalidOperationException("The SSH SOCKS port is invalid.");
            if (routing.MixedPort == tunnel.SocksPort)
                throw new InvalidOperationException("The integrated proxy port must be different from the SSH SOCKS port.");

            List<object> inbounds = new List<object>();
            inbounds.Add(new Dictionary<string, object>
            {
                { "type", "mixed" },
                { "tag", "mixed-in" },
                { "listen", "127.0.0.1" },
                { "listen_port", routing.MixedPort },
                { "sniff", true },
                { "sniff_override_destination", true }
            });

            bool selectedAppsMode = String.Equals(
                routing.RoutingMode,
                RoutingModes.SelectedApps,
                StringComparison.OrdinalIgnoreCase
            );
            if (routing.EnableTunMode)
            {
                inbounds.Add(new Dictionary<string, object>
                {
                    { "type", "tun" },
                    { "tag", "tun-in" },
                    { "interface_name", "NivanShield-TUN" },
                    { "inet4_address", "172.19.0.1/28" },
                    { "mtu", 9000 },
                    { "auto_route", true },
                    // Match NekoRay's compatibility-first Windows default. Users
                    // still get DNS through the routed resolver below.
                    { "strict_route", false },
                    { "stack", "mixed" },
                    { "endpoint_independent_nat", true },
                    { "sniff", false }
                });
            }

            Dictionary<string, object> root = new Dictionary<string, object>();
            root["log"] = new Dictionary<string, object>
            {
                { "disabled", false },
                { "level", "info" },
                { "timestamp", true }
            };
            root["dns"] = new Dictionary<string, object>
            {
                { "servers", new object[]
                    {
                        // Use an IP-addressed DoH endpoint so Windows/ISP DNS
                        // cannot poison the bootstrap lookup (for example by
                        // resolving dns.google to a private 10.x address).
                        // The HTTPS session still travels through the active
                        // SSH/V2Ray/Psiphon SOCKS outbound.
                        new Dictionary<string, object>
                        {
                            { "tag", "dns-remote" },
                            { "address", "https://1.1.1.1/dns-query" },
                            { "address_strategy", "prefer_ipv4" },
                            { "detour", "ssh-out" }
                        }
                    }
                },
                { "final", "dns-remote" },
                { "strategy", "prefer_ipv4" }
            };
            root["inbounds"] = inbounds.ToArray();
            root["outbounds"] = new object[]
            {
                new Dictionary<string, object>
                {
                    { "type", "socks" },
                    { "tag", "ssh-out" },
                    { "server", "127.0.0.1" },
                    { "server_port", tunnel.SocksPort },
                    { "version", "5" }
                },
                new Dictionary<string, object>
                {
                    { "type", "direct" },
                    { "tag", "direct" }
                },
                new Dictionary<string, object>
                {
                    { "type", "dns" },
                    { "tag", "dns-out" }
                },
                new Dictionary<string, object>
                {
                    { "type", "block" },
                    { "tag", "block" }
                }
            };
            List<object> routeRules = new List<object>();
            // Explicit clients of the local mixed proxy must always use the
            // upstream tunnel. Keep this before process-bypass rules so the
            // app's own end-to-end verification cannot accidentally test the
            // direct Windows connection.
            routeRules.Add(new Dictionary<string, object>
            {
                { "inbound", new string[] { "mixed-in" } },
                { "outbound", "ssh-out" }
            });
            routeRules.AddRange(SplitTunnelRuleBuilder.BuildDirectRules(
                network,
                new string[] { "ssh.exe", "xray.exe", "ConsoleClient.exe", "psiphon3.exe", "NivanShield.exe", "nekobox_core.exe" }
            ));
            object selectedProcessRule = selectedAppsMode
                ? SplitTunnelRuleBuilder.BuildProxyProcessRule(routing.SelectedAppProcesses, "ssh-out")
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
            root["route"] = new Dictionary<string, object>
            {
                { "final", selectedAppsMode ? "direct" : "ssh-out" },
                { "auto_detect_interface", true },
                { "rules", routeRules.ToArray() }
            };

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 1024 * 1024;
            return serializer.Serialize(root);
        }
    }
}
