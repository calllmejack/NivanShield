using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Nivan.Shield.Services
{
    public sealed class SystemProxyService
    {
        private const int InternetOptionRefresh = 37;
        private const int InternetOptionSettingsChanged = 39;
        private const int InternetOptionPerConnectionOption = 75;
        private const int InternetOptionProxySettingsChanged = 95;
        private const int InternetPerConnectionFlags = 1;
        private const int ProxyTypeDirect = 1;
        private const int ProxyTypeProxy = 2;
        private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        private readonly AppLogger _logger;

        public SystemProxyService(AppLogger logger)
        {
            _logger = logger;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct InternetPerConnectionOptionList
        {
            public int Size;
            public IntPtr Connection;
            public int OptionCount;
            public int OptionError;
            public IntPtr Options;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct OptionValue
        {
            [FieldOffset(0)] public int IntegerValue;
            [FieldOffset(0)] public IntPtr StringValue;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct InternetPerConnectionOption
        {
            public int Option;
            public OptionValue Value;
        }

        [DllImport("wininet.dll", EntryPoint = "InternetQueryOptionW", SetLastError = true)]
        private static extern bool InternetQueryOption(
            IntPtr internet,
            int option,
            ref InternetPerConnectionOptionList buffer,
            ref int bufferLength
        );

        [DllImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)]
        private static extern bool InternetSetOptionList(
            IntPtr internet,
            int option,
            ref InternetPerConnectionOptionList buffer,
            int bufferLength
        );

        [DllImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)]
        private static extern bool InternetSetOptionNotify(
            IntPtr internet,
            int option,
            IntPtr buffer,
            int bufferLength
        );

        public void DisableLanProxy()
        {
            int optionSize = Marshal.SizeOf(typeof(InternetPerConnectionOption));
            IntPtr optionPointer = Marshal.AllocCoTaskMem(optionSize);
            try
            {
                InternetPerConnectionOption option = new InternetPerConnectionOption();
                option.Option = InternetPerConnectionFlags;
                Marshal.StructureToPtr(option, optionPointer, false);

                InternetPerConnectionOptionList list = new InternetPerConnectionOptionList();
                list.Size = Marshal.SizeOf(typeof(InternetPerConnectionOptionList));
                list.Connection = IntPtr.Zero;
                list.OptionCount = 1;
                list.OptionError = 0;
                list.Options = optionPointer;
                int listSize = list.Size;

                if (!InternetQueryOption(IntPtr.Zero, InternetOptionPerConnectionOption, ref list, ref listSize))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                option = (InternetPerConnectionOption)Marshal.PtrToStructure(optionPointer, typeof(InternetPerConnectionOption));
                option.Value.IntegerValue = (option.Value.IntegerValue | ProxyTypeDirect) & ~ProxyTypeProxy;
                Marshal.StructureToPtr(option, optionPointer, false);

                if (!InternetSetOptionList(IntPtr.Zero, InternetOptionPerConnectionOption, ref list, listSize))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                InternetSetOptionNotify(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
                InternetSetOptionNotify(IntPtr.Zero, InternetOptionProxySettingsChanged, IntPtr.Zero, 0);
                InternetSetOptionNotify(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
                _logger.Info("Windows LAN proxy checkbox disabled after disconnect.");
            }
            finally
            {
                Marshal.FreeCoTaskMem(optionPointer);
            }
        }

        public void ClearLanProxyConfiguration()
        {
            // Nivan only owns the manual WinINET proxy values below. Leave PAC,
            // VPN adapters, and unrelated corporate Windows settings untouched.
            try { DisableLanProxy(); }
            catch (Exception exception)
            {
                // Registry cleanup below is the reliable fallback when a damaged
                // WinINET per-connection structure cannot be queried.
                _logger.Warning("WinINET proxy flags could not be queried during Emergency Reset: " + exception.Message);
            }
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, true))
            {
                if (key == null) throw new InvalidOperationException("Windows Internet Settings could not be opened.");
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                key.DeleteValue("ProxyServer", false);
                key.DeleteValue("ProxyOverride", false);
            }
            NotifySettingsChanged();
            _logger.Warning("Nivan-managed Windows LAN proxy values were cleared by Emergency Reset.");
        }

        public void EnableLanHttpProxy(string host, int port)
        {
            EnableLanHttpProxy(host, port, "<local>");
        }

        public void EnableLanHttpProxy(string host, int port, string bypassList)
        {
            if (String.IsNullOrWhiteSpace(host)) throw new ArgumentException("Proxy host is required.", "host");
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException("port");

            SetLanProxy(host, port, bypassList);
            _logger.Info("Windows LAN proxy enabled at " + host + ":" + port + ".");
        }

        public void EnableNetworkLock(string bypassList)
        {
            SetLanProxy("127.0.0.1", 9, bypassList);
            _logger.Warning(
                "Proxy-aware Windows apps were locked because the VPN disconnected unexpectedly."
            );
        }

        public bool IsLanProxyEnabled(string expectedServer, out string actualServer)
        {
            actualServer = String.Empty;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, false))
                {
                    if (key == null) return false;
                    int enabled = Convert.ToInt32(key.GetValue("ProxyEnable", 0));
                    actualServer = Convert.ToString(key.GetValue("ProxyServer", String.Empty)).Trim();
                    return enabled == 1 && String.Equals(actualServer, expectedServer, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        private void SetLanProxy(string host, int port, string bypassList)
        {
            string normalizedBypass = NormalizeBypassList(bypassList);

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, true))
            {
                if (key == null) throw new InvalidOperationException("Windows Internet Settings could not be opened.");
                key.SetValue("ProxyServer", host + ":" + port, RegistryValueKind.String);
                key.SetValue("ProxyOverride", normalizedBypass, RegistryValueKind.String);
                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            }
            NotifySettingsChanged();
        }

        private static string NormalizeBypassList(string value)
        {
            string normalized = (value ?? String.Empty)
                .Replace("\r", ";")
                .Replace("\n", ";")
                .Replace(",", ";")
                .Trim(' ', ';');
            while (normalized.Contains(";;")) normalized = normalized.Replace(";;", ";");
            if (String.IsNullOrWhiteSpace(normalized)) normalized = "<local>";
            if (normalized.Length > 4096)
                throw new InvalidOperationException("The System Proxy bypass list is too long.");
            return normalized;
        }

        private static void NotifySettingsChanged()
        {
            InternetSetOptionNotify(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
            InternetSetOptionNotify(IntPtr.Zero, InternetOptionProxySettingsChanged, IntPtr.Zero, 0);
            InternetSetOptionNotify(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
        }
    }
}
