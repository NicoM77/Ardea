using System.Net;
using System.Net.Sockets;

namespace Ardea.Platform.Http;

/// <summary>
/// Outbound connections to addresses an organisation or administrator chose (master plan 9.5, 16.9):
/// the host is resolved once, every address is checked, and the connection goes to the checked
/// address, so DNS cannot point it elsewhere afterwards. Loopback, link-local (cloud metadata), and
/// unspecified addresses are never allowed; private ranges only for hosts the instance administrator
/// named explicitly.
/// </summary>
public static class OutboundAddresses
{
    /// <summary>Addresses no outbound call may reach, whoever configured them.</summary>
    public static bool IsForbidden(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.Broadcast) || address.IsIPv6LinkLocal || address.IsIPv6Multicast)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork
            && (bytes[0] == 0 || (bytes[0] == 169 && bytes[1] == 254) || bytes[0] >= 224);
    }

    /// <summary>Private and shared ranges: allowed only for hosts on an administrator's list.</summary>
    public static bool IsInternal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var b = address.GetAddressBytes();
        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168)
                || (b[0] == 100 && b[1] is >= 64 and <= 127),
            AddressFamily.InterNetworkV6 => (b[0] & 0xFE) == 0xFC || address.IsIPv6SiteLocal,
            _ => true,
        };
    }

    /// <summary>Set on a request to a host the instance administrator registered: it may resolve to a private address.</summary>
    public static readonly HttpRequestOptionsKey<bool> AllowInternal = new("ardea.allow-internal");

    /// <summary>A handler that connects only to checked addresses and follows no redirect.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
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
