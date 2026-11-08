using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace GameTune.Workflow;

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
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GameTune", currentVersion));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var resp = await http.GetAsync($"https://api.github.com/repos/{AppInfo.Repository}/releases/latest", ct);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? AppInfo.ReleasesUrl : AppInfo.ReleasesUrl;
            return new UpdateInfo(tag.TrimStart('v'), url, IsNewer(tag, currentVersion));
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
