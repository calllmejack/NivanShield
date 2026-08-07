using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class SubscriptionService
    {
        private const int MaximumBytes = 5 * 1024 * 1024;
        private readonly AppLogger _logger;

        public SubscriptionService(AppLogger logger)
        {
            _logger = logger;
        }

        public async Task<string> DownloadAsync(string address, SecuritySettings security)
        {
            Uri uri = await NetworkSecurityService.ValidateDownloadUriAsync(
                address,
                true,
                true,
                "subscription"
            ).ConfigureAwait(false);

            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
                request.Method = "GET";
                request.UserAgent = "NivanShield/6.0.5";
                request.Timeout = 15000;
                request.ReadWriteTimeout = 15000;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                request.AllowAutoRedirect = false;

                using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                using (Stream input = response.GetResponseStream())
                using (MemoryStream output = new MemoryStream())
                {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                        throw new InvalidOperationException("Subscription redirects are blocked for safety. Use the final HTTPS URL shown by the provider.");
                    if (response.ContentLength > MaximumBytes)
                        throw new InvalidOperationException("The subscription is larger than the 5 MB safety limit.");

                    byte[] buffer = new byte[8192];
                    try
                    {
                        int total = 0;
                        while (true)
                        {
                            int read = await input.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                            if (read <= 0) break;
                            total += read;
                            if (total > MaximumBytes)
                                throw new InvalidOperationException("The subscription is larger than the 5 MB safety limit.");
                            output.Write(buffer, 0, read);
                        }
                    }
                    finally { Array.Clear(buffer, 0, buffer.Length); }

                    byte[] contentBytes = output.ToArray();
                    try
                    {
                        string content = Encoding.UTF8.GetString(contentBytes);
                        _logger.Info("Proxy subscription downloaded successfully.");
                        return content;
                    }
                    finally { Array.Clear(contentBytes, 0, contentBytes.Length); }
                }
            }
            catch (WebException exception)
            {
                throw new InvalidOperationException("The subscription could not be downloaded: " + exception.Message);
            }
        }
    }
}
