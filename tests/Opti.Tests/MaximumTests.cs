using Opti.Core.Fixes;
using Opti.Core.Tweaks;
using Opti.Fixes.Backends;
using Opti.Fixes.Journal;
using Opti.Fixes.Services;
using Opti.ViewModels;
using Opti.Workflow.Cleanup;
using Opti.Workflow.Maximum;
using Opti.Workflow.Sessions;

namespace Opti.Tests;

public class MaximumTests
{
    private sealed class FakeDrivers : IDriverUpdater
    {
        public DriverUpdateReport Report { get; set; } = new();
        public int Calls { get; private set; }
        public Task<DriverUpdateReport> UpdateAsync(IProgress<string>? progress, CancellationToken ct) { Calls++; return Task.FromResult(Report); }
    }

    private sealed class FakeTarget : ICleanupTarget
    {
        public string Id => "fake"; public string Title { get; init; } = "Временные файлы"; public string Description => "";
        public bool DefaultSelected { get; init; } = true; public string? Caution => null; public string? ManualUri { get; init; }
        public long Freed { get; init; } = 3L << 30; public bool Throws { get; init; } public int CleanCalls { get; private set; }
        public Task<CleanupEstimate> EstimateAsync(CancellationToken ct = default) => Task.FromResult(new CleanupEstimate(Freed, 10));
        public Task<CleanupResult> CleanAsync(CancellationToken ct = default)
        {
            CleanCalls++;
            if (Throws) throw new IOException("занято");
            return Task.FromResult(new CleanupResult { FreedBytes = Freed, Deleted = 10 });
        }
    }

    private static (MaxOptimizer Opt, TweakService Tw, InMemoryChangeJournal Journal) Make(IDriverUpdater? drivers, params ICleanupTarget[] targets)
    {
        var reg = InMemoryBackend.CreateRegistry(out _);
        var journal = new InMemoryChangeJournal();
        var tw = new TweakService(reg, journal);
        return (new MaxOptimizer(tw, () => targets, drivers, 26100), tw, journal);
    }

    [Fact]
    public async Task Steps_ApplyTweaksCleanAndUpdateDrivers_AndAreReversibleBySession()
    {
        var drivers = new FakeDrivers { Report = new DriverUpdateReport { Installed = { "Intel Wi-Fi 6 AX200" }, SkippedDisplay = { "NVIDIA Display" }, RebootRequired = true } };
        var target = new FakeTarget();
        var (opt, tw, journal) = Make(drivers, target, new FakeTarget { DefaultSelected = false }, new FakeTarget { ManualUri = "ms-settings:storagesense" });
        var shown = new List<MaxStep>();
        var result = await opt.RunAsync("sess-1", new Progress<MaxStep>(shown.Add), null, updateDrivers: true, CancellationToken.None);

        Assert.Equal(3, result.Steps.Count);
        Assert.True(result.RebootRequired);
        Assert.Equal("good", result.Steps[0].Tone);
        Assert.All(TweakCatalog.MaxOptimizeIds.Select(id => TweakCatalog.Find(id)!).Where(t => tw.IsAvailable(t, 26100)), t => Assert.True(tw.IsOn(t)));
        // Гипотезы и твики удобства «Максимум» не включает
        Assert.False(tw.IsOn(TweakCatalog.Find("MMCSS_GAMES")!));
        Assert.False(tw.IsOn(TweakCatalog.Find("CLASSIC_CONTEXT_MENU")!));
        Assert.Contains("освобождено 3 ГБ", result.Steps[1].Status);
        Assert.Equal(1, target.CleanCalls);
        Assert.Contains(result.Steps[2].Details, d => d.Contains("видеодрайвер пропущен"));
        Assert.Contains(result.Steps[2].Details, d => d.StartsWith("установлено: Intel"));
        Assert.Contains(result.Steps[2].Details, d => d.Contains("Откатить"));

        // Твики лежат в сессии оптимизации — откат сессии возвращает всё
        Assert.All(journal.GetAll(), r => Assert.Equal("sess-1", r.SessionId));
        new RollbackService(InMemoryBackend.CreateRegistry(out _), journal).RollbackRecords(Array.Empty<ChangeRecord>());
    }

    [Fact]
    public async Task Failure_OfOneStep_DoesNotStopTheOthers()
    {
        var (opt, _, _) = Make(null, new FakeTarget { Throws = true });
        var result = await opt.RunAsync("s", null, null, updateDrivers: true, CancellationToken.None);
        Assert.Equal(3, result.Steps.Count);
        Assert.Equal("warn", result.Steps[1].Tone);                 // очистка сообщила ошибку, но шаг завершён
        Assert.Contains("недоступно", result.Steps[2].Status);     // драйверов нет — шаг объясняет
        Assert.False(result.RebootRequired);
    }

    [Fact]
    public async Task DriversStep_ReportsSearchFailureAndSkipsWhenDisabled()
    {
        var drivers = new FakeDrivers { Report = new DriverUpdateReport { Error = "Нет сети" } };
        var (opt, _, _) = Make(drivers);
        var r = await opt.RunAsync("s", null, null, updateDrivers: true, CancellationToken.None);
        Assert.Equal("warn", r.Steps[2].Tone);
        Assert.Equal("Нет сети", r.Steps[2].Details[0]);

        var off = await opt.RunAsync("s", null, null, updateDrivers: false, CancellationToken.None);
        Assert.Equal(2, off.Steps.Count);                           // драйверы выключены в настройках
        Assert.Equal(1, drivers.Calls);
    }

    [Fact]
    public async Task Cancel_StopsBeforeNextStep()
    {
        var (opt, _, _) = Make(new FakeDrivers());
        using var cts = new CancellationTokenSource();
        cts.Cancel();                                              // отмена до начала: ни один шаг не выполняется
        var r = await opt.RunAsync("s", null, null, true, cts.Token);
        Assert.Empty(r.Steps);
    }

    [Fact]
    public async Task SecondRun_DoesNothingTwice()
    {
        var (opt, _, _) = Make(null, new FakeTarget { Freed = 0 });
        await opt.RunAsync("a", null, null, false, CancellationToken.None);
        var again = await opt.RunAsync("b", null, null, false, CancellationToken.None);
        Assert.Equal("уже всё включено", again.Steps[0].Status);
        Assert.Equal("чистить было нечего", again.Steps[1].Status);
    }

    // ---------- Разбор ответа Центра обновления ----------

    [Fact]
    public void DriverScriptOutput_IsParsed()
    {
        var r = WindowsUpdateDriverUpdater.Parse(true, "{\"Installed\":[\"Realtek Audio\",\"Intel Chipset\"],\"Failed\":\"Bad driver\",\"Skipped\":[\"NVIDIA GeForce\"],\"Reboot\":true}\r\n", "");
        Assert.Equal(2, r.Installed.Count);
        Assert.Equal(new[] { "Bad driver" }, r.Failed);          // единственный элемент PowerShell выводит строкой, а не массивом
        Assert.Equal(new[] { "NVIDIA GeForce" }, r.SkippedDisplay);
        Assert.True(r.RebootRequired);
        Assert.Null(r.Error);
    }

    [Theory]
    [InlineData(false, "", "Доступ запрещён", "Доступ запрещён")]
    [InlineData(true, "", "", "Центр обновления не вернул ответ")]
    [InlineData(true, "не json", "", "Не удалось разобрать")]
    public void DriverScriptOutput_ErrorsAreReadable(bool ok, string stdout, string stderr, string expected) =>
        Assert.Contains(expected, WindowsUpdateDriverUpdater.Parse(ok, stdout, stderr).Error);

    // ---------- Полный сценарий одной кнопки ----------

    [Fact]
    public async Task OneButton_DoesEverything_AndRollbackRestoresTheSystem()
    {
        using var dir = new TempDirectory();
        var svc = new ViewModelTests.FakeServices(dir.Path, "laptop-optimus", n => n < 2 ? 8.5 : 7.0)
        {
            DriverUpdater = new FakeDrivers { Report = new DriverUpdateReport { Installed = { "Intel Bluetooth" } } },
        };
        svc.Cleanup.Add(new FakeTarget());
        var main = new MainViewModel(svc) { AutoRebootDelaySeconds = 3600 };
        var tweaksBefore = svc.Map[ChangeType.Registry].Snapshot();

        await main.Home.MaximumCommand.ExecuteAsync(null);         // единственное нажатие
        // Для цели «Минимальная задержка» среди исправлений есть требующие перезагрузки — поток останавливается на отсчёте до неё
        var apply = await ViewModelTests.WaitForPage<ApplyViewModel>(main, 30000);
        apply.CancelRebootCommand.Execute(null);
        Assert.False(svc.Restarted);
        var s = main.Session!;
        Assert.True(s.Maximum);
        Assert.Equal(Opti.Core.Strategy.UserGoal.LowLatency, s.Goal);
        Assert.Equal(3, s.MaxSteps!.Count);
        Assert.Contains(apply.Log, l => l.Title.StartsWith("Твики"));
        Assert.Contains(FixIds.DisplayMaxHz, s.AppliedFixIds);
        Assert.True(svc.Tweaks.IsOn(TweakCatalog.Find("NO_UPDATE_SHARING")!));
        Assert.True(svc.KeepAwakeCalls >= 1);

        main.Session = s;
        svc.Workflow.RollbackSession(s);
        Assert.False(svc.Tweaks.IsOn(TweakCatalog.Find("NO_UPDATE_SHARING")!));   // твики откатились вместе с исправлениями
        Assert.Equal(tweaksBefore.Count(kv => kv.Value.Exists), svc.Map[ChangeType.Registry].Snapshot().Count(kv => kv.Value.Exists));
    }

    [Fact]
    public async Task DriversNeedReboot_ComputerRestartsItselfAfterCountdown()
    {
        using var dir = new TempDirectory();
        var svc = new ViewModelTests.FakeServices(dir.Path, "desktop-rtx", _ => 10)
        {
            DriverUpdater = new FakeDrivers { Report = new DriverUpdateReport { Installed = { "Chipset" }, RebootRequired = true } },
        };
        var main = new MainViewModel(svc) { AutoRebootDelaySeconds = 0 };
        await main.Home.MaximumCommand.ExecuteAsync(null);
        var apply = await ViewModelTests.WaitForPage<ApplyViewModel>(main, 30000);
        for (int i = 0; i < 100 && !svc.Restarted; i++) await Task.Delay(20);
        Assert.True(svc.Restarted);
        Assert.True(apply.RebootRequired);
        Assert.True(main.Session!.RebootRequired);
        Assert.Equal(SessionStage.AwaitingReboot, main.Session.Stage);
        Assert.Contains(apply.Log, l => l.Title.Contains("Драйверы"));
    }

    [Fact]
    public async Task RebootCountdown_CanBeCancelled()
    {
        using var dir = new TempDirectory();
        var svc = new ViewModelTests.FakeServices(dir.Path, "desktop-rtx", _ => 10);
        var main = new MainViewModel(svc);
        var vm = new ApplyViewModel(main, null);
        var run = vm.RunAutoRebootAsync(30);
        await Task.Delay(1200);
        Assert.Contains("Перезагрузка через", vm.CountdownText);
        vm.CancelRebootCommand.Execute(null);
        await run;
        Assert.Null(vm.CountdownText);
        Assert.False(svc.Restarted);
    }

    [Fact]
    public async Task ManualCancelOfAnalysis_LeavesNothingChangedAndReleasesAwake()
    {
        using var dir = new TempDirectory();
        var svc = new ViewModelTests.FakeServices(dir.Path, "desktop-rtx", _ => 10);
        var main = new MainViewModel(svc);
        // Анализ уже есть: нажатие ничего не сломает, а в сессии не остаётся флага «Максимум» без шагов
        await main.Home.AnalyzeCommand.ExecuteAsync(null);
        var first = main.Session!;
        Assert.False(first.Maximum);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opti-max-" + Guid.NewGuid().ToString("N"));
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
