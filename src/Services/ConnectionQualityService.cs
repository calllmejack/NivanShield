using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public enum ConnectionQualityTestKind
    {
        Health,
        Quick,
        Full
    }

    public sealed class ConnectionQualityProgress
    {
        public int Percent { get; set; }
        public string Stage { get; set; }
        public string Detail { get; set; }
    }

    public sealed class ConnectionQualityResult
    {
        public ConnectionQualityTestKind TestKind { get; set; }
        public long ServerLatencyMilliseconds { get; set; }
        public double TunnelLatencyMilliseconds { get; set; }
        public double JitterMilliseconds { get; set; }
        public double FailureRatePercent { get; set; }
        public double DownloadMegabitsPerSecond { get; set; }
        public double UploadMegabitsPerSecond { get; set; }
        public int QualityScore { get; set; }
        public string QualityLabel { get; set; }
        public int SuccessfulSamples { get; set; }
        public int TotalSamples { get; set; }
    }

    public sealed class ConnectionQualityService
    {
        private const string DownloadEndpoint = "https://speed.cloudflare.com/__down";
        private const string UploadEndpoint = "https://speed.cloudflare.com/__up";
        private readonly AppLogger _logger;

        public ConnectionQualityService(AppLogger logger)
        {
            _logger = logger;
        }

        public async Task<ConnectionQualityResult> RunAsync(
            ConnectionProfile profile,
            int localHttpProxyPort,
            HealthSettings settings,
            ConnectionQualityTestKind kind,
            IProgress<ConnectionQualityProgress> progress,
            CancellationToken cancellationToken)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if (settings == null) throw new ArgumentNullException("settings");
            if (localHttpProxyPort < 1 || localHttpProxyPort > 65535)
                throw new InvalidOperationException("The local HTTP proxy port is invalid.");

            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            Report(progress, 2, "Server latency", "Testing " + profile.ServerHost + ":" + profile.ServerPort + "...");
            ProbeResult server = await NetworkProbe.TestTcpAsync(
                profile.ServerHost,
                profile.ServerPort,
                4000
            ).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            int sampleCount = Math.Max(3, Math.Min(10, settings.LatencySamples));
            List<double> samples = new List<double>();
            int failures = 0;
            for (int index = 0; index < sampleCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(
                    progress,
                    8 + (int)Math.Round((index / (double)sampleCount) * 24.0),
                    "VPN latency",
                    "Tunnel sample " + (index + 1) + " of " + sampleCount
                );
                try
                {
                    double latency = await MeasureTunnelLatencyAsync(
                        localHttpProxyPort,
                        cancellationToken
                    ).ConfigureAwait(false);
                    samples.Add(latency);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    failures++;
                    _logger.Warning("Tunnel latency sample failed: " + exception.Message);
                }
            }

            if (samples.Count == 0)
                throw new InvalidOperationException(
                    "The local proxy is open, but no internet request could pass through the VPN."
                );

            ConnectionQualityResult result = new ConnectionQualityResult();
            result.TestKind = kind;
            result.ServerLatencyMilliseconds = server.Success ? server.Milliseconds : -1;
            result.TunnelLatencyMilliseconds = samples.Average();
            result.JitterMilliseconds = CalculateJitter(samples);
            result.TotalSamples = sampleCount;
            result.SuccessfulSamples = samples.Count;
            result.FailureRatePercent = failures * 100.0 / sampleCount;

            if (kind != ConnectionQualityTestKind.Health)
            {
                int downloadMegabytes = kind == ConnectionQualityTestKind.Full
                    ? settings.FullDownloadMegabytes
                    : settings.QuickDownloadMegabytes;
                long downloadBytes = (long)downloadMegabytes * 1024L * 1024L;
                Report(progress, 35, "Download", "Testing " + downloadMegabytes + " MB through the VPN...");
                result.DownloadMegabitsPerSecond = await MeasureDownloadAsync(
                    localHttpProxyPort,
                    downloadBytes,
                    progress,
                    35,
                    68,
                    cancellationToken
                ).ConfigureAwait(false);

                int uploadMegabytes = kind == ConnectionQualityTestKind.Full
                    ? settings.FullUploadMegabytes
                    : Math.Min(2, Math.Max(1, settings.FullUploadMegabytes));
                long uploadBytes = (long)uploadMegabytes * 1024L * 1024L;
                Report(progress, 70, "Upload", "Testing " + uploadMegabytes + " MB through the VPN...");
                result.UploadMegabitsPerSecond = await MeasureUploadAsync(
                    localHttpProxyPort,
                    uploadBytes,
                    progress,
                    70,
                    96,
                    cancellationToken
                ).ConfigureAwait(false);
            }

            result.QualityScore = CalculateScore(result);
            result.QualityLabel = QualityLabel(result.QualityScore);
            Report(progress, 100, "Complete", result.QualityLabel + " connection quality");
            _logger.Info(
                "Connection quality test completed: " + result.QualityLabel
                + " (" + result.QualityScore + "/100), VPN latency "
                + result.TunnelLatencyMilliseconds.ToString("0") + " ms."
            );
            return result;
        }

        private static async Task<double> MeasureTunnelLatencyAsync(
            int proxyPort,
            CancellationToken cancellationToken)
        {
            string address = DownloadEndpoint + "?bytes=64&nivan=" + Guid.NewGuid().ToString("N");
            HttpWebRequest request = CreateRequest(address, proxyPort, "GET", 8000);
            request.KeepAlive = false;
            Stopwatch stopwatch = Stopwatch.StartNew();
            using (HttpWebResponse response = await GetResponseAsync(request, 8000, cancellationToken).ConfigureAwait(false))
            using (Stream stream = response.GetResponseStream())
            {
                byte[] buffer = new byte[64];
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                Array.Clear(buffer, 0, buffer.Length);
                if (read <= 0) throw new InvalidOperationException("The speed-test endpoint returned no data.");
            }
            stopwatch.Stop();
            return Math.Max(0.1, stopwatch.Elapsed.TotalMilliseconds);
        }

        private static async Task<double> MeasureDownloadAsync(
            int proxyPort,
            long expectedBytes,
            IProgress<ConnectionQualityProgress> progress,
            int startPercent,
            int endPercent,
            CancellationToken cancellationToken)
        {
            string address = DownloadEndpoint + "?bytes=" + expectedBytes + "&nivan=" + Guid.NewGuid().ToString("N");
            HttpWebRequest request = CreateRequest(address, proxyPort, "GET", 90000);
            long total = 0;
            Stopwatch stopwatch = new Stopwatch();
            using (HttpWebResponse response = await GetResponseAsync(request, 20000, cancellationToken).ConfigureAwait(false))
            using (Stream stream = response.GetResponseStream())
            {
                byte[] buffer = new byte[64 * 1024];
                stopwatch.Start();
                try
                {
                    while (true)
                    {
                        int read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                        if (read <= 0) break;
                        total += read;
                        int percent = startPercent + (int)Math.Min(
                            endPercent - startPercent,
                            (total * (endPercent - startPercent)) / Math.Max(1L, expectedBytes)
                        );
                        Report(progress, percent, "Download", FormatTransferred(total, expectedBytes));
                    }
                }
                finally
                {
                    stopwatch.Stop();
                    Array.Clear(buffer, 0, buffer.Length);
                }
            }
            if (total <= 0) throw new InvalidOperationException("The download test returned no data.");
            return ToMegabitsPerSecond(total, stopwatch.Elapsed);
        }

        private static async Task<double> MeasureUploadAsync(
            int proxyPort,
            long totalBytes,
            IProgress<ConnectionQualityProgress> progress,
            int startPercent,
            int endPercent,
            CancellationToken cancellationToken)
        {
            HttpWebRequest request = CreateRequest(UploadEndpoint, proxyPort, "POST", 90000);
            request.ContentType = "application/octet-stream";
            request.ContentLength = totalBytes;
            byte[] buffer = new byte[64 * 1024];
            new Random(7319).NextBytes(buffer);
            long sent = 0;
            Stopwatch stopwatch = new Stopwatch();
            try
            {
                using (CancellationTokenRegistration registration = cancellationToken.Register(delegate
                {
                    try { request.Abort(); } catch { }
                }))
                using (Stream stream = await request.GetRequestStreamAsync().ConfigureAwait(false))
                {
                    stopwatch.Start();
                    while (sent < totalBytes)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int count = (int)Math.Min(buffer.Length, totalBytes - sent);
                        await stream.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
                        sent += count;
                        int percent = startPercent + (int)Math.Min(
                            endPercent - startPercent,
                            (sent * (endPercent - startPercent)) / Math.Max(1L, totalBytes)
                        );
                        Report(progress, percent, "Upload", FormatTransferred(sent, totalBytes));
                    }
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                using (HttpWebResponse response = await GetResponseAsync(request, 30000, cancellationToken).ConfigureAwait(false))
                using (Stream responseStream = response.GetResponseStream())
                {
                    if (responseStream != null)
                    {
                        byte[] responseBuffer = new byte[256];
                        await responseStream.ReadAsync(responseBuffer, 0, responseBuffer.Length, cancellationToken).ConfigureAwait(false);
                        Array.Clear(responseBuffer, 0, responseBuffer.Length);
                    }
                }
            }
            catch (WebException)
            {
                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
                throw;
            }
            finally
            {
                stopwatch.Stop();
                Array.Clear(buffer, 0, buffer.Length);
            }
            if (sent <= 0) throw new InvalidOperationException("The upload test sent no data.");
            return ToMegabitsPerSecond(sent, stopwatch.Elapsed);
        }

        private static HttpWebRequest CreateRequest(string address, int proxyPort, string method, int timeout)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(address);
            request.Method = method;
            request.Proxy = new WebProxy("http://127.0.0.1:" + proxyPort);
            request.UserAgent = "NivanShield/6.0.5";
            request.Timeout = timeout;
            request.ReadWriteTimeout = timeout;
            request.AllowAutoRedirect = false;
            request.AutomaticDecompression = DecompressionMethods.None;
            request.Headers[HttpRequestHeader.CacheControl] = "no-cache, no-store";
            request.Headers[HttpRequestHeader.Pragma] = "no-cache";
            return request;
        }

        private static async Task<HttpWebResponse> GetResponseAsync(
            HttpWebRequest request,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            using (CancellationTokenRegistration registration = cancellationToken.Register(delegate
            {
                try { request.Abort(); } catch { }
            }))
            {
                Task<WebResponse> responseTask = request.GetResponseAsync();
                Task completed = await Task.WhenAny(
                    responseTask,
                    Task.Delay(timeoutMilliseconds, cancellationToken)
                ).ConfigureAwait(false);
                if (completed != responseTask)
                {
                    try { request.Abort(); } catch { }
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException("The tunneled request timed out.");
                }
                try { return (HttpWebResponse)await responseTask.ConfigureAwait(false); }
                catch (WebException)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);
                    throw;
                }
            }
        }

        private static double CalculateJitter(IList<double> samples)
        {
            if (samples == null || samples.Count < 2) return 0;
            double total = 0;
            for (int index = 1; index < samples.Count; index++)
                total += Math.Abs(samples[index] - samples[index - 1]);
            return total / (samples.Count - 1);
        }

        private static int CalculateScore(ConnectionQualityResult result)
        {
            double score = 100;
            double latency = result.TunnelLatencyMilliseconds;
            if (latency > 400) score -= 45;
            else if (latency > 250) score -= 32;
            else if (latency > 150) score -= 20;
            else if (latency > 80) score -= 9;

            if (result.JitterMilliseconds > 80) score -= 25;
            else if (result.JitterMilliseconds > 40) score -= 16;
            else if (result.JitterMilliseconds > 20) score -= 8;
            else if (result.JitterMilliseconds > 10) score -= 3;

            score -= Math.Min(35, result.FailureRatePercent * 0.7);
            if (result.DownloadMegabitsPerSecond > 0 && result.DownloadMegabitsPerSecond < 2) score -= 15;
            else if (result.DownloadMegabitsPerSecond >= 2 && result.DownloadMegabitsPerSecond < 8) score -= 7;
            if (result.UploadMegabitsPerSecond > 0 && result.UploadMegabitsPerSecond < 1) score -= 8;
            return Math.Max(0, Math.Min(100, (int)Math.Round(score)));
        }

        private static string QualityLabel(int score)
        {
            if (score >= 85) return "Excellent";
            if (score >= 70) return "Good";
            if (score >= 50) return "Fair";
            return "Poor";
        }

        private static double ToMegabitsPerSecond(long bytes, TimeSpan elapsed)
        {
            double seconds = Math.Max(0.001, elapsed.TotalSeconds);
            return (bytes * 8.0) / seconds / 1000000.0;
        }

        private static string FormatTransferred(long current, long total)
        {
            return (current / 1048576.0).ToString("0.0") + " / "
                + (total / 1048576.0).ToString("0.0") + " MB";
        }

        private static void Report(
            IProgress<ConnectionQualityProgress> progress,
            int percent,
            string stage,
            string detail)
        {
            if (progress == null) return;
            progress.Report(new ConnectionQualityProgress
            {
                Percent = Math.Max(0, Math.Min(100, percent)),
                Stage = stage ?? String.Empty,
                Detail = detail ?? String.Empty
            });
        }
    }
}
