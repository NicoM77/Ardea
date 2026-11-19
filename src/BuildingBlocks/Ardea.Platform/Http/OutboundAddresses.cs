using System.Net;
using System.Net.Sockets;

namespace Ardea.Platform.Http;

/// <summary>
/// Outbound connections to addresses an organisation or administrator chose (master plan 9.5, 16.9):
/// the host is resolved once, every address is checked, and the connection goes to the checked
/// address, so DNS cannot point it elsewhere afterwards. Loopback, link-local (cloud metadata), and
/// unspecified addresses are never allowed; private ranges only for hosts the instance administrator
/// named explicitly. IPv6 forms that embed an IPv4 address are checked as that address; no proxy is used.
/// </summary>
public static class OutboundAddresses
{
    /// <summary>Addresses no outbound call may reach, whoever configured them.</summary>
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

    /// <summary>Private and shared ranges: allowed only for hosts on an administrator's list.</summary>
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

    /// <summary>
    /// The IPv4 address an IPv6 address stands for, where it embeds one: IPv4-mapped (<c>::ffff:a.b.c.d</c>),
    /// IPv4-compatible (<c>::a.b.c.d</c>), NAT64 (<c>64:ff9b::/96</c>, which a NAT64 gateway forwards to the IPv4
    /// address) and 6to4 (<c>2002::/16</c>). Checked as that IPv4 address, none of them leads past the rules.
    /// </summary>
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

    /// <summary>Set on a request to a host the instance administrator registered: it may resolve to a private address.</summary>
    public static readonly HttpRequestOptionsKey<bool> AllowInternal = new("ardea.allow-internal");

    /// <summary>
    /// Set on a request whose address is a secret (a push endpoint, a chat webhook with its token in the path): the
    /// HTTP client instrumentation records no span for it, so its URL never reaches a trace exporter.
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> Untraced = new("ardea.untraced");

    /// <summary>A handler that connects only to checked addresses and follows no redirect.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        // Through a proxy the check would see the proxy's address, not the target's; outbound calls go direct.
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
