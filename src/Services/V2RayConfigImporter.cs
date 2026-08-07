using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class ImportedProxyProfile
    {
        public ConnectionProfile Profile { get; set; }
        public string Secret { get; set; }
    }

    public sealed class ProxyImportResult
    {
        public ProxyImportResult()
        {
            Profiles = new List<ImportedProxyProfile>();
            Errors = new List<string>();
        }

        public List<ImportedProxyProfile> Profiles { get; private set; }
        public List<string> Errors { get; private set; }
    }

    public sealed class V2RayConfigImporter
    {
        private const int MaximumInputCharacters = 5 * 1024 * 1024;
        private const int MaximumLinkCharacters = 16 * 1024;
        private const int MaximumProfilesPerImport = 500;
        private static readonly Regex ShareLinkPattern = new Regex(
            @"(?i)(?:vmess|vless|trojan|ss)://[^\s""'<>]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );

        public ProxyImportResult ParseMany(string input, string category)
        {
            ProxyImportResult result = new ProxyImportResult();
            if (String.IsNullOrWhiteSpace(input))
            {
                result.Errors.Add("No configuration text was provided.");
                return result;
            }
            if (input.Length > MaximumInputCharacters)
            {
                result.Errors.Add("Configuration input exceeds the 5 MB safety limit.");
                return result;
            }

            List<string> links = ExtractLinks(input);
            if (links.Count == 0)
            {
                string decoded;
                if (TryDecodeBase64(input, out decoded)) links = ExtractLinks(decoded);
            }
            if (links.Count == 0)
            {
                result.Errors.Add("No supported VMess, VLESS, Trojan, or Shadowsocks links were found.");
                return result;
            }

            HashSet<string> fingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int index = 0;
            foreach (string link in links)
            {
                if (index >= MaximumProfilesPerImport)
                {
                    result.Errors.Add("Only the first 500 configurations were processed for safety.");
                    break;
                }
                index++;
                try
                {
                    if (link.Length > MaximumLinkCharacters)
                        throw new InvalidOperationException("The share link exceeds the 16 KB safety limit.");
                    ImportedProxyProfile imported = ParseOne(link, category);
                    string fingerprint = imported.Profile.Proxy.ImportFingerprint;
                    if (!fingerprints.Add(fingerprint)) continue;
                    result.Profiles.Add(imported);
                }
                catch (Exception exception)
                {
                    result.Errors.Add("Item " + index + ": " + exception.Message);
                }
            }
            return result;
        }

        public ImportedProxyProfile ParseOne(string link, string category)
        {
            string clean = (link ?? String.Empty).Trim().Trim('\uFEFF');
            if (clean.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
                return ParseVmess(clean, category);
            if (clean.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
                return ParseStandardUri(clean, category, "vless");
            if (clean.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase))
                return ParseStandardUri(clean, category, "trojan");
            if (clean.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
                return ParseShadowsocks(clean, category);
            throw new InvalidOperationException("Unsupported configuration type.");
        }

        private static ImportedProxyProfile ParseVmess(string link, string category)
        {
            string encoded = link.Substring("vmess://".Length).Trim();
            string json;
            if (!TryDecodeBase64(encoded, out json))
                throw new InvalidOperationException("The VMess payload is not valid Base64.");

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> values = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (values == null) throw new InvalidOperationException("The VMess payload is not valid JSON.");

            string server = Value(values, "add");
            int port = Integer(values, "port", 0);
            string secret = Value(values, "id");
            ValidateEndpointAndSecret(server, port, secret, "VMess");

            string transport = NormalizeTransport(Value(values, "net"));
            if (String.Equals(transport, "tcp", StringComparison.OrdinalIgnoreCase)
                && String.Equals(Value(values, "type"), "http", StringComparison.OrdinalIgnoreCase))
                transport = "http";
            string tls = Value(values, "tls");
            ProxySettings proxy = new ProxySettings
            {
                Protocol = "vmess",
                Server = server,
                ServerPort = port,
                LocalSocksPort = 0,
                Encryption = DefaultIfEmpty(Value(values, "scy"), "auto"),
                AlterId = Integer(values, "aid", 0),
                Transport = transport,
                TransportHost = Value(values, "host"),
                Path = Value(values, "path"),
                ServiceName = Value(values, "path"),
                TlsMode = String.IsNullOrWhiteSpace(tls) || String.Equals(tls, "none", StringComparison.OrdinalIgnoreCase)
                    ? "none"
                    : "tls",
                ServerName = Value(values, "sni"),
                Fingerprint = Value(values, "fp"),
                Alpn = Value(values, "alpn"),
                PacketEncoding = Value(values, "packetEncoding"),
                XHttpMode = Value(values, "mode"),
                XHttpExtra = Value(values, "extra"),
                WebSocketEarlyData = Integer(values, "ed", 0),
                EarlyDataHeaderName = Value(values, "eh"),
                ImportFingerprint = Fingerprint(RemoveFragment(link))
            };
            return BuildImported(
                proxy,
                secret,
                DefaultIfEmpty(Value(values, "ps"), "VMess " + server),
                category
            );
        }

        private static ImportedProxyProfile ParseStandardUri(string link, string category, string protocol)
        {
            Uri uri;
            if (!Uri.TryCreate(link, UriKind.Absolute, out uri))
                throw new InvalidOperationException("The " + protocol.ToUpperInvariant() + " link is not a valid URI.");

            string secret = Uri.UnescapeDataString(uri.UserInfo ?? String.Empty);
            ValidateEndpointAndSecret(uri.Host, uri.Port, secret, protocol.ToUpperInvariant());
            Dictionary<string, string> query = ParseQuery(uri.Query);
            string security = Query(query, "security");
            if (String.IsNullOrWhiteSpace(security)) security = Query(query, "tls");
            if (String.IsNullOrWhiteSpace(security)) security = "none";

            string transport = NormalizeTransport(Query(query, "type"));
            if (String.Equals(transport, "tcp", StringComparison.OrdinalIgnoreCase)
                && String.Equals(Query(query, "headerType"), "http", StringComparison.OrdinalIgnoreCase))
                transport = "http";
            string path = Query(query, "path");
            string serviceName = Query(query, "serviceName");
            if (String.IsNullOrWhiteSpace(serviceName) && String.Equals(transport, "grpc", StringComparison.OrdinalIgnoreCase))
                serviceName = path;

            ProxySettings proxy = new ProxySettings
            {
                Protocol = protocol,
                Server = uri.Host,
                ServerPort = uri.Port,
                LocalSocksPort = 0,
                Encryption = DefaultIfEmpty(Query(query, "encryption"), "none"),
                Flow = Query(query, "flow"),
                Transport = transport,
                TransportHost = Query(query, "host"),
                Path = path,
                ServiceName = serviceName,
                TlsMode = security.ToLowerInvariant(),
                ServerName = FirstNonEmpty(Query(query, "sni"), Query(query, "peer")),
                AllowInsecure = IsTrue(Query(query, "allowInsecure")) || IsTrue(Query(query, "insecure")),
                Fingerprint = Query(query, "fp"),
                RealityPublicKey = Query(query, "pbk"),
                RealityShortId = Query(query, "sid"),
                Alpn = Query(query, "alpn"),
                PacketEncoding = Query(query, "packetEncoding"),
                XHttpMode = FirstNonEmpty(Query(query, "mode"), Query(query, "xhttpMode")),
                XHttpExtra = FirstNonEmpty(Query(query, "extra"), Query(query, "xhttpExtra")),
                WebSocketEarlyData = ParseInteger(Query(query, "ed"), 0),
                EarlyDataHeaderName = Query(query, "eh"),
                ImportFingerprint = Fingerprint(RemoveFragment(link))
            };
            string name = DecodeFragment(uri.Fragment);
            if (String.IsNullOrWhiteSpace(name)) name = protocol.ToUpperInvariant() + " " + uri.Host;
            return BuildImported(proxy, secret, name, category);
        }

        private static ImportedProxyProfile ParseShadowsocks(string link, string category)
        {
            string payload = link.Substring("ss://".Length);
            string name = String.Empty;
            int fragmentIndex = payload.IndexOf('#');
            if (fragmentIndex >= 0)
            {
                name = DecodeComponent(payload.Substring(fragmentIndex + 1));
                payload = payload.Substring(0, fragmentIndex);
            }

            string queryText = String.Empty;
            int queryIndex = payload.IndexOf('?');
            if (queryIndex >= 0)
            {
                queryText = payload.Substring(queryIndex + 1);
                payload = payload.Substring(0, queryIndex);
            }
            payload = payload.TrimEnd('/');

            string credentials;
            string address;
            int atIndex = payload.LastIndexOf('@');
            if (atIndex >= 0)
            {
                string encodedCredentials = payload.Substring(0, atIndex);
                address = payload.Substring(atIndex + 1);
                credentials = DecodeBase64OrPlain(encodedCredentials);
            }
            else
            {
                string decoded = DecodeBase64OrPlain(payload);
                int decodedAt = decoded.LastIndexOf('@');
                if (decodedAt < 0) throw new InvalidOperationException("The Shadowsocks link has no server address.");
                credentials = decoded.Substring(0, decodedAt);
                address = decoded.Substring(decodedAt + 1);
            }

            credentials = DecodeComponent(credentials);
            int separator = credentials.IndexOf(':');
            if (separator <= 0) throw new InvalidOperationException("The Shadowsocks method or password is missing.");
            string method = credentials.Substring(0, separator);
            string secret = credentials.Substring(separator + 1);
            string host;
            int port;
            ParseHostPort(address, out host, out port);
            ValidateEndpointAndSecret(host, port, secret, "Shadowsocks");

            Dictionary<string, string> query = ParseQuery(queryText);
            string pluginValue = Query(query, "plugin");
            string plugin = pluginValue;
            string pluginOptions = String.Empty;
            int pluginSeparator = pluginValue.IndexOf(';');
            if (pluginSeparator >= 0)
            {
                plugin = pluginValue.Substring(0, pluginSeparator);
                pluginOptions = pluginValue.Substring(pluginSeparator + 1);
            }

            ProxySettings proxy = new ProxySettings
            {
                Protocol = "shadowsocks",
                Server = host,
                ServerPort = port,
                LocalSocksPort = 0,
                Encryption = method,
                Transport = "tcp",
                TlsMode = "none",
                Plugin = plugin,
                PluginOptions = pluginOptions,
                ImportFingerprint = Fingerprint(RemoveFragment(link))
            };
            if (String.IsNullOrWhiteSpace(name)) name = "Shadowsocks " + host;
            return BuildImported(proxy, secret, name, category);
        }

        private static ImportedProxyProfile BuildImported(
            ProxySettings proxy,
            string secret,
            string name,
            string category)
        {
            ConnectionProfile profile = new ConnectionProfile
            {
                Id = "proxy-" + Guid.NewGuid().ToString("N"),
                Name = CleanName(name),
                Category = String.IsNullOrWhiteSpace(category) ? "Imported" : category.Trim(),
                Engine = String.Equals(proxy.Transport, "xhttp", StringComparison.OrdinalIgnoreCase)
                    ? "xray"
                    : "sing-box",
                IsFavorite = false,
                Proxy = proxy,
                LastLatencyMilliseconds = -1,
                LastTestStatus = "Not tested"
            };
            return new ImportedProxyProfile { Profile = profile, Secret = secret };
        }

        private static List<string> ExtractLinks(string text)
        {
            List<string> links = new List<string>();
            foreach (Match match in ShareLinkPattern.Matches(text ?? String.Empty))
            {
                string value = match.Value.Trim().TrimEnd(',', ';', ']', '}', ')');
                if (!String.IsNullOrWhiteSpace(value)) links.Add(value);
            }
            return links;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string clean = (query ?? String.Empty).TrimStart('?');
            if (String.IsNullOrWhiteSpace(clean)) return values;
            foreach (string item in clean.Split('&'))
            {
                if (String.IsNullOrWhiteSpace(item)) continue;
                int separator = item.IndexOf('=');
                string key = separator < 0 ? item : item.Substring(0, separator);
                string value = separator < 0 ? String.Empty : item.Substring(separator + 1);
                values[DecodeComponent(key)] = DecodeComponent(value);
            }
            return values;
        }

        private static string Query(Dictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : String.Empty;
        }

        private static string Value(Dictionary<string, object> values, string key)
        {
            object value;
            if (!values.TryGetValue(key, out value) || value == null) return String.Empty;
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;
        }

        private static int Integer(Dictionary<string, object> values, string key, int fallback)
        {
            return ParseInteger(Value(values, key), fallback);
        }

        private static int ParseInteger(string value, int fallback)
        {
            int parsed;
            return Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : fallback;
        }

        private static string NormalizeTransport(string value)
        {
            string transport = DefaultIfEmpty(value, "tcp").Trim().ToLowerInvariant();
            if (transport == "h2") return "http";
            if (transport == "http-upgrade") return "httpupgrade";
            return transport;
        }

        private static string DecodeFragment(string fragment)
        {
            return DecodeComponent((fragment ?? String.Empty).TrimStart('#'));
        }

        private static string DecodeComponent(string value)
        {
            try { return Uri.UnescapeDataString((value ?? String.Empty).Replace("+", "%20")); }
            catch { return value ?? String.Empty; }
        }

        private static string DecodeBase64OrPlain(string value)
        {
            string decoded;
            return TryDecodeBase64(value, out decoded) ? decoded : value;
        }

        private static bool TryDecodeBase64(string value, out string decoded)
        {
            decoded = null;
            try
            {
                string clean = Regex.Replace((value ?? String.Empty).Trim().Trim('\uFEFF'), @"\s+", String.Empty)
                    .Replace('-', '+')
                    .Replace('_', '/');
                if (clean.Length == 0) return false;
                int padding = clean.Length % 4;
                if (padding > 0) clean = clean.PadRight(clean.Length + (4 - padding), '=');
                byte[] bytes = Convert.FromBase64String(clean);
                decoded = Encoding.UTF8.GetString(bytes);
                Array.Clear(bytes, 0, bytes.Length);
                return !String.IsNullOrWhiteSpace(decoded);
            }
            catch
            {
                decoded = null;
                return false;
            }
        }

        private static void ParseHostPort(string value, out string host, out int port)
        {
            string clean = (value ?? String.Empty).Trim();
            if (clean.StartsWith("[", StringComparison.Ordinal))
            {
                int close = clean.IndexOf(']');
                if (close < 0) throw new InvalidOperationException("The Shadowsocks IPv6 address is invalid.");
                host = clean.Substring(1, close - 1);
                if (close + 2 > clean.Length || clean[close + 1] != ':')
                    throw new InvalidOperationException("The Shadowsocks port is missing.");
                port = ParseInteger(clean.Substring(close + 2), 0);
                return;
            }
            int separator = clean.LastIndexOf(':');
            if (separator <= 0) throw new InvalidOperationException("The Shadowsocks server or port is missing.");
            host = clean.Substring(0, separator);
            port = ParseInteger(clean.Substring(separator + 1), 0);
        }

        private static void ValidateEndpointAndSecret(string host, int port, string secret, string label)
        {
            if (String.IsNullOrWhiteSpace(host))
                throw new InvalidOperationException(label + " server is missing.");
            if (port < 1 || port > 65535)
                throw new InvalidOperationException(label + " port is invalid.");
            if (String.IsNullOrWhiteSpace(secret))
                throw new InvalidOperationException(label + " credential is missing.");
            if (host.Length > 253)
                throw new InvalidOperationException(label + " server name is too long.");
            if (secret.Length > 4096)
                throw new InvalidOperationException(label + " credential is too long.");
        }

        private static string RemoveFragment(string value)
        {
            int index = (value ?? String.Empty).IndexOf('#');
            return index < 0 ? value : value.Substring(0, index);
        }

        private static string Fingerprint(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes((value ?? String.Empty).Trim());
            byte[] hash;
            using (SHA256 sha = SHA256.Create()) hash = sha.ComputeHash(bytes);
            Array.Clear(bytes, 0, bytes.Length);
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            foreach (byte item in hash) builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
            Array.Clear(hash, 0, hash.Length);
            return builder.ToString();
        }

        private static string CleanName(string value)
        {
            string clean = Regex.Replace(value ?? String.Empty, @"[\r\n\t]+", " ").Trim();
            if (clean.Length > 80) clean = clean.Substring(0, 80).Trim();
            return String.IsNullOrWhiteSpace(clean) ? "Imported proxy" : clean;
        }

        private static string DefaultIfEmpty(string value, string fallback)
        {
            return String.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string FirstNonEmpty(string first, string second)
        {
            return String.IsNullOrWhiteSpace(first) ? second : first;
        }

        private static bool IsTrue(string value)
        {
            return String.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || String.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
