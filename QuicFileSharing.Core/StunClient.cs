using System.Net;
using System.Net.Sockets;
using STUN.Client;
 
namespace QuicFileSharing.Core;
 
class StunClient(string stunServerAddress, int stunServerPort)
{
   
    public async Task<(IPEndPoint?, IPEndPoint?)> RunAsync()
    {
        var stunServerEndpoints = await GetStunServerEndpoints();

        var ipv6Task = GetPublicEndpoint(stunServerEndpoints, AddressFamily.InterNetworkV6);
        var ipv4Task = GetPublicEndpoint(stunServerEndpoints, AddressFamily.InterNetwork);

        await Task.WhenAll(ipv6Task, ipv4Task);
        return (ipv4Task.Result, ipv6Task.Result);
    }

    private async Task<List<IPEndPoint>> GetStunServerEndpoints()
    {
        IPAddress[] addresses;
        try
        {
            var token = new CancellationTokenSource();
            token.CancelAfter(TimeSpan.FromSeconds(5));
            addresses = await Dns.GetHostAddressesAsync(stunServerAddress, token.Token);
        }
        catch
        {
            return [];
        }

        var results = new List<IPEndPoint>();
        foreach (var address in addresses)
        {
            results.Add(new IPEndPoint(address, stunServerPort));
        }
        return results;
    }

    private static async Task<IPEndPoint?> GetPublicEndpoint(List<IPEndPoint> stunServerEndpoints, AddressFamily family)
    {
        foreach (var ep in stunServerEndpoints)
        {
            if (ep.AddressFamily != family)
                continue;

            var localEndpoint = family == AddressFamily.InterNetworkV6
                ? new IPEndPoint(IPAddress.IPv6Any, 0)
                : new IPEndPoint(IPAddress.Any, 0);

            var client = new StunClient5389UDP(ep, localEndpoint);
            client.ReceiveTimeout = TimeSpan.FromSeconds(5);
            try
            {
                await client.QueryAsync();
                client.Dispose();
                if (client.State.PublicEndPoint != null)
                    return client.State.PublicEndPoint;
            }
            catch
            {
                // ignore
            }
        }

        return null; 
    }
}