using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nivan.Shield.Services
{
    public sealed class ProxyConnectivityResult
    {
        public bool Success { get; set; }
        public long Milliseconds { get; set; }
        public string Endpoint { get; set; }
        public string Error { get; set; }
    }

    public static class ProxyConnectivityProbe
    {
        private static readonly string[] ProbeEndpoints = new string[]
        {
            "https://cp.cloudflare.com/generate_204",
            "https://www.gstatic.com/generate_204",
            "http://www.msftconnecttest.com/connecttest.txt"
        };

        public static async Task<ProxyConnectivityResult> TestAsync(
            int localProxyPort,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            if (localProxyPort < 1 || localProxyPort > 65535)
                throw new ArgumentOutOfRangeException("localProxyPort");

            List<string> errors = new List<string>();
            Stopwatch total = Stopwatch.StartNew();
            int perEndpointTimeout = Math.Max(2500, timeoutMilliseconds / ProbeEndpoints.Length);
            foreach (string endpoint in ProbeEndpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProxyConnectivityResult result = await TestEndpointAsync(
                    endpoint,
                    localProxyPort,
                    perEndpointTimeout,
                    cancellationToken
                ).ConfigureAwait(false);
                if (result.Success)
                {
                    total.Stop();
                    result.Milliseconds = total.ElapsedMilliseconds;
                    return result;
                }
                if (!String.IsNullOrWhiteSpace(result.Error)) errors.Add(result.Error);
            }
            total.Stop();
            return new ProxyConnectivityResult
            {
                Success = false,
                Milliseconds = total.ElapsedMilliseconds,
                Endpoint = String.Empty,
                Error = errors.Count == 0
                    ? "No internet response was received through the local proxy."
                    : String.Join(" | ", errors.ToArray())
            };
        }

        public static async Task<ProxyConnectivityResult> TestSocks5Async(
            int localProxyPort,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            TcpClient client = new TcpClient();
            try
            {
                Task connect = client.ConnectAsync("127.0.0.1", localProxyPort);
                if (await Task.WhenAny(connect, Task.Delay(timeoutMilliseconds, cancellationToken)).ConfigureAwait(false) != connect)
                    throw new TimeoutException("Local SOCKS5 connection timed out.");
                await connect.ConfigureAwait(false);
                NetworkStream stream = client.GetStream();
                byte[] greeting = new byte[] { 5, 1, 0 };
                await stream.WriteAsync(greeting, 0, greeting.Length, cancellationToken).ConfigureAwait(false);
                byte[] response = await ReadExactAsync(stream, 2, timeoutMilliseconds, cancellationToken).ConfigureAwait(false);
                if (response[0] != 5 || response[1] != 0)
                    throw new InvalidOperationException("The local endpoint did not accept SOCKS5 authentication.");

                byte[] host = Encoding.ASCII.GetBytes("cp.cloudflare.com");
                byte[] request = new byte[7 + host.Length];
                request[0] = 5; request[1] = 1; request[2] = 0; request[3] = 3;
                request[4] = (byte)host.Length;
                Buffer.BlockCopy(host, 0, request, 5, host.Length);
                request[request.Length - 2] = 1;
                request[request.Length - 1] = 187;
                await stream.WriteAsync(request, 0, request.Length, cancellationToken).ConfigureAwait(false);
                byte[] header = await ReadExactAsync(stream, 4, timeoutMilliseconds, cancellationToken).ConfigureAwait(false);
                if (header[0] != 5 || header[1] != 0)
                    throw new InvalidOperationException("SOCKS5 could not reach the internet target (code " + header[1] + ").");
                stopwatch.Stop();
                return new ProxyConnectivityResult
                {
                    Success = true,
                    Milliseconds = stopwatch.ElapsedMilliseconds,
                    Endpoint = "cp.cloudflare.com:443",
                    Error = String.Empty
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                stopwatch.Stop();
                return new ProxyConnectivityResult
                {
                    Success = false,
                    Milliseconds = stopwatch.ElapsedMilliseconds,
                    Endpoint = "cp.cloudflare.com:443",
                    Error = ShortError(exception)
                };
            }
            finally { client.Close(); }
        }

        private static async Task<byte[]> ReadExactAsync(
            NetworkStream stream,
            int count,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                Task<int> read = stream.ReadAsync(buffer, offset, count - offset, cancellationToken);
                if (await Task.WhenAny(read, Task.Delay(timeoutMilliseconds, cancellationToken)).ConfigureAwait(false) != read)
                    throw new TimeoutException("SOCKS5 response timed out.");
                int received = await read.ConfigureAwait(false);
                if (received <= 0) throw new InvalidOperationException("SOCKS5 closed the connection unexpectedly.");
                offset += received;
            }
            return buffer;
        }

        private static async Task<ProxyConnectivityResult> TestEndpointAsync(
            string endpoint,
            int localProxyPort,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            HttpWebRequest request = null;
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                request = (HttpWebRequest)WebRequest.Create(endpoint);
                request.Method = "GET";
                request.UserAgent = "NivanShield/6.0.5";
                request.Proxy = new WebProxy("http://127.0.0.1:" + localProxyPort);
                request.AllowAutoRedirect = false;
                request.KeepAlive = false;
                request.Timeout = timeoutMilliseconds;
                request.ReadWriteTimeout = timeoutMilliseconds;

                using (cancellationToken.Register(delegate { try { request.Abort(); } catch { } }))
                {
                    Task<WebResponse> responseTask = request.GetResponseAsync();
                    Task completed = await Task.WhenAny(
                        responseTask,
                        Task.Delay(timeoutMilliseconds, cancellationToken)
                    ).ConfigureAwait(false);
                    if (completed != responseTask)
                    {
                        request.Abort();
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new TimeoutException("Timed out");
                    }

                    using (HttpWebResponse response = (HttpWebResponse)await responseTask.ConfigureAwait(false))
                    {
                        int status = (int)response.StatusCode;
                        if (status >= 200 && status < 400)
                        {
                            stopwatch.Stop();
                            return new ProxyConnectivityResult
                            {
                                Success = true,
                                Milliseconds = stopwatch.ElapsedMilliseconds,
                                Endpoint = endpoint,
                                Error = String.Empty
                            };
                        }
                        throw new InvalidOperationException("HTTP " + status);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                stopwatch.Stop();
                return new ProxyConnectivityResult
                {
                    Success = false,
                    Milliseconds = stopwatch.ElapsedMilliseconds,
                    Endpoint = endpoint,
                    Error = ShortHost(endpoint) + ": " + ShortError(exception)
                };
            }
        }

        private static string ShortHost(string endpoint)
        {
            Uri uri;
            return Uri.TryCreate(endpoint, UriKind.Absolute, out uri) ? uri.Host : endpoint;
        }

        private static string ShortError(Exception exception)
        {
            WebException web = exception as WebException;
            if (web != null && web.Response != null)
            {
                using (WebResponse response = web.Response)
                {
                    HttpWebResponse http = response as HttpWebResponse;
                    if (http != null) return "HTTP " + (int)http.StatusCode;
                }
            }
            string text = (exception.Message ?? exception.GetType().Name)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
            return text.Length <= 140 ? text : text.Substring(0, 137) + "...";
        }
    }
}
