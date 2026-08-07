using System;
using System.IO;

namespace Nivan.Shield.Core
{
    public sealed class AppPaths
    {
        public AppPaths()
        {
            InstallRoot = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            DataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NivanShield"
            );
            LogRoot = Path.Combine(DataRoot, "logs");
            CredentialRoot = Path.Combine(DataRoot, "credentials");
            RuntimeRoot = Path.Combine(DataRoot, "runtime");
            UpdateRoot = Path.Combine(DataRoot, "updates");
            SettingsPath = Path.Combine(DataRoot, "settings.json");
            CredentialPath = Path.Combine(DataRoot, "ssh-password.dpapi");
            LogPath = Path.Combine(LogRoot, "nivan-shield-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
            AskPassPath = Path.Combine(InstallRoot, "NivanShield.exe");
            XamlPath = Path.Combine(InstallRoot, "app", "MainWindow.xaml");
            BundledSingBoxPath = Path.Combine(InstallRoot, "tools", "sing-box", "sing-box.exe");
            BundledNekoRayRoot = Path.Combine(InstallRoot, "tools", "nekoray");
            BundledNekoCorePath = Path.Combine(BundledNekoRayRoot, "nekobox_core.exe");
            BundledXrayPath = Path.Combine(InstallRoot, "tools", "xray", "xray.exe");
            SshRoutingConfigPath = Path.Combine(RuntimeRoot, "ssh-integrated-routing.json");
            SessionMarkerPath = Path.Combine(RuntimeRoot, "active-session.lock");
            DnsSnapshotPath = Path.Combine(RuntimeRoot, "dns-restore.json");
            PsiphonRuntimeConfigPath = Path.Combine(RuntimeRoot, "psiphon-client.json");
            PsiphonDataRoot = Path.Combine(DataRoot, "psiphon");
            BundledPsiphonRoot = Path.Combine(InstallRoot, "tools", "psiphon");
            BundledPsiphonPath = Path.Combine(BundledPsiphonRoot, "ConsoleClient.exe");
            BundledPsiphonConfigPath = Path.Combine(BundledPsiphonRoot, "client.config");
            IntegrityManifestPath = Path.Combine(InstallRoot, "tools", "integrity.sha256");

            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(LogRoot);
            Directory.CreateDirectory(CredentialRoot);
            Directory.CreateDirectory(RuntimeRoot);
            Directory.CreateDirectory(UpdateRoot);
            Directory.CreateDirectory(PsiphonDataRoot);
        }

        public string InstallRoot { get; private set; }
        public string DataRoot { get; private set; }
        public string LogRoot { get; private set; }
        public string CredentialRoot { get; private set; }
        public string RuntimeRoot { get; private set; }
        public string UpdateRoot { get; private set; }
        public string SettingsPath { get; private set; }
        public string CredentialPath { get; private set; }
        public string LogPath { get; private set; }
        public string AskPassPath { get; private set; }
        public string XamlPath { get; private set; }
        public string BundledSingBoxPath { get; private set; }
        public string BundledNekoRayRoot { get; private set; }
        public string BundledNekoCorePath { get; private set; }
        public string BundledXrayPath { get; private set; }
        public string SshRoutingConfigPath { get; private set; }
        public string SessionMarkerPath { get; private set; }
        public string DnsSnapshotPath { get; private set; }
        public string PsiphonRuntimeConfigPath { get; private set; }
        public string PsiphonDataRoot { get; private set; }
        public string BundledPsiphonRoot { get; private set; }
        public string BundledPsiphonPath { get; private set; }
        public string BundledPsiphonConfigPath { get; private set; }
        public string IntegrityManifestPath { get; private set; }

        public string GetSingBoxConfigPath(string profileId)
        {
            string safeId = String.IsNullOrWhiteSpace(profileId) ? "active" : profileId;
            foreach (char invalid in Path.GetInvalidFileNameChars()) safeId = safeId.Replace(invalid, '_');
            return Path.Combine(RuntimeRoot, "sing-box-" + safeId + ".json");
        }

        public string GetXrayConfigPath(string profileId)
        {
            string safeId = String.IsNullOrWhiteSpace(profileId) ? "active" : profileId;
            foreach (char invalid in Path.GetInvalidFileNameChars()) safeId = safeId.Replace(invalid, '_');
            return Path.Combine(RuntimeRoot, "xray-" + safeId + ".json");
        }

        public string GetSubscriptionSecretPath(string subscriptionId)
        {
            string safeId = String.IsNullOrWhiteSpace(subscriptionId) ? "subscription" : subscriptionId;
            foreach (char invalid in Path.GetInvalidFileNameChars()) safeId = safeId.Replace(invalid, '_');
            return Path.Combine(CredentialRoot, safeId + ".subscription.dpapi");
        }
    }
}
