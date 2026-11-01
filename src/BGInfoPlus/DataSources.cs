using System.Net.NetworkInformation;
using System.IO;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using BGInfoPlus.Core;

namespace BGInfoPlus;

internal static class DataSources
{
    public static async Task<Dictionary<string, string>> ResolveAsync(IEnumerable<PlaceholderDefinition> definitions)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var requested = definitions.ToArray();
        Dictionary<string, string> azure = new(StringComparer.Ordinal);
        if (requested.Any(d => d.Kind == "Azure"))
        {
            try { azure = (await SystemInfo.ReadAzureAsync()).ToDictionary(v => v.Key["azure:".Length..], v => v.Value, StringComparer.Ordinal); }
            catch (Exception exception) { AppLog.Warning("Azure metadata could not be resolved.", exception); }
        }
        foreach (var definition in requested)
        {
            try
            {
                var value = definition.Kind switch
                {
                    "Environment" => Environment.GetEnvironmentVariable(definition.Argument),
                    "Registry" => ReadRegistry(definition.Argument),
                    "Networking" => ReadNetwork(definition.Argument),
                    "NetworkInterface" => ReadNetworkInterface(definition.Argument),
                    "UserData" => ReadUserData(definition.Argument),
                    "CIM" => ReadCimValue(definition.Argument),
                    "Azure" => azure.GetValueOrDefault(definition.Argument),
                    _ => null
                };
                if (!string.IsNullOrEmpty(value)) result[definition.Name] = LimitValue(value);
            }
            catch (Exception exception) { AppLog.Warning($"The {definition.Kind} source for {{{definition.Name}}} could not be resolved.", exception); }
        }
        return result;
    }

    internal static string? ReadRegistry(string argument)
    {
        var parts = argument.Split('|', 3);
        if (parts.Length != 3) return null;
        var hive = parts[0].ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            "HKU" or "HKEY_USERS" => Registry.Users,
            _ => null
        };
        using var key = hive?.OpenSubKey(parts[1]);
        var value = key?.GetValue(parts[2]);
        return value switch
        {
            string[] strings => Join(strings),
            byte[] bytes => Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, AppLimits.MaxResolvedValueCharacters / 2))),
            _ => value?.ToString()
        };
    }

    private static string? ReadNetwork(string argument)
    {
        if (argument == "Hostname") return Environment.MachineName;
        if (argument == "Username") return WindowsIdentity.GetCurrent().Name;
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback).ToArray();
        if (argument == "IPv4") return Join(adapters.SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => $"{a.Address}/{a.PrefixLength}"));
        if (argument == "IPv6") return Join(adapters.SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6).Select(a => $"{a.Address}/{a.PrefixLength}"));
        if (argument == "DNS") return Join(adapters.SelectMany(n => n.GetIPProperties().DnsAddresses).Select(a => a.ToString()));
        if (argument == "Gateway") return Join(adapters.SelectMany(n => n.GetIPProperties().GatewayAddresses).Select(a => a.Address.ToString()));
        return null;
    }

    private static string Join(IEnumerable<string> values)
    {
        var result = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value.Length == 0 || !seen.Add(value)) continue;
            if (result.Length > 0) result.Append(", ");
            var remaining = AppLimits.MaxResolvedValueCharacters - result.Length;
            if (remaining <= 1) break;
            if (value.Length <= remaining) result.Append(value);
            else { result.Append(value.AsSpan(0, remaining - 1)); result.Append('…'); break; }
        }
        return result.ToString();
    }

    internal static string LimitValue(string value)
    {
        if (value.Length <= AppLimits.MaxResolvedValueCharacters) return value;
        var length = AppLimits.MaxResolvedValueCharacters - 1;
        if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
        return value[..length] + "…";
    }

    private static string? ReadNetworkInterface(string argument)
    {
        var separator = argument.IndexOf('|');
        if (separator < 0) return null;
        var id = argument[..separator]; var parameter = argument[(separator + 1)..];
        var adapter = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (adapter is null) return null;
        var properties = adapter.GetIPProperties();
        var ipv4 = properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).ToArray();
        var ipv6 = properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
        return parameter switch
        {
            "IPv4" => Join(ipv4.Select(a => a.Address.ToString())),
            "IPv6" => Join(ipv6.Select(a => a.Address.ToString())),
            "Prefix" => Join(ipv4.Select(a => a.PrefixLength.ToString())),
            "SubnetMask" => Join(ipv4.Select(a => a.IPv4Mask?.ToString() ?? "")),
            "Gateway" => Join(properties.GatewayAddresses.Select(a => a.Address.ToString())),
            "DNS" => Join(properties.DnsAddresses.Select(a => a.ToString())),
            "DhcpServer" => Join(properties.DhcpServerAddresses.Select(a => a.ToString())),
            "DhcpEnabled" => properties.GetIPv4Properties()?.IsDhcpEnabled.ToString(),
            "DnsSuffix" => properties.DnsSuffix,
            "Wins" => Join(properties.WinsServersAddresses.Select(a => a.ToString())),
            "MacAddress" => FormatMac(adapter.GetPhysicalAddress()),
            "Description" => adapter.Description,
            "Status" => adapter.OperationalStatus.ToString(),
            "InterfaceType" => adapter.NetworkInterfaceType.ToString(),
            "Speed" => FormatSpeed(adapter.Speed),
            "Mtu" => properties.GetIPv4Properties()?.Mtu.ToString(),
            _ => null
        };
    }

    private static string FormatMac(PhysicalAddress address) => string.Join("-", address.GetAddressBytes().Select(b => b.ToString("X2")));

    private static string FormatSpeed(long bitsPerSecond) => bitsPerSecond switch
    {
        >= 1_000_000_000 => $"{bitsPerSecond / 1_000_000_000d:0.##} Gbps",
        >= 1_000_000 => $"{bitsPerSecond / 1_000_000d:0.##} Mbps",
        >= 1_000 => $"{bitsPerSecond / 1_000d:0.##} Kbps",
        _ => $"{bitsPerSecond} bps"
    };

    private static string? ReadUserData(string argument)
    {
        var separator = argument.IndexOf('|');
        if (separator < 0) return null;
        var path = argument[..separator];
        var pointer = argument[(separator + 1)..];
        var content = ReadTextAtMost(path, InputLimits.MaxSourceCharacters);
        if (content is null) return null;
        return UserDataJson.Parse(content).FirstOrDefault(field =>
            field.Key.Equals("userdata:" + pointer, StringComparison.Ordinal))?.Value;
    }

    internal static string? ReadTextAtMost(string path, int maximumCharacters)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 4096, false);
            var result = new StringBuilder(Math.Min(maximumCharacters, 16 * 1024));
            var buffer = new char[4096];
            while (true)
            {
                var read = reader.Read(buffer, 0, Math.Min(buffer.Length, maximumCharacters + 1 - result.Length));
                if (read == 0) return result.ToString();
                result.Append(buffer, 0, read);
                if (result.Length > maximumCharacters) return null;
            }
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string? ReadCimValue(string argument) => argument switch
    {
        "OSName" => ReadRegistry(@"HKLM|SOFTWARE\Microsoft\Windows NT\CurrentVersion|ProductName"),
        "OSVersion" => ReadRegistry(@"HKLM|SOFTWARE\Microsoft\Windows NT\CurrentVersion|DisplayVersion"),
        "Manufacturer" => ReadRegistry(@"HKLM|HARDWARE\DESCRIPTION\System\BIOS|SystemManufacturer"),
        "Model" => ReadRegistry(@"HKLM|HARDWARE\DESCRIPTION\System\BIOS|SystemProductName"),
        "SerialNumber" => ReadRegistry(@"HKLM|HARDWARE\DESCRIPTION\System\BIOS|SystemSerialNumber"),
        "Processor" => ReadRegistry(@"HKLM|HARDWARE\DESCRIPTION\System\CentralProcessor\0|ProcessorNameString"),
        _ => null
    };

}
