using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace GameTune.Analysis.Collectors;

[SupportedOSPlatform("windows")]
internal static class Wmi
{
    public static List<ManagementBaseObject> Query(string wql, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql));
        searcher.Options.Timeout = TimeSpan.FromSeconds(20);
        return searcher.Get().Cast<ManagementBaseObject>().ToList();
    }

    public static string? Str(this ManagementBaseObject o, string prop) => Try(o, prop)?.ToString()?.Trim();
    public static long? Long(this ManagementBaseObject o, string prop) => Try(o, prop) is { } v ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : null;
    public static bool? Bool(this ManagementBaseObject o, string prop) => Try(o, prop) is { } v ? Convert.ToBoolean(v, CultureInfo.InvariantCulture) : null;

    public static DateTime? Date(this ManagementBaseObject o, string prop)
    {
        var s = o.Str(prop);
        if (string.IsNullOrEmpty(s)) return null;
        try { return ManagementDateTimeConverter.ToDateTime(s); } catch { return null; }
    }

    private static object? Try(ManagementBaseObject o, string prop)
    {
        try { return o[prop]; } catch (ManagementException) { return null; }
    }
}

[SupportedOSPlatform("windows")]
internal static class Reg
{
    public static object? Get(string path, string name)
    {
        int i = path.IndexOf('\\');
        var hive = path[..i].ToUpperInvariant() switch
        {
            "HKLM" => RegistryHive.LocalMachine,
            "HKCU" => RegistryHive.CurrentUser,
            _ => throw new ArgumentException(path),
        };
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.OpenSubKey(path[(i + 1)..]);
        return key?.GetValue(name);
    }

    public static int? Int(string path, string name) => Get(path, name) is int v ? v : null;
    public static string? Str(string path, string name) => Get(path, name)?.ToString();

    public static IEnumerable<(string Name, object? Value)> Values(string path)
    {
        int i = path.IndexOf('\\');
        var hive = path[..i].ToUpperInvariant() == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.OpenSubKey(path[(i + 1)..]);
        if (key is null) yield break;
        foreach (var n in key.GetValueNames()) yield return (n, key.GetValue(n));
    }

    public static IEnumerable<string> SubKeys(string path)
    {
        int i = path.IndexOf('\\');
        var hive = path[..i].ToUpperInvariant() == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.OpenSubKey(path[(i + 1)..]);
        return key?.GetSubKeyNames() ?? Array.Empty<string>();
    }
}
