using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace ContentPlatform.Ingestion.Infrastructure;

/// <summary>
/// SSRF sertleştirme (00 §20): dış URL çekerken hedef IP'yi doğrular.
/// Yerel/özel/link-local/metadata (169.254.169.254) IP'lere bağlanmayı engeller;
/// DNS-rebinding'e karşı bağlantı ANINDA çözümlenen IP kontrol edilir ve o IP'ye bağlanılır.
/// SocketsHttpHandler.ConnectCallback olarak kullanılır.
/// </summary>
internal static class SsrfGuard
{
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var endpoint = context.DnsEndPoint;

        IPAddress[] addresses = IPAddress.TryParse(endpoint.Host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(endpoint.Host, ct);

        var target = addresses.FirstOrDefault(a => !IsBlocked(a));
        if (target is null)
            throw new IOException($"SSRF koruması: '{endpoint.Host}' engellenen bir IP'ye çözümlendi.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(target, endpoint.Port), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Bağlanılmaması gereken (dahili/özel/ayrılmış) adresler.</summary>
    public static bool IsBlocked(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip)) return true;                       // 127/8, ::1
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;

        var b = ip.GetAddressBytes();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] switch
            {
                0 or 10 or 127 => true,                                  // 0/8, 10/8, 127/8
                100 => b[1] is >= 64 and <= 127,                         // 100.64/10 (CGNAT)
                169 => b[1] == 254,                                      // 169.254/16 (link-local + metadata)
                172 => b[1] is >= 16 and <= 31,                          // 172.16/12
                192 => b[1] == 168,                                      // 192.168/16
                _ => false
            };
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
            if ((b[0] & 0xFE) == 0xFC) return true;                      // fc00::/7 unique-local
            return false;
        }

        return true; // bilinmeyen adres ailesi → engelle
    }
}
