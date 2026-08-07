using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class CredentialService
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NivanTunnel|SSH|v1");
        private readonly AppPaths _paths;
        private readonly AppLogger _logger;

        public CredentialService(AppPaths paths, AppLogger logger)
        {
            _paths = paths;
            _logger = logger;
        }

        public string GetPath(string profileId)
        {
            string safeId = String.IsNullOrWhiteSpace(profileId) ? "ssh-primary" : profileId;
            foreach (char invalid in Path.GetInvalidFileNameChars()) safeId = safeId.Replace(invalid, '_');
            return Path.Combine(_paths.CredentialRoot, safeId + ".dpapi");
        }

        public bool Exists(string profileId) { return File.Exists(GetPath(profileId)); }

        public void Save(string profileId, string password)
        {
            if (String.IsNullOrEmpty(password)) throw new InvalidOperationException("Enter the SSH password first.");

            byte[] plain = null;
            byte[] encrypted = null;
            try
            {
                plain = Encoding.UTF8.GetBytes(password);
                encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(GetPath(profileId), encrypted);
                _logger.Info("SSH password saved with Windows DPAPI protection.");
            }
            finally
            {
                if (plain != null) Array.Clear(plain, 0, plain.Length);
                if (encrypted != null) Array.Clear(encrypted, 0, encrypted.Length);
            }
        }

        public void Delete(string profileId)
        {
            string path = GetPath(profileId);
            if (File.Exists(path)) File.Delete(path);
            _logger.Info("Saved SSH password removed.");
        }

        public bool Copy(string sourceProfileId, string targetProfileId)
        {
            string source = GetPath(sourceProfileId);
            string target = GetPath(targetProfileId);
            if (!File.Exists(source)) return false;
            File.Copy(source, target, true);
            _logger.Info("Encrypted SSH credential copied for the duplicated profile.");
            return true;
        }

        public void MigrateLegacyCredentialIfNeeded(string profileId)
        {
            string target = GetPath(profileId);
            if (File.Exists(target)) return;

            try
            {
                string stageOnePath = _paths.CredentialPath;
                string tunnelPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NivanTunnel",
                    "ssh-password.dpapi"
                );
                string source = File.Exists(stageOnePath) ? stageOnePath : tunnelPath;
                if (File.Exists(source))
                {
                    File.Copy(source, target, false);
                    _logger.Info("Existing encrypted SSH password migrated from Nivan Tunnel.");
                }
            }
            catch (Exception exception)
            {
                _logger.Warning("The previous encrypted password could not be migrated: " + exception.Message);
            }
        }
    }
}
