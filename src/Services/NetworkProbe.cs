using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Nivan.Shield.Services
{
    public sealed class ProbeResult
    {
        public bool Success { get; set; }
        public long Milliseconds { get; set; }
        public string Error { get; set; }
    }

    public static class NetworkProbe
    {
        public static async Task<ProbeResult> TestTcpAsync(string host, int port, int timeoutMilliseconds)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            TcpClient client = new TcpClient();
            try
            {
                Task connectTask = client.ConnectAsync(host, port);
                Task completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMilliseconds)).ConfigureAwait(false);
                if (completed != connectTask)
                    return new ProbeResult { Success = false, Milliseconds = stopwatch.ElapsedMilliseconds, Error = "Connection timed out." };

                await connectTask.ConfigureAwait(false);
                return new ProbeResult { Success = true, Milliseconds = stopwatch.ElapsedMilliseconds, Error = String.Empty };
            }
            catch (Exception exception)
            {
                return new ProbeResult { Success = false, Milliseconds = stopwatch.ElapsedMilliseconds, Error = exception.Message };
            }
            finally
            {
                client.Close();
                stopwatch.Stop();
            }
        }

        public static async Task<bool> IsLocalPortOpenAsync(int port)
        {
            ProbeResult result = await TestTcpAsync("127.0.0.1", port, 250).ConfigureAwait(false);
            return result.Success;
        }
    }
}
