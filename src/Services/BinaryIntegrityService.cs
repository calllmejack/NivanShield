using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Nivan.Shield.Services
{
    public sealed class BinaryIntegrityService
    {
        private const string TrustedBundledNekoCoreSha256 = "b365388ee5f53edd453a7a461e3f58e05c63eebcc39c681070f05f2eacfd4c6d";
        private const string TrustedBundledXraySha256 = "15c2d007954ac53ba69b80ec91242786b3c0b71d52649165b4ca1d5cc96ef8f1";

        public string ComputeSha256(string path)
        {
            string fullPath = RequireExecutable(path);
            return ComputeFileHash(fullPath);
        }

        public string ComputeFileSha256(string path)
        {
            return ComputeFileHash(RequireLocalRegularFile(path, "file"));
        }

        private static string ComputeFileHash(string fullPath)
        {
            using (FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 hash = SHA256.Create())
                return ToHex(hash.ComputeHash(stream));
        }

        public void VerifyPinned(string path, string expectedSha256, string label)
        {
            string expected = NormalizeHash(expectedSha256);
            if (expected.Length != 64)
                throw new InvalidOperationException(label + " has not been approved yet. Select the official file and save its fingerprint first.");
            string actual = ComputeSha256(path);
            if (!String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(label + " failed its SHA-256 integrity check. The file may have been replaced or modified.");
        }

        public void VerifyFilePinned(string path, string expectedSha256, string label)
        {
            string expected = NormalizeHash(expectedSha256);
            if (expected.Length != 64)
                throw new InvalidOperationException(label + " has not been approved yet.");
            string actual = ComputeFileSha256(path);
            if (!String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(label + " failed its SHA-256 integrity check. The file may have been replaced or modified.");
        }

        public void VerifyBundled(string path, string manifestPath, string relativeName)
        {
            if (!File.Exists(manifestPath))
                throw new InvalidOperationException("The bundled-core integrity manifest is missing.");
            string normalizedName = (relativeName ?? String.Empty).Replace('\\', '/').TrimStart('/');
            string expected = String.Empty;
            foreach (string rawLine in File.ReadAllLines(manifestPath, Encoding.ASCII))
            {
                string line = (rawLine ?? String.Empty).Trim();
                if (line.Length < 66 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int separator = line.IndexOfAny(new char[] { ' ', '\t' });
                if (separator <= 0) continue;
                string name = line.Substring(separator).Trim().TrimStart('*').Replace('\\', '/');
                if (String.Equals(name, normalizedName, StringComparison.OrdinalIgnoreCase))
                {
                    expected = line.Substring(0, separator).Trim();
                    break;
                }
            }
            if (expected.Length != 64)
                throw new InvalidOperationException("No trusted fingerprint exists for " + relativeName + ".");
            if (String.Equals(normalizedName, "tools/nekoray/nekobox_core.exe", StringComparison.OrdinalIgnoreCase))
            {
                if (!String.Equals(expected, TrustedBundledNekoCoreSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The bundled-core integrity manifest was modified.");
                expected = TrustedBundledNekoCoreSha256;
            }
            else if (String.Equals(normalizedName, "tools/xray/xray.exe", StringComparison.OrdinalIgnoreCase))
            {
                if (!String.Equals(expected, TrustedBundledXraySha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The bundled Xray-core integrity manifest was modified.");
                expected = TrustedBundledXraySha256;
            }
            VerifyPinned(path, expected, "The bundled connection core");
        }

        public async Task VerifyPsiphonPublisherAsync(string path)
        {
            await VerifyPublisherAsync(path, "Psiphon").ConfigureAwait(false);
        }

        public async Task VerifyPublisherAsync(string path, string expectedPublisher)
        {
            string fullPath = RequireExecutable(path);
            string expected = (expectedPublisher ?? String.Empty).Trim();
            if (expected.Length < 3 || expected.Length > 80)
                throw new InvalidOperationException("The expected publisher name is invalid.");
            const string command = "$s=Get-AuthenticodeSignature -LiteralPath $args[0];"
                + "if($s.Status -ne 'Valid'){Write-Error ('Signature status: '+$s.Status);exit 17};"
                + "if($null -eq $s.SignerCertificate){Write-Error 'Missing signer certificate';exit 18};"
                + "Write-Output $s.SignerCertificate.Subject";
            ProcessResult result = await ProcessTools.RunHiddenAsync(
                "powershell.exe",
                "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -Command "
                + ProcessTools.Quote(command) + " " + ProcessTools.Quote(fullPath)
            ).ConfigureAwait(false);
            string subject = (result.StandardOutput ?? String.Empty).Trim();
            if (result.ExitCode != 0 || subject.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException(
                    "The selected executable is not signed by the expected publisher (" + expected + ")."
                );
        }

        private static string RequireExecutable(string path)
        {
            string fullPath = RequireLocalRegularFile(path, "executable");
            if (!String.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only Windows .exe files can be selected as a connection core.");
            return fullPath;
        }

        private static string RequireLocalRegularFile(string path, string label)
        {
            string value = (path ?? String.Empty).Trim();
            if (String.IsNullOrWhiteSpace(value) || !File.Exists(value))
                throw new FileNotFoundException("The selected " + label + " was not found.", value);
            string fullPath = Path.GetFullPath(value);
            if (fullPath.StartsWith("\\\\", StringComparison.Ordinal)
                || (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Selected files must be regular files on a local disk, not network or reparse-point paths.");
            return fullPath;
        }

        private static string NormalizeHash(string value)
        {
            string normalized = (value ?? String.Empty).Trim().Replace(" ", String.Empty).ToLowerInvariant();
            foreach (char character in normalized)
            {
                bool valid = (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f');
                if (!valid) return String.Empty;
            }
            return normalized;
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) builder.Append(value.ToString("x2"));
            return builder.ToString();
        }
    }
}
