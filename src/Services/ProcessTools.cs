using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Nivan.Shield.Services
{
    public sealed class ProcessResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
    }

    public static class ProcessTools
    {
        public static async Task<ProcessResult> RunHiddenAsync(string fileName, string arguments)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = fileName;
            info.Arguments = arguments;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;

            using (Process process = new Process())
            {
                process.StartInfo = info;
                process.Start();
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                await Task.Run(delegate { process.WaitForExit(); }).ConfigureAwait(false);
                return new ProcessResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = await stdout.ConfigureAwait(false),
                    StandardError = await stderr.ConfigureAwait(false)
                };
            }
        }

        public static void KillProcessTree(Process process)
        {
            if (process == null) return;
            try
            {
                if (process.HasExited) return;
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "taskkill.exe";
                info.Arguments = "/PID " + process.Id + " /T /F";
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                using (Process killer = Process.Start(info))
                {
                    if (killer != null) killer.WaitForExit(2500);
                }
            }
            catch
            {
                try { process.Kill(); } catch { }
            }
        }

        public static string Quote(string value)
        {
            if (String.IsNullOrEmpty(value)) return "\"\"";
            if (value.IndexOfAny(new char[] { ' ', '\t', '"' }) < 0) return value;

            StringBuilder builder = new StringBuilder();
            builder.Append('"');
            int slashCount = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    slashCount++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', slashCount * 2 + 1);
                    builder.Append('"');
                    slashCount = 0;
                    continue;
                }

                builder.Append('\\', slashCount);
                slashCount = 0;
                builder.Append(character);
            }
            builder.Append('\\', slashCount * 2);
            builder.Append('"');
            return builder.ToString();
        }
    }
}
