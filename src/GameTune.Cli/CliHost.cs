using System.Runtime.Versioning;
using GameTune.Analysis;
using GameTune.Core.Json;
using GameTune.Core.Profile;
using GameTune.Fixes.Backends;
using GameTune.Fixes.Journal;
using GameTune.Fixes.Services;
using GameTune.Workflow;
using GameTune.Workflow.Sessions;

namespace GameTune.Cli;

/// <summary>Сборка рабочего окружения CLI: настройки, журнал, бэкенды Windows, сессии.</summary>
[SupportedOSPlatform("windows")]
internal sealed class CliHost : IDisposable
{
    public GameTuneSettings Settings { get; } = GameTuneSettings.Load();
    public SqliteChangeJournal Journal { get; } = new(GameTunePaths.JournalPath);
    public TuneWorkflow Workflow { get; }

    public CliHost()
    {
        Workflow = new TuneWorkflow(Settings, new SessionStore(), WindowsBackends.Create(), Journal,
            new WindowsRestorePointService(), new WindowsResumeScheduler(), new WindowsCrashEventSource());
    }

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "gametune.exe");

    public void Dispose() => Journal.Dispose();
}

internal static class AnalysisLoader
{
    public static AnalysisResult LoadOrCollect(Args a)
    {
        if (a.Get("profile") is { } path)
        {
            var text = File.ReadAllText(path);
            // Файл отчёта analyze содержит { profile, findings } — поддерживаем оба формата
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            var profileJson = doc.RootElement.TryGetProperty("profile", out var p) ? p.GetRawText() : text;
            return SystemAnalyzer.FromProfile(GameTuneJson.Deserialize<SystemProfile>(profileJson));
        }
        Console.Write("Анализ системы: ");
        var r = SystemAnalyzer.Collect(progress: name => Console.Write(name + "… "));
        Console.WriteLine("готово.");
        return r;
    }
}
