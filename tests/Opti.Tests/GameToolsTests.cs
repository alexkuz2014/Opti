using Opti.Analysis.Detectors;
using Opti.Core.Findings;
using Opti.Core.Fixes;
using Opti.Core.Profile;
using Opti.Core.Tweaks;
using Opti.Native;
using Opti.ViewModels;
using Opti.Workflow.Game;

namespace Opti.Tests;

public class GameToolsTests
{
    private sealed class FakeProcs : IProcessControl
    {
        public List<ProcInfo> Procs { get; } = new();
        public List<(int Pid, ProcPriority Prio)> PriorityCalls { get; } = new();
        public List<int> Closed { get; } = new();
        public HashSet<int> CannotClose { get; } = new();
        public IReadOnlyList<ProcInfo> List() => Procs.ToList();
        public bool TrySetPriority(int pid, ProcPriority priority)
        {
            var i = Procs.FindIndex(p => p.Pid == pid);
            if (i < 0) return false;
            Procs[i] = Procs[i] with { Priority = priority };
            PriorityCalls.Add((pid, priority));
            return true;
        }
        public bool TryClose(int pid, TimeSpan grace)
        {
            if (CannotClose.Contains(pid)) return false;
            Closed.Add(pid);
            Procs.RemoveAll(p => p.Pid == pid);
            return true;
        }
        public FakeProcs Add(int pid, string name, long mb, ProcPriority prio = ProcPriority.Normal)
        {
            Procs.Add(new ProcInfo(pid, name, mb * 1024 * 1024, prio, true));
            return this;
        }
    }

    // ---------- Перед игрой ----------

    [Fact]
    public void Scan_GroupsByName_HidesProtectedAndSmall_SortsByMemory()
    {
        var f = new FakeProcs().Add(1, "chrome", 300).Add(2, "chrome", 700).Add(3, "svchost", 900).Add(4, "MsMpEng", 800)
            .Add(5, "discord", 500).Add(6, "tiny", 10).Add(7, "GTA5", 4000).Add(8, "explorer", 400);
        var groups = new GamePrep(f).Scan();
        Assert.Equal(new[] { "chrome", "discord" }, groups.Select(g => g.Name));
        Assert.Equal(2, groups[0].Count);
        Assert.Equal(1000L * 1024 * 1024, groups[0].Bytes);
    }

    [Fact]
    public void Close_NeverTouchesProtectedEvenIfAsked_AndCountsFailures()
    {
        var f = new FakeProcs().Add(1, "chrome", 300).Add(2, "chrome", 300).Add(3, "svchost", 100).Add(4, "MsMpEng", 100).Add(5, "discord", 100);
        f.CannotClose.Add(5);
        var r = new GamePrep(f).Close(new[] { "chrome", "svchost", "MsMpEng", "discord" });
        Assert.Equal(2, r.Closed);
        Assert.Equal(1, r.Failed);
        Assert.Equal(600L * 1024 * 1024, r.FreedBytes);
        Assert.DoesNotContain(3, f.Closed);
        Assert.DoesNotContain(4, f.Closed);
    }

    // ---------- Приглушать фон ----------

    [Fact]
    public void Throttler_LowersBackgroundWhileGameRuns_AndRestoresAfter()
    {
        var f = new FakeProcs().Add(1, "chrome", 300).Add(2, "steamwebhelper", 100).Add(3, "Discord", 100).Add(4, "obs64", 100).Add(5, "notepad", 10);
        var t = new BackgroundThrottler(f);
        t.SetGames(Array.Empty<string>());

        Assert.False(t.Tick().GameRunning);                 // игры нет — ничего не трогаем
        Assert.Empty(f.PriorityCalls);

        f.Add(10, "GTA5", 3000);
        var st = t.Tick();
        Assert.True(st.GameRunning);
        Assert.Equal("GTA5", st.GameName);
        Assert.Equal(2, st.Throttled);
        Assert.Equal(ProcPriority.BelowNormal, f.Procs.Single(p => p.Pid == 1).Priority);
        Assert.Equal(ProcPriority.Normal, f.Procs.Single(p => p.Pid == 3).Priority);   // Discord не трогаем
        Assert.Equal(ProcPriority.Normal, f.Procs.Single(p => p.Pid == 4).Priority);   // OBS тоже

        t.Tick();                                           // повторный шаг не меняет то, что уже сделано
        Assert.Equal(2, f.PriorityCalls.Count);

        f.Procs.RemoveAll(p => p.Name == "GTA5");
        Assert.False(t.Tick().GameRunning);
        Assert.Equal(ProcPriority.Normal, f.Procs.Single(p => p.Pid == 1).Priority);
        Assert.Equal(0, t.ThrottledCount);
    }

    [Fact]
    public void Throttler_KeepsUserChosenPriority_AndForgetsClosedProcesses()
    {
        var f = new FakeProcs().Add(1, "chrome", 300, ProcPriority.High).Add(2, "msedge", 300).Add(10, "cs2", 3000);
        var t = new BackgroundThrottler(f);
        t.SetGames(Array.Empty<string>());
        t.Tick();
        Assert.Equal(ProcPriority.High, f.Procs.Single(p => p.Pid == 1).Priority);      // выбор пользователя не трогаем
        f.Procs.RemoveAll(p => p.Pid == 2);                                              // браузер закрыли во время игры
        t.Tick();
        t.Restore();                                                                     // не падает на исчезнувшем процессе
        Assert.Equal(0, t.ThrottledCount);
    }

    [Fact]
    public void Throttler_UsesGameExecutablesFromAnalysis()
    {
        var f = new FakeProcs().Add(1, "chrome", 300).Add(10, "MyOnlineGame", 3000);
        var t = new BackgroundThrottler(f);
        Assert.False(t.Tick().GameRunning);
        t.SetGames(new[] { @"D:\Games\MyOnlineGame\MyOnlineGame.exe" });
        Assert.True(t.Tick().GameRunning);
    }

    // ---------- Страница «Игра» ----------

    [Fact]
    public async Task GamePage_RemembersChoice_ConfirmsBeforeClosing_AndTogglePersists()
    {
        using var dir = new TempDir();
        var svc = new ViewModelTests.FakeServices(dir.Path, "gta-majestic-pc", _ => 10);
        var f = new FakeProcs().Add(1, "chrome", 800).Add(2, "Telegram", 200);
        svc.Processes = f;
        var main = new MainViewModel(svc);

        main.Game.OnShown();
        Assert.True(main.Game.IsAvailable);
        Assert.Equal(2, main.Game.Apps.Count);

        await main.Game.PrepareCommand.ExecuteAsync(null);            // ничего не отмечено
        Assert.Empty(f.Closed);

        main.Game.Apps.Single(a => a.Name == "chrome").IsChecked = true;
        svc.ConfirmAnswer = false;
        await main.Game.PrepareCommand.ExecuteAsync(null);
        Assert.Empty(f.Closed);                                       // отказался в подтверждении

        svc.ConfirmAnswer = true;
        await main.Game.PrepareCommand.ExecuteAsync(null);
        Assert.Equal(new[] { 1 }, f.Closed);
        Assert.Equal(new[] { "chrome" }, svc.Settings.PrepareCloseList);
        Assert.Contains("Закрыто процессов: 1", main.Game.PrepResult);

        main.Game.ThrottleBackground = true;
        Assert.True(svc.Settings.ThrottleBackground);
        Assert.True(File.Exists(Path.Combine(dir.Path, "settings.json")));
        f.Add(20, "GTA5", 3000).Add(21, "msedge", 200);
        main.Game.Tick();
        Assert.Contains("GTA5", main.Game.ThrottleStatus);
        main.Game.ThrottleBackground = false;
        Assert.Equal(ProcPriority.Normal, f.Procs.Single(p => p.Pid == 21).Priority);
    }

    [Fact]
    public void GamePage_UnavailableWithoutProcessAccess()
    {
        using var dir = new TempDir();
        var svc = new ViewModelTests.FakeServices(dir.Path, "desktop-rtx", _ => 10);
        var main = new MainViewModel(svc);
        main.Game.OnShown();
        Assert.False(main.Game.IsAvailable);
        Assert.Empty(main.Game.Apps);
        main.Game.Tick();                                             // не падает
    }

    // ---------- Resizable BAR ----------

    [Theory]
    [InlineData("    BAR1 Memory Usage\n        Total                             : 256 MiB\n        Used                              : 1 MiB", 256)]
    [InlineData("    BAR1 Memory Usage\r\n        Total                             : 8192 MiB\r\n", 8192)]
    [InlineData("    FB Memory Usage\n        Total : 8192 MiB", null)]
    public void ParseBar1(string text, int? expected) => Assert.Equal(expected, ToolParsers.ParseNvidiaBar1TotalMb(text));

    [Theory]
    [InlineData("NVIDIA GeForce RTX 3060", false, true)]
    [InlineData("NVIDIA GeForce RTX 4070", false, true)]
    [InlineData("NVIDIA GeForce RTX 3060", true, false)]    // уже включён
    [InlineData("NVIDIA GeForce GTX 1660", false, false)]   // не поддерживается официально
    [InlineData("NVIDIA GeForce RTX 2060", false, false)]
    public void ResizableBar_OnlyForSupportedCardsWhereOff(string gpu, bool enabled, bool expected)
    {
        var p = new SystemProfile { Gpus = { new GpuInfo { Name = gpu, Vendor = GpuVendor.Nvidia, DedicatedVramMb = 8192 } }, Graphics = { Bar1TotalMb = enabled ? 8192 : 256, ResizableBarEnabled = enabled } };
        Assert.Equal(expected, TestProfiles.Findings(p).Any(f => f.Id == "GPU_REBAR_OFF"));
    }

    [Fact]
    public void ResizableBar_UnknownStateGivesNoFinding()
    {
        var p = new SystemProfile { Gpus = { new GpuInfo { Name = "NVIDIA GeForce RTX 4070", Vendor = GpuVendor.Nvidia } } };
        Assert.DoesNotContain(TestProfiles.Findings(p), f => f.Id == "GPU_REBAR_OFF");
    }

    // ---------- Уровень доказательности ----------

    [Fact]
    public void Evidence_LabelsHypothesesHonestly()
    {
        Assert.Equal(EvidenceLevel.Solid, FixCatalogHelper.Level(FixIds.TrimOn));
        Assert.Equal(EvidenceLevel.Solid, FixCatalogHelper.Level(FixIds.BcdBootLimitsReset));
        Assert.Equal(EvidenceLevel.Varies, FixCatalogHelper.Level(FixIds.HagsAb));
        Assert.Equal(EvidenceLevel.Varies, FixCatalogHelper.Level(FixIds.PowerSchemePerf));
        Assert.Equal(EvidenceLevel.Hypothesis, TweakCatalog.Find("MMCSS_GAMES")!.Evidence);
        Assert.Equal(EvidenceLevel.Hypothesis, TweakCatalog.Find("NO_NETWORK_THROTTLING")!.Evidence);
        Assert.Equal(EvidenceLevel.Solid, TweakCatalog.Find("SHOW_EXTENSIONS")!.Evidence);
        Assert.Equal("гипотеза", EvidenceTexts.For(EvidenceLevel.Hypothesis));
        Assert.Equal("зависит от ПК", EvidenceTexts.For(EvidenceLevel.Varies));
    }

    [Fact]
    public void NewTweaks_ToggleCleanlyAndRestoreOriginalValues()
    {
        var reg = Opti.Fixes.Backends.InMemoryBackend.CreateRegistry(out var map);
        const string games = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
        map[ChangeType.Registry].Seed(Opti.Fixes.Backends.Targets.Reg(games, "Priority"), StateValue.DWord(2));
        map[ChangeType.Registry].Seed(Opti.Fixes.Backends.Targets.Reg(games, "Scheduling Category"), StateValue.Str("Medium"));
        var svc = new Opti.Fixes.Services.TweakService(reg, new Opti.Fixes.Journal.InMemoryChangeJournal());
        var t = TweakCatalog.Find("MMCSS_GAMES")!;
        svc.Enable(t);
        Assert.True(svc.IsOn(t));
        svc.Disable(t);
        Assert.False(svc.IsOn(t));
        Assert.Equal(StateValue.DWord(2), map[ChangeType.Registry].Read(Opti.Fixes.Backends.Targets.Reg(games, "Priority")));
        Assert.Equal(StateValue.Str("Medium"), map[ChangeType.Registry].Read(Opti.Fixes.Backends.Targets.Reg(games, "Scheduling Category")));
    }

    [Fact]
    public void PeripheralSoftware_DetectedByProcessNames()
    {
        var found = Opti.Core.Hardware.PeripheralApps.Match(new[] { "explorer", "SteelSeriesGGClient", "lghub_agent", "LGHUB", "chrome" }).ToList();
        Assert.Equal(new[] { "SteelSeries GG", "Logitech G HUB" }, found);

        var p = new SystemProfile { Background = { PeripheralApps = found } };
        var f = TestProfiles.Findings(p).Single(x => x.Id == "PERIPHERAL_SOFTWARE");
        Assert.Equal(Severity.Advice, f.Severity);
        Assert.Contains(f.Evidence, e => e.Contains("Logitech"));
        Assert.Empty(f.FixIds);
        p.Background.PeripheralApps.Clear();
        Assert.DoesNotContain(TestProfiles.Findings(p), x => x.Id == "PERIPHERAL_SOFTWARE");
    }

    [Fact]
    public void GpuPanelTips_MentionGpuLoadCapAndMonitorProcessing()
    {
        var f = TestProfiles.Findings(TestProfiles.Load("gta-majestic-pc")).Single(x => x.Id == "GPU_PANEL_TIPS");
        Assert.Contains(f.ManualSteps, s => s.Contains("99–100 %"));
        Assert.Contains(f.ManualSteps, s => s.Contains("Dynamic Contrast"));
        Assert.Contains(f.ManualSteps, s => s.Contains("чистую установку"));
    }

    // ---------- Загрузка игр ----------

    private static SystemProfile Pc(params (string Letter, DiskMediaType Type, double Size, double Free, string[] Games)[] drives)
    {
        var p = new SystemProfile();
        foreach (var d in drives)
        {
            p.Disks.Add(new DiskInfo { Model = "Disk " + d.Letter, MediaType = d.Type, IsSystem = d.Letter == "C:", DriveLetters = { d.Letter }, Volumes = { new DiskVolume { Letter = d.Letter, SizeGb = d.Size, FreeGb = d.Free } } });
            foreach (var g in d.Games) p.Games.Add(new GameInfo { Name = g, InstallDir = d.Letter + @"\Games\" + g, IsOnline = true });
        }
        return p;
    }

    [Fact]
    public void GameStorage_MapsGamesToDriveKindAndFreeSpace()
    {
        var p = Pc(("C:", DiskMediaType.Nvme, 500, 200, new[] { "Fortnite" }), ("D:", DiskMediaType.Hdd, 1000, 40, new[] { "GTA V", "Majestic RP" }));
        var drives = Opti.Core.Games.GameStorage.Analyze(p);
        Assert.Equal(new[] { "C:", "D:" }, drives.Select(d => d.Letter));
        Assert.Equal(Opti.Core.Games.StorageKind.Hdd, drives[1].Kind);
        Assert.Equal(2, drives[1].Games.Count);
        Assert.True(drives[1].IsNearlyFull);                 // 4 % и меньше 50 ГБ
        Assert.False(drives[0].IsNearlyFull);
    }

    [Fact]
    public void GameStorage_BigDiskWithPlentyOfSpaceIsNotFull()
    {
        var p = Pc(("D:", DiskMediaType.Nvme, 4000, 300, new[] { "Game" }));   // 7,5 %, но 300 ГБ свободно
        Assert.False(Opti.Core.Games.GameStorage.Analyze(p)[0].IsNearlyFull);
    }

    [Fact]
    public void GameLoadTips_HddGamesAreTheFirstAdvice_AndNvmeOnlyPcStaysQuiet()
    {
        var hdd = TestProfiles.Findings(Pc(("D:", DiskMediaType.Hdd, 1000, 400, new[] { "GTA V" }))).Single(f => f.Id == "GAME_LOAD_TIPS");
        Assert.Contains("перенесите на SSD", hdd.ManualSteps[0]);
        Assert.Contains(Opti.Core.Findings.Sources.GameLoadBenchmark, hdd.Links);
        Assert.Contains(hdd.Evidence, e => e.StartsWith("D: — HDD"));
        Assert.Contains(hdd.ManualSteps, s => s.Contains("кэш шейдеров"));

        Assert.DoesNotContain(TestProfiles.Findings(Pc(("D:", DiskMediaType.Nvme, 1000, 600, new[] { "GTA V" }))), f => f.Id == "GAME_LOAD_TIPS");
    }

    [Fact]
    public void GameLoadTips_SataSsdIsHonestAboutNvme_FortniteAndAntivirusAreSpecific()
    {
        var p = Pc(("D:", DiskMediaType.Ssd, 1000, 600, new[] { "Fortnite" }));
        p.Security.AntiVirus.AddRange(new[] { "Windows Defender", "Kaspersky Free" });
        var f = TestProfiles.Findings(p).Single(x => x.Id == "GAME_LOAD_TIPS");
        Assert.Contains(f.ManualSteps, s => s.Contains("Менять диск ради этого не стоит"));
        Assert.Contains(f.ManualSteps, s => s.Contains("DirectX 12 Shaders"));
        Assert.Contains(f.ManualSteps, s => s.Contains("Kaspersky") && s.Contains("Opti этого сам не делает"));
        Assert.DoesNotContain(f.ManualSteps, s => s.Contains("Defender"));
        Assert.Contains(Opti.Core.Findings.Sources.FortniteShaders, f.Links);
    }

    [Fact]
    public void GamesDiskFull_FlagsNearlyFullGameDrive()
    {
        var f = TestProfiles.Findings(Pc(("D:", DiskMediaType.Ssd, 500, 20, new[] { "Game" }))).Single(x => x.Id == "GAME_DISK_FULL");
        Assert.Contains(f.Evidence, e => e.Contains("D:") && e.Contains("4 %"));
    }

    [Fact]
    public void DiskActivity_SummarizesAndDetectsBusyDisk()
    {
        var samples = new[]
        {
            new Opti.Analysis.Collectors.DiskSample(95, 3, 80e6, new Dictionary<string, double> { ["MsMpEng"] = 60e6, ["chrome"] = 2e6 }),
            new Opti.Analysis.Collectors.DiskSample(85, 2, 70e6, new Dictionary<string, double> { ["MsMpEng"] = 50e6 }),
            new Opti.Analysis.Collectors.DiskSample(100, 4, 90e6, new Dictionary<string, double> { ["MsMpEng"] = 70e6, ["tiny"] = 0.1e6 }),
        };
        var a = Opti.Analysis.Collectors.DiskActivityCollector.Summarize(samples)!;
        Assert.Equal(93, a.BusyPercent);
        Assert.Equal("MsMpEng", a.Top[0].Name);
        Assert.Equal(60, a.Top[0].MbPerSec);
        Assert.DoesNotContain(a.Top, w => w.Name == "tiny");

        var p = new SystemProfile { DiskActivity = a, Disks = { new DiskInfo { IsSystem = true, MediaType = DiskMediaType.Hdd } } };
        var f = TestProfiles.Findings(p).Single(x => x.Id == "DISK_BUSY");
        Assert.Equal(Severity.Important, f.Severity);
        Assert.Contains(f.ManualSteps, s => s.Contains("Defender"));

        p.DiskActivity = new DiskActivityInfo { BusyPercent = 12, QueueLength = 0.1 };
        Assert.DoesNotContain(TestProfiles.Findings(p), x => x.Id == "DISK_BUSY");
        Assert.Null(Opti.Analysis.Collectors.DiskActivityCollector.Summarize(Array.Empty<Opti.Analysis.Collectors.DiskSample>()));
    }

    [Theory]
    [InlineData("chrome#12", "chrome")]
    [InlineData("MsMpEng", "MsMpEng")]
    [InlineData("Razer Synapse 3#1", "Razer Synapse 3")]
    public void DiskActivity_NormalizesProcessNames(string raw, string expected) =>
        Assert.Equal(expected, Opti.Analysis.Collectors.DiskActivityCollector.Normalize(raw));

    [Fact]
    public void GamePage_ShowsWhereGamesLive()
    {
        using var dir = new TempDir();
        var svc = new ViewModelTests.FakeServices(dir.Path, "gta-majestic-pc", _ => 10);
        var main = new MainViewModel(svc);
        main.Game.OnShown();
        Assert.False(main.Game.HasDrives);                       // до анализа списка нет
        main.Session = svc.Workflow.StartSession(SystemAnalyzerFromProfile(svc.Profile), Opti.Core.Strategy.UserGoal.MaxFps);
        main.Game.OnShown();
        Assert.True(main.Game.HasDrives);
        Assert.Equal(2, main.Game.Drives.Count);                 // GTA V на D: (HDD), клиент Majestic в профиле на C:
        var d = main.Game.Drives.Single(x => x.Title.StartsWith("D:"));
        Assert.Contains("HDD", d.Title);
        Assert.Contains("SSD", d.Verdict);
        Assert.Equal("warn", d.Color);
    }

    private static Opti.Analysis.AnalysisResult SystemAnalyzerFromProfile(SystemProfile p) => Opti.Analysis.SystemAnalyzer.FromProfile(p);

    private static class FixCatalogHelper
    {
        public static EvidenceLevel Level(string id) => Opti.Fixes.Catalog.FixCatalog.Find(id)!.Evidence;
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opti-game-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
