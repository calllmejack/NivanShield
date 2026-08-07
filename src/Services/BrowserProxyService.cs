using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class BrowserChoice
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ExecutablePath { get; set; }
        public override string ToString() { return Name; }
    }

    public sealed class BrowserProxyService
    {
        private readonly AppPaths _paths;
        private readonly BinaryIntegrityService _integrity;

        public BrowserProxyService(AppPaths paths, BinaryIntegrityService integrity)
        {
            _paths = paths;
            _integrity = integrity;
        }

        public IList<BrowserChoice> Discover()
        {
            List<BrowserChoice> choices = new List<BrowserChoice>();
            AddIfTrusted(choices, "edge", "Microsoft Edge", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft", "Edge", "Application", "msedge.exe"
            ), "Microsoft");
            AddIfTrusted(choices, "edge", "Microsoft Edge", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft", "Edge", "Application", "msedge.exe"
            ), "Microsoft");
            AddIfTrusted(choices, "chrome", "Google Chrome", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Google", "Chrome", "Application", "chrome.exe"
            ), "Google");
            AddIfTrusted(choices, "chrome", "Google Chrome", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Google", "Chrome", "Application", "chrome.exe"
            ), "Google");
            return choices;
        }

        public void Launch(BrowserChoice browser, int proxyPort)
        {
            if (browser == null || String.IsNullOrWhiteSpace(browser.ExecutablePath))
                throw new InvalidOperationException("No supported browser was found.");
            if (proxyPort < 1 || proxyPort > 65535)
                throw new InvalidOperationException("The local proxy port is invalid.");
            VerifyPublisher(browser.ExecutablePath, browser.Id == "edge" ? "Microsoft" : "Google");

            string profileRoot = Path.Combine(_paths.DataRoot, "browser", browser.Id);
            Directory.CreateDirectory(profileRoot);
            string arguments = ProcessTools.Quote(browser.ExecutablePath)
                + " --proxy-server=" + ProcessTools.Quote("socks5://127.0.0.1:" + proxyPort)
                + " --host-resolver-rules=" + ProcessTools.Quote("MAP * ~NOTFOUND , EXCLUDE localhost")
                + " --user-data-dir=" + ProcessTools.Quote(profileRoot)
                + " --no-first-run --no-default-browser-check --new-window "
                + ProcessTools.Quote("https://cp.cloudflare.com/");

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = "explorer.exe";
            info.Arguments = arguments;
            info.UseShellExecute = true;
            Process.Start(info);
        }

        private void AddIfTrusted(
            IList<BrowserChoice> choices,
            string id,
            string name,
            string path,
            string company)
        {
            if (!File.Exists(path)) return;
            foreach (BrowserChoice existing in choices)
                if (String.Equals(existing.ExecutablePath, path, StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                VerifyPublisher(path, company);
                choices.Add(new BrowserChoice { Id = id, Name = name, ExecutablePath = path });
            }
            catch { }
        }

        private void VerifyPublisher(string path, string expected)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("The selected browser no longer exists.", path);
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
            string company = version.CompanyName ?? String.Empty;
            if (company.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("The browser publisher could not be verified.");
            _integrity.VerifyPublisherAsync(path, expected).GetAwaiter().GetResult();
        }
    }
}
