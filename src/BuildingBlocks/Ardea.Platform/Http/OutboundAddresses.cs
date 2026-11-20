using System.Net;
using System.Net.Sockets;

namespace Ardea.Platform.Http;

public static class OutboundAddresses
{
    public static bool IsForbidden(IPAddress address)
    {
        address = Normalize(address);

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.Broadcast) || address.IsIPv6LinkLocal || address.IsIPv6Multicast)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork
            && (bytes[0] == 0 || (bytes[0] == 169 && bytes[1] == 254) || bytes[0] >= 224);
    }

    public static bool IsInternal(IPAddress address)
    {
        address = Normalize(address);

        var b = address.GetAddressBytes();
        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168)
                || (b[0] == 100 && b[1] is >= 64 and <= 127),
            AddressFamily.InterNetworkV6 => (b[0] & 0xFE) == 0xFC || address.IsIPv6SiteLocal,
            _ => true,
        };
    }

    public static IPAddress Normalize(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }

        var b = address.GetAddressBytes();
        if (b.AsSpan(0, 12).IndexOfAnyExcept((byte)0) < 0
            || (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0))
        {
            return new IPAddress(b.AsSpan(12, 4));
        }

        return b[0] == 0x20 && b[1] == 0x02 ? new IPAddress(b.AsSpan(2, 4)) : address;
    }

    public static readonly HttpRequestOptionsKey<bool> AllowInternal = new("ardea.allow-internal");

    public static readonly HttpRequestOptionsKey<bool> Untraced = new("ardea.untraced");

    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var host = context.DnsEndPoint.Host;
            var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, cancellationToken);
            var internalAllowed = context.InitialRequestMessage.Options.TryGetValue(AllowInternal, out var allow) && allow;
            var allowed = addresses.Where(a => !IsForbidden(a) && (!IsInternal(a) || internalAllowed)).ToList();
            if (allowed.Count == 0)
            {
                throw new HttpRequestException($"{host} resolves to no address Ardea may connect to.");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync([.. allowed], context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };
}
