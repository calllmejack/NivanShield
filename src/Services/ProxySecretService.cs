using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class ProxySecretService
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NivanShield|Proxy|v1");
        private readonly AppPaths _paths;
        private readonly AppLogger _logger;

        public ProxySecretService(AppPaths paths, AppLogger logger)
        {
            _paths = paths;
            _logger = logger;
        }

        public string GetPath(string profileId)
        {
            string safeId = String.IsNullOrWhiteSpace(profileId) ? "proxy" : profileId;
            foreach (char invalid in Path.GetInvalidFileNameChars()) safeId = safeId.Replace(invalid, '_');
            return Path.Combine(_paths.CredentialRoot, safeId + ".proxy.dpapi");
        }

        public bool Exists(string profileId)
        {
            return File.Exists(GetPath(profileId));
        }

        public void Save(string profileId, string secret)
        {
            if (String.IsNullOrEmpty(secret))
                throw new InvalidOperationException("The imported proxy credential is empty.");

            byte[] plain = null;
            byte[] encrypted = null;
            try
            {
                plain = Encoding.UTF8.GetBytes(secret);
                encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(GetPath(profileId), encrypted);
                _logger.Info("Imported proxy credential saved with Windows DPAPI protection.");
            }
            finally
            {
                if (plain != null) Array.Clear(plain, 0, plain.Length);
                if (encrypted != null) Array.Clear(encrypted, 0, encrypted.Length);
            }
        }

        public string Read(string profileId)
        {
            string path = GetPath(profileId);
            if (!File.Exists(path))
                throw new FileNotFoundException("The encrypted credential for this proxy profile is missing.", path);

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
                throw new InvalidOperationException("The proxy credential cannot be decrypted by this Windows account.");
            }
            finally
            {
                if (plain != null) Array.Clear(plain, 0, plain.Length);
                if (encrypted != null) Array.Clear(encrypted, 0, encrypted.Length);
            }
        }

        public bool Copy(string sourceProfileId, string targetProfileId)
        {
            string source = GetPath(sourceProfileId);
            if (!File.Exists(source)) return false;
            File.Copy(source, GetPath(targetProfileId), true);
            _logger.Info("Encrypted proxy credential copied for the duplicated profile.");
            return true;
        }

        public void Delete(string profileId)
        {
            string path = GetPath(profileId);
            if (File.Exists(path)) File.Delete(path);
            _logger.Info("Saved proxy credential removed.");
        }
    }
}
