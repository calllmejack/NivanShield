using System;
using System.IO;
using System.Text;

namespace Nivan.Shield.Services
{
    internal static class SessionMarkerFile
    {
        internal static void Write(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A session marker path is required.", "path");

            // File.WriteAllText on .NET Framework cannot replace an existing
            // hidden or read-only file. A marker left by an unclean shutdown is
            // hidden by design, so make it writable before refreshing it.
            if (File.Exists(path))
            {
                FileAttributes attributes = File.GetAttributes(path);
                FileAttributes writableAttributes = attributes
                    & ~FileAttributes.Hidden
                    & ~FileAttributes.ReadOnly;
                if (writableAttributes == 0) writableAttributes = FileAttributes.Normal;
                if (writableAttributes != attributes) File.SetAttributes(path, writableAttributes);
            }

            File.WriteAllText(path, DateTime.UtcNow.ToString("O"), new UTF8Encoding(false));
            try { File.SetAttributes(path, FileAttributes.Hidden); }
            catch { }
        }
    }
}
