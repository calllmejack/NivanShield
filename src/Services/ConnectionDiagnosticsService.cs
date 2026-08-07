using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class DiagnosticStepResult
    {
        public string Name { get; set; }
        public bool Success { get; set; }
        public bool Warning { get; set; }
        public string Detail { get; set; }
    }

    public sealed class ConnectionDiagnosticResult
    {
        public ConnectionDiagnosticResult() { Steps = new List<DiagnosticStepResult>(); }
        public IList<DiagnosticStepResult> Steps { get; private set; }
        public bool Success { get { return Steps.All(delegate(DiagnosticStepResult step) { return step.Success || step.Warning; }); } }
        public string Summary { get; set; }
    }

    public sealed class ConnectionDiagnosticsService
    {
        private readonly SystemProxyService _systemProxy;

        public ConnectionDiagnosticsService(SystemProxyService systemProxy)
        {
            _systemProxy = systemProxy;
        }

        public async Task<ConnectionDiagnosticResult> RunAsync(
            ConnectionProfile profile,
            ConnectionState state,
            string routingMode,
            string selectedProcesses,
            int integratedProxyPort,
            CancellationToken cancellationToken)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            ConnectionDiagnosticResult result = new ConnectionDiagnosticResult();
            bool connected = state == ConnectionState.Connected;
            result.Steps.Add(new DiagnosticStepResult
            {
                Name = "Connection provider",
                Success = connected,
                Detail = connected ? profile.ProtocolLabel + " provider reports connected" : "Provider state is " + state
            });

            cancellationToken.ThrowIfCancellationRequested();
            if (profile.IsPsiphon)
            {
                result.Steps.Add(new DiagnosticStepResult
                {
                    Name = "Remote endpoint",
                    Success = connected,
                    Warning = !connected,
                    Detail = "Psiphon selects and changes remote servers automatically"
                });
            }
            else
            {
                ProbeResult remote = await NetworkProbe.TestTcpAsync(profile.ServerHost, profile.ServerPort, 3500).ConfigureAwait(false);
                result.Steps.Add(new DiagnosticStepResult
                {
                    Name = "Server reachability",
                    Success = remote.Success,
                    Detail = remote.Success ? "TCP reachable in " + remote.Milliseconds + " ms" : remote.Error
                });
            }

            cancellationToken.ThrowIfCancellationRequested();
            ProbeResult local = await NetworkProbe.TestTcpAsync("127.0.0.1", profile.LocalSocksPort, 1200).ConfigureAwait(false);
            result.Steps.Add(new DiagnosticStepResult
            {
                Name = "Local proxy",
                Success = local.Success,
                Detail = local.Success ? "Listening on 127.0.0.1:" + profile.LocalSocksPort : "Local proxy port is closed"
            });

            if (local.Success)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProxyConnectivityResult internet = await ProxyConnectivityProbe.TestSocks5Async(
                    profile.LocalSocksPort,
                    12000,
                    cancellationToken
                ).ConfigureAwait(false);
                result.Steps.Add(new DiagnosticStepResult
                {
                    Name = "Internet through tunnel",
                    Success = internet.Success,
                    Detail = internet.Success
                        ? "SOCKS5 reached an internet TLS endpoint in " + internet.Milliseconds + " ms"
                        : internet.Error
                });
            }
            else
            {
                result.Steps.Add(new DiagnosticStepResult
                {
                    Name = "Internet through tunnel",
                    Success = false,
                    Detail = "Skipped because the local proxy is not listening"
                });
            }

            bool tunExpected = String.Equals(routingMode, RoutingModes.SelectedApps, StringComparison.OrdinalIgnoreCase)
                || String.Equals(routingMode, RoutingModes.WholeDevice, StringComparison.OrdinalIgnoreCase);
            if (tunExpected)
            {
                bool tunFound = NetworkInterface.GetAllNetworkInterfaces().Any(delegate(NetworkInterface adapter)
                {
                    return adapter.Name.IndexOf("NivanShield-TUN", StringComparison.OrdinalIgnoreCase) >= 0
                        && adapter.OperationalStatus == OperationalStatus.Up;
                });
                bool appListMissing = String.Equals(routingMode, RoutingModes.SelectedApps, StringComparison.OrdinalIgnoreCase)
                    && String.IsNullOrWhiteSpace(selectedProcesses);
                result.Steps.Add(new DiagnosticStepResult
                {
                    Name = "Routing mode",
                    Success = tunFound && !appListMissing,
                    Detail = appListMissing
                        ? "No application has been selected for Selected Apps mode"
                        : tunFound ? "NivanShield-TUN is active" : "NivanShield-TUN adapter is not active"
                });
            }
            else
            {
                bool systemProxyMode = String.Equals(routingMode, RoutingModes.SystemProxy, StringComparison.OrdinalIgnoreCase);
                string actualProxy = String.Empty;
                int expectedPort = profile.IsSsh || profile.IsPsiphon ? integratedProxyPort : profile.LocalSocksPort;
                bool proxyReady = !systemProxyMode || _systemProxy.IsLanProxyEnabled("127.0.0.1:" + expectedPort, out actualProxy);
                result.Steps.Add(new DiagnosticStepResult
                {
                    Name = "Routing mode",
                    Success = proxyReady,
                    Detail = !systemProxyMode
                        ? "Only the protected browser uses the local proxy"
                        : proxyReady
                            ? "Windows System Proxy points to 127.0.0.1:" + expectedPort
                            : "Windows System Proxy is not set to the expected local endpoint"
                                + (String.IsNullOrWhiteSpace(actualProxy) ? String.Empty : " (current: " + actualProxy + ")")
                });
            }

            DiagnosticStepResult failure = result.Steps.FirstOrDefault(delegate(DiagnosticStepResult step) { return !step.Success && !step.Warning; });
            result.Summary = failure == null
                ? "Connection path is working correctly."
                : "First problem: " + failure.Name + " — " + failure.Detail;
            return result;
        }
    }
}
