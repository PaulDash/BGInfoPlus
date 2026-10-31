using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using BGInfoPlus.Core;

namespace BGInfoPlus;

internal static class SystemInfo
{
    public static List<InfoField> ReadLocal()
    {
        var fields = new List<InfoField> { new("host", "Hostname", Environment.MachineName),
            new("user", "Username", WindowsIdentity.GetCurrent().Name) };
        // Compare the account SID's domain with the local machine's account domain,
        // rather than guessing from computer/domain display names.
        string context;
        try
        {
            var local = new NTAccount(Environment.MachineName, Environment.UserName)
                .Translate(typeof(SecurityIdentifier)) as SecurityIdentifier;
            context = local == WindowsIdentity.GetCurrent().User ? "Local account" : "Domain account";
        }
        catch (IdentityNotMappedException) { context = "Domain / external account"; }
        fields.Add(new("context", "Logon context", context));
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            var properties = adapter.GetIPProperties();
            var addresses = properties.UnicastAddresses.Where(a => a.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                .Select(a => $"{a.Address}/{a.PrefixLength}");
            fields.Add(new("network:" + adapter.Id, adapter.Name + " — IP / prefix", string.Join(", ", addresses)));
            fields.Add(new("dns:" + adapter.Id, adapter.Name + " — DNS", string.Join(", ", properties.DnsAddresses)));
        }
        return fields;
    }

    public static async Task<List<InfoField>> ReadAzureAsync()
    {
        // IMDS is link-local and must bypass all HTTP proxies; never send credentials.
        using var handler = new HttpClientHandler { UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "http://169.254.169.254/metadata/instance/compute?api-version=2021-02-01");
        request.Headers.Add("Metadata", "true");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var fields = new List<InfoField>();
        foreach (var (key, label) in new[] { ("name", "Azure VM name"), ("vmSize", "Azure VM size"), ("location", "Azure region") })
            if (json.RootElement.TryGetProperty(key, out var value) && value.GetString() is { Length: > 0 } text)
                fields.Add(new("azure:" + key, label, text));
        return fields;
    }
}
