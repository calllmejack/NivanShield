using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class ProfileHealthResult
    {
        public ConnectionProfile Profile { get; set; }
        public bool Success { get; set; }
        public long LatencyMilliseconds { get; set; }
        public string Status { get; set; }
        public string Error { get; set; }
    }

    public sealed class ProfileHealthService
    {
        private readonly AppLogger _logger;

        public ProfileHealthService(AppLogger logger)
        {
            _logger = logger;
        }

        public async Task<ProfileHealthResult> TestAsync(ConnectionProfile profile, int timeoutMilliseconds)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            ProbeResult probe = await NetworkProbe.TestTcpAsync(
                profile.ServerHost,
                profile.ServerPort,
                timeoutMilliseconds
            ).ConfigureAwait(false);

            string status;
            if (probe.Success) status = "Online";
            else if (!String.IsNullOrWhiteSpace(probe.Error) && probe.Error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0) status = "Timeout";
            else status = "Offline";

            profile.LastTestedUtc = DateTime.UtcNow;
            profile.LastLatencyMilliseconds = probe.Success ? probe.Milliseconds : -1;
            profile.LastTestStatus = status;
            _logger.Info(
                "Profile health test: " + profile.Name + " - " + status +
                (probe.Success ? " in " + probe.Milliseconds + " ms." : ".")
            );

            return new ProfileHealthResult
            {
                Profile = profile,
                Success = probe.Success,
                LatencyMilliseconds = probe.Success ? probe.Milliseconds : -1,
                Status = status,
                Error = probe.Error ?? String.Empty
            };
        }

        public async Task<IList<ProfileHealthResult>> TestAllAsync(
            IEnumerable<ConnectionProfile> profiles,
            int timeoutMilliseconds,
            int maximumConcurrency)
        {
            SemaphoreSlim gate = new SemaphoreSlim(Math.Max(1, maximumConcurrency));
            List<Task<ProfileHealthResult>> tasks = new List<Task<ProfileHealthResult>>();
            foreach (ConnectionProfile profile in profiles)
            {
                ConnectionProfile captured = profile;
                tasks.Add(Task.Run(async delegate
                {
                    await gate.WaitAsync().ConfigureAwait(false);
                    try { return await TestAsync(captured, timeoutMilliseconds).ConfigureAwait(false); }
                    finally { gate.Release(); }
                }));
            }

            ProfileHealthResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
            gate.Dispose();
            return results;
        }
    }
}
