using System.Net;
using System.Net.Sockets;

namespace DynamicEntity.Infrastructure.Notifications;

/// <summary>
/// Rejects webhook destinations that resolve to addresses reachable only inside the deployment, which is the
/// primary defence against using tenant-configured webhooks to reach internal services (SSRF).
/// </summary>
public static class PublicNetworkGuard
{
    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var octets = address.GetAddressBytes();
            return octets[0] switch
            {
                0 or 10 or 127 => true,                                  // this network, private, loopback
                100 => octets[1] is >= 64 and <= 127,                    // carrier-grade NAT
                169 => octets[1] == 254,                                 // link local, includes cloud metadata
                172 => octets[1] is >= 16 and <= 31,                     // private
                192 => octets[1] == 168 || (octets[1] == 0 && octets[2] == 0), // private, IETF protocol assignments
                >= 224 => true,                                          // multicast and reserved
                _ => false
            };
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6) return true;
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6Teredo)
            return true;

        // fc00::/7 unique local addresses.
        return (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
    }

    public static void EnsureAllowed(IPAddress address, string host)
    {
        if (IsBlocked(address))
            throw new WebhookDestinationException($"Webhook host '{host}' resolves to a non-public address.");
    }
}

public sealed class WebhookDestinationException(string message) : Exception(message);
