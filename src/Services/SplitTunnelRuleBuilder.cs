using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public static class SplitTunnelRuleBuilder
    {
        public static IList<object> BuildDirectRules(
            NetworkProtectionSettings settings,
            IEnumerable<string> requiredProcesses)
        {
            List<object> rules = new List<object>();
            string[] required = NormalizeItems(requiredProcesses ?? new string[0], false);
            if (required.Length > 0) rules.Add(ProcessRule(required));

            if (settings == null || !settings.EnableSplitTunneling) return rules;

            string[] processes = NormalizeItems(Split(settings.TunBypassProcesses), false);
            if (processes.Length > 0) rules.Add(ProcessRule(processes));

            string[] domains = NormalizeItems(Split(settings.TunBypassDomains), true);
            if (domains.Length > 0)
            {
                rules.Add(new Dictionary<string, object>
                {
                    { "domain_suffix", domains },
                    { "outbound", "direct" }
                });
            }

            string[] cidrs = NormalizeCidrs(Split(settings.TunBypassIpCidrs));
            if (cidrs.Length > 0)
            {
                rules.Add(new Dictionary<string, object>
                {
                    { "ip_cidr", cidrs },
                    { "outbound", "direct" }
                });
            }
            return rules;
        }

        public static object BuildProxyProcessRule(string processList, string outboundTag)
        {
            string[] processes = NormalizeItems(Split(processList), false);
            if (processes.Length == 0) return null;
            return new Dictionary<string, object>
            {
                { "process_name", processes },
                { "outbound", String.IsNullOrWhiteSpace(outboundTag) ? "proxy" : outboundTag }
            };
        }

        public static string NormalizeProcessList(string processList)
        {
            return String.Join(";", NormalizeItems(Split(processList), false));
        }

        private static Dictionary<string, object> ProcessRule(string[] processes)
        {
            return new Dictionary<string, object>
            {
                { "process_name", processes },
                { "outbound", "direct" }
            };
        }

        private static IEnumerable<string> Split(string value)
        {
            return (value ?? String.Empty).Split(
                new char[] { ',', ';', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries
            );
        }

        private static string[] NormalizeItems(IEnumerable<string> values, bool domain)
        {
            return values
                .Select(delegate(string item)
                {
                    string value = (item ?? String.Empty).Trim();
                    if (domain)
                    {
                        while (value.StartsWith("*.", StringComparison.Ordinal)) value = value.Substring(2);
                        value = value.TrimStart('.');
                    }
                    return value;
                })
                .Where(delegate(string item) { return item.Length > 0; })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(128)
                .ToArray();
        }

        private static string[] NormalizeCidrs(IEnumerable<string> values)
        {
            List<string> valid = new List<string>();
            foreach (string raw in values)
            {
                string value = (raw ?? String.Empty).Trim();
                if (value.Length == 0) continue;
                string[] parts = value.Split('/');
                IPAddress address;
                int prefix;
                if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out address)
                    || !Int32.TryParse(parts[1], out prefix)) continue;
                int maximum = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
                if (prefix < 0 || prefix > maximum) continue;
                if (!valid.Contains(value, StringComparer.OrdinalIgnoreCase)) valid.Add(value);
                if (valid.Count >= 128) break;
            }
            return valid.ToArray();
        }
    }
}
