using System.Net;
using System.Net.Sockets;

namespace Controledu.Tests.Infrastructure;

internal static class TestPortAllocator
{
    public static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
