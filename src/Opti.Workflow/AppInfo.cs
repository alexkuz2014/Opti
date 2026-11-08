using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Opti.Workflow;

public static class AppInfo
{
    public const string Repository = "allora-dev/optim";
    public const string ReleasesUrl = "https://github.com/" + Repository + "/releases";

    public static string Version
    {
        get
        {
            var asm = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
            var v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "0.0.0";
            int plus = v.IndexOf('+');
            return plus > 0 ? v[..plus] : v;
        }
    }
}

public sealed record UpdateInfo(string LatestVersion, string Url, bool IsNewer);

/// <summary>Проверка новой версии через GitHub Releases. Только уведомление — ничего не скачивается и не запускается.</summary>
public static class UpdateChecker
{
    public static async Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Opti", currentVersion));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            // Стабильная версия смотрит только стабильные релизы; бета — и бета-релизы тоже.
            bool pre = currentVersion.Contains('-');
            var url = pre ? $"https://api.github.com/repos/{AppInfo.Repository}/releases?per_page=15"
                          : $"https://api.github.com/repos/{AppInfo.Repository}/releases/latest";
            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var releases = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new() { doc.RootElement };
            var best = releases
                .Where(r => !(r.TryGetProperty("draft", out var d) && d.GetBoolean()))
                .Select(r => (Tag: r.GetProperty("tag_name").GetString() ?? "", Url: r.TryGetProperty("html_url", out var u) ? u.GetString() ?? AppInfo.ReleasesUrl : AppInfo.ReleasesUrl))
                .Where(r => r.Tag.Length > 0)
                .Aggregate(((string Tag, string Url)?)null, (acc, r) => acc is null || IsNewer(r.Tag, acc.Value.Tag) ? r : acc);
            if (best is null) return null;
            return new UpdateInfo(best.Value.Tag.TrimStart('v'), best.Value.Url, IsNewer(best.Value.Tag, currentVersion));
        }
        catch { return null; }
    }

    /// <summary>Сравнение версий вида 1.2.3 (префикс v и суффиксы -beta допускаются; предрелиз ниже релиза).</summary>
    public static bool IsNewer(string candidate, string current)
    {
        static (Version V, bool Pre) P(string s)
        {
            s = s.Trim().TrimStart('v', 'V');
            int dash = s.IndexOf('-');
            bool pre = dash >= 0;
            if (pre) s = s[..dash];
            return (System.Version.TryParse(s.Contains('.') ? s : s + ".0", out var v) ? v : new Version(0, 0), pre);
        }
        var (a, aPre) = P(candidate);
        var (b, bPre) = P(current);
        int c = a.CompareTo(b);
        return c > 0 || (c == 0 && bPre && !aPre);
    }
}
