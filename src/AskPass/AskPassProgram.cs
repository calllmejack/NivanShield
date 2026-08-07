using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Nivan.Shield.AskPass
{
    public static class AskPassProgram
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NivanTunnel|SSH|v1");

        [STAThread]
        public static int Main(string[] args)
        {
            string prompt = args != null && args.Length > 0 ? String.Join(" ", args) : String.Empty;
            string promptMode = Environment.GetEnvironmentVariable("SSH_ASKPASS_PROMPT");
            bool confirmation =
                String.Equals(promptMode, "confirm", StringComparison.OrdinalIgnoreCase) ||
                prompt.IndexOf("yes/no", StringComparison.OrdinalIgnoreCase) >= 0 ||
                prompt.IndexOf("fingerprint", StringComparison.OrdinalIgnoreCase) >= 0;

            if (confirmation) return WriteResponse("yes");

            string credentialPath = Environment.GetEnvironmentVariable("NIVAN_SHIELD_PASSWORD_FILE");
            if (String.IsNullOrWhiteSpace(credentialPath) || !File.Exists(credentialPath)) return 1;

            byte[] encrypted = null;
            byte[] plain = null;
            string password = null;
            try
            {
                encrypted = File.ReadAllBytes(credentialPath);
                plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                password = Encoding.UTF8.GetString(plain);
                return WriteResponse(password);
            }
            catch
            {
                return 1;
            }
            finally
            {
                password = null;
                if (plain != null) Array.Clear(plain, 0, plain.Length);
                if (encrypted != null) Array.Clear(encrypted, 0, encrypted.Length);
            }
        }

        private static int WriteResponse(string response)
        {
            if (response == null) return 1;
            using (Stream stream = Console.OpenStandardOutput())
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.WriteLine(response);
                writer.Flush();
            }
            return 0;
        }
    }
}
