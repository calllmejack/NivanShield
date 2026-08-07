using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class SubscriptionSecretService
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NivanShield|Subscription|v1");
        private readonly AppPaths _paths;
        private readonly AppLogger _logger;

        public SubscriptionSecretService(AppPaths paths, AppLogger logger)
        {
            _paths = paths;
            _logger = logger;
        }

        public bool Exists(string subscriptionId)
        {
            return File.Exists(_paths.GetSubscriptionSecretPath(subscriptionId));
        }

        public void Save(string subscriptionId, string address)
        {
            Uri uri;
            if (!Uri.TryCreate((address ?? String.Empty).Trim(), UriKind.Absolute, out uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("Enter a valid HTTP or HTTPS subscription URL.");

            byte[] plain = null;
            byte[] encrypted = null;
            try
            {
                plain = Encoding.UTF8.GetBytes(uri.AbsoluteUri);
                encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(_paths.GetSubscriptionSecretPath(subscriptionId), encrypted);
                _logger.Info("Subscription URL saved with Windows DPAPI protection.");
            }
            finally
            {
                if (plain != null) Array.Clear(plain, 0, plain.Length);
                if (encrypted != null) Array.Clear(encrypted, 0, encrypted.Length);
            }
        }

        public string Read(string subscriptionId)
        {
            string path = _paths.GetSubscriptionSecretPath(subscriptionId);
            if (!File.Exists(path))
                throw new FileNotFoundException("The protected subscription URL is missing.", path);
            byte[] encrypted = null;
            byte[] plain = null;
            try
            {
                encrypted = File.ReadAllBytes(path);
                plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (CryptographicException)
            {
                throw new InvalidOperationException("This Windows account cannot decrypt the saved subscription URL.");
            }
            finally
            {
                if (plain != null) Array.Clear(plain, 0, plain.Length);
                if (encrypted != null) Array.Clear(encrypted, 0, encrypted.Length);
            }
        }

        public void Delete(string subscriptionId)
        {
            string path = _paths.GetSubscriptionSecretPath(subscriptionId);
            if (File.Exists(path)) File.Delete(path);
            _logger.Info("Protected subscription URL removed.");
        }
    }
}
