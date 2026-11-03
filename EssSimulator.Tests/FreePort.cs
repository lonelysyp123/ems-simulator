using System.Net;
using System.Net.Sockets;

namespace EssSimulator.Tests;

/// <summary>
/// 取一个当前空闲的本地端口。
/// 之前多个用例各自 <c>Random.Shared.Next(42000, 58000)</c>，并行跑时两个用例会抽到
/// 同一个端口，先绑上的那个让另一个 AttachDevice 失败，表现为随机失败的端口类用例。
/// 这里让操作系统分配，并在进程内去重。
/// </summary>
internal static class FreePort
{
    private static readonly HashSet<int> Issued = new();
    private static readonly object Gate = new();

    public static int Next()
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            lock (Gate)
            {
                if (Issued.Add(port))
                    return port;
            }
        }

        throw new InvalidOperationException("找不到可用的本地端口");
    }
}
