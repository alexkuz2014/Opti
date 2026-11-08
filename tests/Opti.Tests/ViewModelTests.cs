using Opti.Analysis;
using Opti.Core.Fixes;
using Opti.Fixes.Backends;
using Opti.Fixes.Journal;
using Opti.Measure.Stats;
using Opti.ViewModels;
using Opti.Workflow;
using Opti.Workflow.Measuring;
using Opti.Workflow.Sessions;

namespace Opti.Tests;

public sealed class ViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("opti-vm-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private sealed class FakeRunner : IMeasureRunner
    {
        private readonly Func<int, double> _base;
        private int _n;
        public FakeRunner(Func<int, double> baseMs) => _base = baseMs;
        public string Name => "fake";
        public Task<MeasurementRun> RunAsync(MeasureRequest r, IProgress<MeasureProgress>? progress = null, CancellationToken ct = default)
        {
            int n = _n++;
            var rng = new Random(n * 31 + 7);
            double b = _base(n);
            var ft = Enumerable.Range(0, 5000).Select(_ => b + (rng.NextDouble() - 0.5) + (rng.NextDouble() < 0.02 ? b * 2 : 0)).ToList();
            progress?.Report(new MeasureProgress(1, 2, ft.Take(100).ToList(), "game", false));
            return Task.FromResult(new MeasurementRun { Label = r.Label, FrameTimesMs = ft, Conditions = new RunConditions { ProcessName = "game", OnAcPower = true } });
        }
    }

    private sealed class FakeServices : IAppServices
    {
        public FakeServices(string dir, string profile, Func<int, double> frameMs)
        {
            Profile = TestProfiles.Load(profile);
            var reg = ProfileSeeder.CreateEmulation(Profile, out var map);
            Map = map;
            Journal = new InMemoryChangeJournal();
            Settings = new OptiSettings { MeasureRuns = 2, CheckForUpdates = false };
            Workflow = new TuneWorkflow(Settings, new SessionStore(Path.Combine(dir, "sessions")), reg, Journal);
            Runner = new FakeRunner(frameMs);
            DataDir = dir;
        }
        public Opti.Core.Profile.SystemProfile Profile { get; }
        public Dictionary<ChangeType, InMemoryBackend> Map { get; }
        public FakeRunner Runner { get; }
        public TuneWorkflow Workflow { get; }
        public OptiSettings Settings { get; }
        public IChangeJournal Journal { get; }
        public string AppVersion => "test";
        public string ReportsDir => Path.Combine(DataDir, "reports");
        public string DataDir { get; }
        public List<string> Opened { get; } = new();
        public bool ConfirmAnswer { get; set; } = true;
        public bool Restarted { get; private set; }
        public Task<AnalysisResult> AnalyzeAsync(IProgress<string> progress, CancellationToken ct) { progress.Report("профиль"); return Task.FromResult(SystemAnalyzer.FromProfile(Profile)); }
        public bool IsModeAvailable(MeasureMode mode) => true;
        public IMeasureRunner CreateRunner(MeasureMode mode) => Runner;
        public string ResumeExePath => "Opti.exe";
        public void RestartComputer() => Restarted = true;
        public void OpenPath(string pathOrUrl) => Opened.Add(pathOrUrl);
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(ConfirmAnswer);
        public Task<UpdateInfo?> CheckForUpdatesAsync() => Task.FromResult<UpdateInfo?>(new UpdateInfo("9.9.9", "https://example.org", true));
        public List<Opti.Workflow.Cleanup.ICleanupTarget> Cleanup { get; } = new();
        public IReadOnlyList<Opti.Workflow.Cleanup.ICleanupTarget> CleanupTargets() => Cleanup;
        public string? StartupNotice => null;
    }

    [Fact]
    public async Task FullUserJourney_AnalyzeMeasureApplyMeasureCompare()
    {
        // «до» — 8,5 мс, «после» — 7 мс: явное улучшение
        var svc = new FakeServices(_dir, "laptop-optimus", n => n < 2 ? 8.5 : 7.0);
        var main = new MainViewModel(svc);
        Assert.Same(main.Home, main.CurrentPage);
        Assert.False(main.Nav[1].IsEnabled);

        main.Home.SelectedGoal = GoalOption.All.First(g => g.Goal == Core.Strategy.UserGoal.Streaming);
        await main.Home.AnalyzeCommand.ExecuteAsync(null);
        Assert.Same(main.Analysis, main.CurrentPage);
        Assert.True(main.Nav[1].IsEnabled);
        Assert.Contains(main.Analysis.Findings, f => f.Finding.Id == "DISP_LOW_HZ" && f.SeverityText == "КРИТИЧНО");
        Assert.NotNull(main.Analysis.Summary);

        main.Analysis.MeasureBeforeCommand.Execute(null);
        var before = Assert.IsType<MeasureViewModel>(main.CurrentPage);
        Assert.Equal(MeasurePhase.Before, before.Phase);
        before.SelectedMode = before.Modes.First(m => m.Mode == MeasureMode.Synthetic); // без отсчёта перед прогоном
        Assert.False(before.FinishCommand.CanExecute(null));
        await before.StartCommand.ExecuteAsync(null);
        Assert.Equal(2, before.Completed.Count);
        for (int i = 0; i < 50 && before.LiveFrameTimes.Count == 0; i++) await Task.Delay(20); // Progress<T> вызывается асинхронно
        Assert.NotEmpty(before.LiveFrameTimes);
        await before.FinishCommand.ExecuteAsync(null);

        Assert.Same(main.Strategy, main.CurrentPage);
        Assert.Contains(main.Strategy.Fixes, f => f.Id == FixIds.DisplayMaxHz && f.IsSelected);
        Assert.Contains(main.Strategy.AbFixes, f => f.Id == FixIds.HagsAb && !f.IsSelected);
        Assert.Contains(main.Strategy.Advice, a => a.Finding.Id == "HVCI_ON");
        Assert.True(main.Strategy.ApplyCommand.CanExecute(null));
        await main.Strategy.ApplyCommand.ExecuteAsync(null);

        var apply = Assert.IsType<ApplyViewModel>(main.CurrentPage);
        Assert.Contains(apply.Log, l => l.Status == "применено");
        Assert.Equal("144", svc.Map[ChangeType.Display].Read(Targets.DisplayRefresh(@"\\.\DISPLAY1")).Value);
        apply.ContinueCommand.Execute(null);

        var after = Assert.IsType<MeasureViewModel>(main.CurrentPage);
        Assert.Equal(MeasurePhase.After, after.Phase);
        Assert.True(after.ModeLocked);
        await after.StartCommand.ExecuteAsync(null);
        await after.FinishCommand.ExecuteAsync(null);

        var cmp = Assert.IsType<CompareViewModel>(main.CurrentPage);
        Assert.Contains(cmp.Cards, c => c.Name == "Средний FPS" && c.Color == Palette.Good);
        Assert.False(cmp.SuggestRollback);
        Assert.NotEmpty(cmp.BeforeFrames);
        Assert.Contains(cmp.AppliedFixes, f => f.FixId == FixIds.DisplayMaxHz && !f.RolledBack);
        Assert.True(main.Nav[3].IsEnabled);

        // Откат одного исправления
        await cmp.RollbackFixCommand.ExecuteAsync(cmp.AppliedFixes.First(f => f.FixId == FixIds.DisplayMaxHz));
        Assert.Equal("60", svc.Map[ChangeType.Display].Read(Targets.DisplayRefresh(@"\\.\DISPLAY1")).Value);

        cmp.ExportHtmlCommand.Execute(null);
        Assert.True(File.Exists(svc.Opened.Last()));
        cmp.FinishCommand.Execute(null);
        Assert.Same(main.History, main.CurrentPage);
        Assert.Single(main.History.Sessions);
        Assert.Equal("стало лучше", main.History.Sessions[0].OutcomeText);
    }

    [Fact]
    public async Task Worse_SuggestsRollback_AndRollbackRestoresEverything()
    {
        var svc = new FakeServices(_dir, "desktop-rtx", n => n < 2 ? 7.0 : 8.5);
        var original = svc.Map[ChangeType.Registry].Snapshot().OrderBy(k => k.Key).ToList();
        var main = new MainViewModel(svc);
        await main.Home.AnalyzeCommand.ExecuteAsync(null);
        main.Analysis.MeasureBeforeCommand.Execute(null);
        var m1 = (MeasureViewModel)main.CurrentPage;
        m1.SelectedMode = m1.Modes.First(m => m.Mode == MeasureMode.Synthetic);
        await m1.StartCommand.ExecuteAsync(null);
        await m1.FinishCommand.ExecuteAsync(null);
        await main.Strategy.ApplyCommand.ExecuteAsync(null);
        var apply = (ApplyViewModel)main.CurrentPage;
        Assert.True(apply.RebootRequired);           // MPO/BCD требуют перезагрузки
        apply.RebootNowCommand.Execute(null);
        Assert.True(svc.Restarted);

        // Имитация запуска после перезагрузки
        var s = main.Session!;
        svc.Settings.CheckForUpdates = true;
        s.AppliedAt = RebootHelper.LastBootTime.AddMinutes(-3);
        svc.Workflow.Store.Save(s);
        var main2 = new MainViewModel(svc);
        await main2.InitializeAsync();
        var post = Assert.IsType<ApplyViewModel>(main2.CurrentPage);
        Assert.Contains(post.PostRebootItems, i => i.Status == "всё в порядке");
        Assert.Equal("Доступна новая версия 9.9.9", main2.UpdateMessage);
        post.ContinueCommand.Execute(null);
        var m2 = (MeasureViewModel)main2.CurrentPage;
        await m2.StartCommand.ExecuteAsync(null);
        await m2.FinishCommand.ExecuteAsync(null);
        var cmp = (CompareViewModel)main2.CurrentPage;
        Assert.True(cmp.SuggestRollback);
        await cmp.RollbackSessionCommand.ExecuteAsync(null);
        Assert.All(cmp.AppliedFixes, f => Assert.True(f.RolledBack));
        Assert.Equal(original, svc.Map[ChangeType.Registry].Snapshot().OrderBy(k => k.Key).ToList());
    }

    [Fact]
    public async Task AbFlow_FromStrategyPage()
    {
        // A: 8 мс; B: 8 мс — эффекта нет → вариант B откатывается
        var svc = new FakeServices(_dir, "desktop-rtx", _ => 8.0);
        var main = new MainViewModel(svc);
        await main.Home.AnalyzeCommand.ExecuteAsync(null);
        main.Analysis.SkipToStrategyCommand.Execute(null);
        var hags = main.Strategy.AbFixes.Single(f => f.Id == FixIds.HagsAb);
        svc.ConfirmAnswer = true;
        await main.Strategy.StartAbCommand.ExecuteAsync(hags);
        var a = Assert.IsType<MeasureViewModel>(main.CurrentPage);
        Assert.Equal(MeasurePhase.AbA, a.Phase);
        a.SelectedMode = a.Modes.First(m => m.Mode == MeasureMode.Synthetic);
        await a.StartCommand.ExecuteAsync(null);
        await a.FinishCommand.ExecuteAsync(null);
        var applied = Assert.IsType<ApplyViewModel>(main.CurrentPage);
        Assert.True(applied.RebootRequired);
        Assert.Equal("2", svc.Map[ChangeType.Registry].Read(Targets.Reg(Opti.Fixes.Catalog.HagsAbFix.Key, "HwSchMode")).Value);

        applied.ContinueCommand.Execute(null);
        var b = Assert.IsType<MeasureViewModel>(main.CurrentPage);
        Assert.Equal(MeasurePhase.AbB, b.Phase);
        await b.StartCommand.ExecuteAsync(null);
        await b.FinishCommand.ExecuteAsync(null);
        var cmp = Assert.IsType<CompareViewModel>(main.CurrentPage);
        Assert.True(cmp.IsAb);
        Assert.Contains("возвращено исходное", cmp.Message);
        Assert.Equal("1", svc.Map[ChangeType.Registry].Read(Targets.Reg(Opti.Fixes.Catalog.HagsAbFix.Key, "HwSchMode")).Value);
    }

    [Fact]
    public async Task Strategy_StartupChoicesAndBitLockerGate()
    {
        var svc = new FakeServices(_dir, "desktop-rtx", _ => 8.0);
        svc.Profile.Security.BitLockerOnSystemDrive = true;
        var main = new MainViewModel(svc);
        await main.Home.AnalyzeCommand.ExecuteAsync(null);
        main.Analysis.SkipToStrategyCommand.Execute(null);
        var bcd = main.Strategy.Fixes.Single(f => f.Id == FixIds.BcdTimersReset);
        Assert.False(bcd.IsSelected);                       // из-за BitLocker не отмечено
        foreach (var f in main.Strategy.Fixes) f.IsSelected = false;
        bcd.IsSelected = true;
        Assert.True(main.Strategy.NeedsBitLockerAck);
        Assert.False(main.Strategy.ApplyCommand.CanExecute(null));
        main.Strategy.BitLockerAcknowledged = true;
        Assert.True(main.Strategy.ApplyCommand.CanExecute(null));
        await main.Strategy.ApplyCommand.ExecuteAsync(null);
        var apply = (ApplyViewModel)main.CurrentPage;
        Assert.Contains(apply.Log, l => l.Status == "применено");
        Assert.False(svc.Map[ChangeType.Bcd].Read(Targets.Bcd("useplatformclock")).Exists);
    }

    private static async Task<T> WaitForPage<T>(MainViewModel main, int timeoutMs = 10000) where T : PageViewModel
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (main.CurrentPage is T t) return t;
            await Task.Delay(20);
        }
        throw new TimeoutException($"Страница {typeof(T).Name} не открылась, сейчас {main.CurrentPage.GetType().Name}");
    }

    [Fact]
    public async Task AutoFix_RunsWholeCycleWithoutQuestions()
    {
        var svc = new FakeServices(_dir, "laptop-optimus", n => n < 2 ? 8.5 : 7.0);
        var main = new MainViewModel(svc);
        main.Home.SelectedGoal = GoalOption.All.First(g => g.Goal == Core.Strategy.UserGoal.Streaming);
        await main.Home.AnalyzeCommand.ExecuteAsync(null);
        Assert.True(main.Analysis.CanAutoFix);
        Assert.Contains("исправлен", main.Analysis.AutoFixText);

        await main.Analysis.AutoFixCommand.ExecuteAsync(null);
        var cmp = await WaitForPage<CompareViewModel>(main);
        var s = main.Session!;
        Assert.True(s.AutoMode);
        Assert.Equal(MeasureMode.Synthetic, s.MeasureMode);
        Assert.Equal(svc.Settings.MeasureRuns, s.Before.Count);
        Assert.Equal(svc.Settings.MeasureRuns, s.After.Count);
        Assert.Contains(FixIds.DisplayMaxHz, s.AppliedFixIds);
        Assert.Contains(cmp.Cards, c => c.Name == "Средний FPS" && c.Color == Palette.Good);
    }

    [Fact]
    public async Task AutoFix_WithReboot_ContinuesAfterRestart()
    {
        var svc = new FakeServices(_dir, "desktop-rtx", n => n < 2 ? 8.5 : 7.0);
        var main = new MainViewModel(svc);
        await main.Home.AnalyzeCommand.ExecuteAsync(null);
        await main.Analysis.AutoFixCommand.ExecuteAsync(null);
        var apply = await WaitForPage<ApplyViewModel>(main);
        Assert.True(apply.RebootRequired);

        var s = main.Session!;
        s.AppliedAt = RebootHelper.LastBootTime.AddMinutes(-2);
        svc.Workflow.Store.Save(s);
        var main2 = new MainViewModel(svc);
        await main2.InitializeAsync();
        await WaitForPage<CompareViewModel>(main2);
        Assert.Equal(svc.Settings.MeasureRuns, main2.Session!.After.Count);
    }

    [Fact]
    public async Task Measure_DefaultsToSynthetic_WithShortDuration()
    {
        var svc = new FakeServices(_dir, "desktop-rtx", _ => 8.0);
        var main = new MainViewModel(svc);
        await main.Home.AnalyzeCommand.ExecuteAsync(null);
        main.Analysis.MeasureBeforeCommand.Execute(null);
        var m = (MeasureViewModel)main.CurrentPage;
        Assert.Equal(MeasureMode.Synthetic, m.SelectedMode.Mode);
        Assert.Equal(svc.Settings.SyntheticDurationSec, m.Duration);
        Assert.DoesNotContain(m.Modes, x => x.Mode == MeasureMode.PresentMon);
        m.SelectedMode = m.Modes.First(x => x.Mode == MeasureMode.Game);
        Assert.Equal(svc.Settings.MeasureDurationSec, m.Duration);
        Assert.True(m.IsGameMode);
    }

    [Fact]
    public async Task Cleanup_EstimatesAndCleansSelected()
    {
        var svc = new FakeServices(_dir, "desktop-rtx", _ => 8.0);
        var root = Directory.CreateDirectory(Path.Combine(_dir, "junk", "temp")).FullName;
        for (int i = 0; i < 3; i++)
        {
            var f = Path.Combine(root, $"f{i}.tmp");
            File.WriteAllBytes(f, new byte[2048]);
            File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-5));
        }
        svc.Cleanup.Add(new Opti.Workflow.Cleanup.FolderCleanupTarget("t", "Временные", "", () => new[] { root }, TimeSpan.FromDays(1)));
        svc.Cleanup.Add(new Opti.Workflow.Cleanup.FolderCleanupTarget("u", "Не отмечено", "", () => new[] { root }, TimeSpan.FromDays(1), defaultSelected: false));
        var main = new MainViewModel(svc);
        await main.Cleanup.EstimateCommand.ExecuteAsync(null);
        Assert.Equal(6144, main.Cleanup.Items[0].Bytes);
        Assert.Contains("6 КБ", main.Cleanup.TotalText);
        Assert.True(main.Cleanup.CleanCommand.CanExecute(null));
        await main.Cleanup.CleanCommand.ExecuteAsync(null);
        Assert.Empty(Directory.GetFiles(root));
        Assert.Contains("Освобождено 6 КБ", main.Cleanup.ResultText);
    }

    [Fact]
    public void Settings_SaveAndExclusions()
    {
        var svc = new FakeServices(_dir, "desktop-rtx", _ => 8.0);
        var main = new MainViewModel(svc);
        var st = main.SettingsPage;
        st.MeasureRuns = 50;                                // ограничивается до 10
        st.ExcludedFixes.First(x => x.Name == FixIds.MpoReset).IsChecked = true;
        st.SaveCommand.Execute(null);
        Assert.Equal(10, svc.Settings.MeasureRuns);
        Assert.Contains(FixIds.MpoReset, svc.Settings.ExcludedFixIds);
        Assert.True(File.Exists(Path.Combine(_dir, "settings.json")));
    }
}
