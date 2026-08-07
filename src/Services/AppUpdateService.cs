using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Nivan.Shield.Services
{
    public sealed class AppUpdateInfo
    {
        public Version Version { get; set; }
        public Uri DownloadUri { get; set; }
        public string Sha256 { get; set; }
        public string Notes { get; set; }
        public bool IsNewer { get; set; }
    }

    public sealed class AppUpdateService
    {
        private const int MaximumManifestBytes = 128 * 1024;
        private const long MaximumPackageBytes = 500L * 1024L * 1024L;
        private readonly AppLogger _logger;

        public AppUpdateService(AppLogger logger)
        {
            _logger = logger;
        }

        public async Task<AppUpdateInfo> CheckAsync(string manifestAddress, Version currentVersion)
        {
            Uri manifestUri = await NetworkSecurityService.ValidateDownloadUriAsync(
                manifestAddress,
                true,
                true,
                "update manifest"
            ).ConfigureAwait(false);
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(manifestUri);
            request.Method = "GET";
            request.UserAgent = "NivanShield/6.0.5";
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.AllowAutoRedirect = false;

            string json;
            using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
            using (Stream input = response.GetResponseStream())
            using (MemoryStream output = new MemoryStream())
            {
                if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                    throw new InvalidOperationException("Update-manifest redirects are blocked. Configure the final HTTPS URL.");
                if (response.ContentLength > MaximumManifestBytes)
                    throw new InvalidOperationException("The update manifest is larger than the safety limit.");
                byte[] buffer = new byte[4096];
                try
                {
                    int total = 0;
                    while (true)
                    {
                        int read = await input.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                        if (read <= 0) break;
                        total += read;
                        if (total > MaximumManifestBytes)
                            throw new InvalidOperationException("The update manifest is larger than the safety limit.");
                        output.Write(buffer, 0, read);
                    }
                    json = Encoding.UTF8.GetString(output.ToArray());
                }
                finally { Array.Clear(buffer, 0, buffer.Length); }
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> manifest = serializer.Deserialize<Dictionary<string, object>>(json);
            if (manifest == null) throw new InvalidOperationException("The update manifest is empty.");
            string versionText = Value(manifest, "version");
            string downloadText = Value(manifest, "download_url");
            string sha256 = Value(manifest, "sha256").ToLowerInvariant();
            string notes = Value(manifest, "notes");
            Version version;
            if (!Version.TryParse(versionText, out version))
                throw new InvalidOperationException("The update manifest contains an invalid version.");
            Uri downloadUri = await NetworkSecurityService.ValidateDownloadUriAsync(
                downloadText,
                true,
                true,
                "update download"
            ).ConfigureAwait(false);
            if (sha256.Length != 64 || !IsHex(sha256))
                throw new InvalidOperationException("The update manifest must contain a valid SHA-256 hash.");

            AppUpdateInfo info = new AppUpdateInfo
            {
                Version = version,
                DownloadUri = downloadUri,
                Sha256 = sha256,
                Notes = notes,
                IsNewer = version.CompareTo(currentVersion ?? new Version(0, 0)) > 0
            };
            _logger.Info("Update check completed. Latest manifest version: " + version + ".");
            return info;
        }

        public async Task<string> DownloadPackageAsync(
            AppUpdateInfo info,
            string updateDirectory,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            if (info == null || info.DownloadUri == null)
                throw new ArgumentNullException("info");
            Directory.CreateDirectory(updateDirectory);
            string finalPath = Path.Combine(updateDirectory, "NivanShield-" + info.Version + ".zip");
            string temporaryPath = finalPath + ".download";
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);

            Uri verifiedDownloadUri = await NetworkSecurityService.ValidateDownloadUriAsync(
                info.DownloadUri.ToString(),
                true,
                true,
                "update download"
            ).ConfigureAwait(false);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(verifiedDownloadUri);
            request.Method = "GET";
            request.UserAgent = "NivanShield/6.0.5";
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;
            request.AllowAutoRedirect = false;
            try
            {
                using (CancellationTokenRegistration registration = cancellationToken.Register(delegate
                {
                    try { request.Abort(); } catch { }
                }))
                using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                using (Stream input = response.GetResponseStream())
                using (FileStream output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (SHA256 hash = SHA256.Create())
                {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                        throw new InvalidOperationException("Update-download redirects are blocked. Use the final HTTPS URL.");
                    if (response.ContentLength > MaximumPackageBytes)
                        throw new InvalidOperationException("The update package is larger than the 500 MB safety limit.");
                    byte[] buffer = new byte[64 * 1024];
                    long total = 0;
                    try
                    {
                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                            if (read <= 0) break;
                            total += read;
                            if (total > MaximumPackageBytes)
                                throw new InvalidOperationException("The update package is larger than the 500 MB safety limit.");
                            hash.TransformBlock(buffer, 0, read, buffer, 0);
                            await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                            if (progress != null && response.ContentLength > 0)
                                progress.Report((int)Math.Min(99, total * 100L / response.ContentLength));
                        }
                        hash.TransformFinalBlock(new byte[0], 0, 0);
                        string actualHash = ToHex(hash.Hash);
                        if (!String.Equals(actualHash, info.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("The downloaded update failed SHA-256 verification.");
                    }
                    finally { Array.Clear(buffer, 0, buffer.Length); }
                }

                if (File.Exists(finalPath)) File.Delete(finalPath);
                File.Move(temporaryPath, finalPath);
                if (progress != null) progress.Report(100);
                _logger.Info("Verified update package downloaded to " + finalPath + ".");
                return finalPath;
            }
            catch (WebException)
            {
                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
                throw;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try { File.Delete(temporaryPath); } catch { }
                }
            }
        }

        private static Uri RequireHttps(string value, string label)
        {
            Uri uri;
            if (!Uri.TryCreate((value ?? String.Empty).Trim(), UriKind.Absolute, out uri)
                || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("Enter a valid HTTPS " + label + " URL.");
            return uri;
        }

        private static string Value(IDictionary<string, object> dictionary, string key)
        {
            object value;
            return dictionary.TryGetValue(key, out value) && value != null ? value.ToString().Trim() : String.Empty;
        }

        private static bool IsHex(string value)
        {
            foreach (char character in value)
            {
                bool valid = (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F');
                if (!valid) return false;
            }
            return true;
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) builder.Append(value.ToString("x2"));
            return builder.ToString();
        }
    }
}
