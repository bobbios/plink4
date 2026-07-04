using System;
using System.Net.Sockets;

namespace plink4
{
    internal static class TerminalConnectivity
    {
        public static bool IsReachable(string ip, int port, int timeoutMs)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;

            try
            {
                using (var client = new TcpClient())
                {
                    IAsyncResult result = client.BeginConnect(ip, port, null, null);
                    bool signaled = result.AsyncWaitHandle.WaitOne(timeoutMs);

                    if (!signaled || !client.Connected)
                        return false;

                    client.EndConnect(result);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
