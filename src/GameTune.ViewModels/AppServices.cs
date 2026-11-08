using GameTune.Analysis;
using GameTune.Core.Fixes;
using GameTune.Workflow;
using GameTune.Workflow.Measuring;
using GameTune.Workflow.Sessions;

namespace GameTune.ViewModels;

/// <summary>Всё, что ViewModel'ям нужно от платформы. В приложении — Windows-реализация, в тестах — эмуляция на профиле.</summary>
public interface IAppServices
{
    TuneWorkflow Workflow { get; }
    GameTuneSettings Settings { get; }
    IChangeJournal Journal { get; }
    string AppVersion { get; }
    string ReportsDir { get; }
    string DataDir { get; }

    Task<AnalysisResult> AnalyzeAsync(IProgress<string> progress, CancellationToken ct);
    bool IsModeAvailable(MeasureMode mode);
    IMeasureRunner CreateRunner(MeasureMode mode);
    /// <summary>Путь и аргументы для продолжения после перезагрузки.</summary>
    string ResumeExePath { get; }
    void RestartComputer();
    void OpenPath(string pathOrUrl);
    Task<bool> ConfirmAsync(string title, string message);
    Task<UpdateInfo?> CheckForUpdatesAsync();
}

/// <summary>Цвета темы для элементов списков (строки — WPF сам превращает их в кисти при привязке).</summary>
public static class Palette
{
    public const string Good = "#4CC38A";
    public const string Bad = "#F2766B";
    public const string Warn = "#F0B54A";
    public const string Neutral = "#9AA2AE";
    public const string Accent = "#5B9BFF";
    public const string Critical = "#F2766B";
    public const string Important = "#F0B54A";
    public const string Advice = "#7D8696";
}
