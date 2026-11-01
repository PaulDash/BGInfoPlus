using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.IO;
using BGInfoPlus.Core;

namespace BGInfoPlus;

internal static class SystemInfo
{
    private const int MaxMetadataBytes = 64 * 1024;
    private static readonly HttpClient MetadataClient = CreateMetadataClient();

    private static HttpClient CreateMetadataClient()
    {
        var handler = new HttpClientHandler { UseProxy = false };
        return new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(3) };
    }

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
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "http://169.254.169.254/metadata/instance/compute?api-version=2021-02-01");
        request.Headers.Add("Metadata", "true");
        try
        {
            using var response = await MetadataClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return [];
            if (response.Content.Headers.ContentLength is > MaxMetadataBytes) return [];
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var buffer = new MemoryStream(Math.Min((int)(response.Content.Headers.ContentLength ?? 4096), MaxMetadataBytes));
            var chunk = new byte[4096];
            while (true)
            {
                var read = await stream.ReadAsync(chunk);
                if (read == 0) break;
                if (buffer.Length + read > MaxMetadataBytes) return [];
                buffer.Write(chunk, 0, read);
            }
            buffer.Position = 0;
            using var json = JsonDocument.Parse(buffer);
            var fields = new List<InfoField>();
            foreach (var (key, label) in new[] { ("name", "Azure VM name"), ("vmSize", "Azure VM size"), ("location", "Azure region") })
                if (json.RootElement.TryGetProperty(key, out var value) && value.GetString() is { Length: > 0 } text)
                    fields.Add(new("azure:" + key, label, DataSources.LimitValue(text)));
            return fields;
        }
        catch (HttpRequestException) { return []; }
        catch (TaskCanceledException) { return []; }
    }
}
