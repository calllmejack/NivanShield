using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Nivan.Shield.Services
{
    public static class PrivateFileService
    {
        public static void WriteUtf8(string path, string content)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(content ?? String.Empty);
            try
            {
                using (FileStream stream = new FileStream(
                    path,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                RestrictToCurrentUser(path);
                try { File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.Temporary); }
                catch { }
            }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }

        private static void RestrictToCurrentUser(string path)
        {
            try
            {
                SecurityIdentifier user = WindowsIdentity.GetCurrent().User;
                if (user == null) return;
                FileSecurity security = new FileSecurity();
                security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(new FileSystemAccessRule(
                    user,
                    FileSystemRights.FullControl,
                    AccessControlType.Allow
                ));
                File.SetAccessControl(path, security);
            }
            catch
            {
                // The file remains inside the current user's LocalAppData even
                // when a restrictive ACL cannot be applied on a non-NTFS disk.
            }
        }
    }
}
