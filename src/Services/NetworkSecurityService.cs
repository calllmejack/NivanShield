using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Nivan.Shield.Services
{
    public static class NetworkSecurityService
    {
        public static async Task<Uri> ValidateDownloadUriAsync(
            string value,
            bool requireHttps,
            bool blockPrivateTargets,
            string label)
        {
            Uri uri;
            if (!Uri.TryCreate((value ?? String.Empty).Trim(), UriKind.Absolute, out uri))
                throw new InvalidOperationException("Enter a valid " + label + " URL.");
            if (requireHttps && uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException(label + " must use HTTPS.");
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                throw new InvalidOperationException(label + " must use HTTP or HTTPS.");
            if (!String.IsNullOrWhiteSpace(uri.UserInfo))
                throw new InvalidOperationException(label + " URLs cannot contain embedded usernames or passwords.");
            if (uri.IsLoopback || String.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(label + " cannot target this computer.");
            if (uri.Port < 1 || uri.Port > 65535)
                throw new InvalidOperationException(label + " contains an invalid port.");

            if (blockPrivateTargets)
            {
                IPAddress[] addresses;
                try { addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost).ConfigureAwait(false); }
                catch { throw new InvalidOperationException(label + " host could not be resolved safely."); }
                if (addresses.Length == 0 || addresses.Any(IsUnsafeAddress))
                    throw new InvalidOperationException(label + " resolves to a local, private, or reserved network address.");
            }
            return uri;
        }

        private static bool IsUnsafeAddress(IPAddress address)
        {
            if (address == null || IPAddress.IsLoopback(address)
                || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
                || address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None)) return true;
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] bytes = address.GetAddressBytes();
                return bytes[0] == 0 || bytes[0] == 10 || bytes[0] == 127
                    || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                    || (bytes[0] == 169 && bytes[1] == 254)
                    || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    || (bytes[0] == 192 && bytes[1] == 168)
                    || (bytes[0] == 192 && bytes[1] == 0 && (bytes[2] == 0 || bytes[2] == 2))
                    || (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19 || (bytes[1] == 51 && bytes[2] == 100)))
                    || (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
                    || bytes[0] >= 224;
            }
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv4MappedToIPv6) return IsUnsafeAddress(address.MapToIPv4());
                if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal) return true;
                byte[] bytes = address.GetAddressBytes();
                return (bytes[0] & 0xFE) == 0xFC;
            }
            return true;
        }
    }
}
