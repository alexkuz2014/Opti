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
        public bool EfficiencySupported { get; set; }
        public HashSet<int> Efficiency { get; } = new();
        public bool TrySetEfficiency(int pid, bool on)
        {
            if (!EfficiencySupported) return false;
            if (on) Efficiency.Add(pid); else Efficiency.Remove(pid);
            return true;
        }
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

    // ---------- Сброс настроек ----------

    [Fact]
    public void Drift_DetectsResetSetting_AndRestoreKeepsOriginalForRollback()
    {
        var p = TestProfiles.Load("laptop-optimus");
        var reg = Opti.Fixes.Backends.ProfileSeeder.CreateEmulation(p, out var map);
        var journal = new Opti.Fixes.Journal.InMemoryChangeJournal();
        new Opti.Fixes.Services.ApplyService(reg, journal, null).Apply(p, new[] { Opti.Fixes.Catalog.FixCatalog.Find(FixIds.DisplayMaxHz)! }, Opti.Core.Strategy.UserGoal.MaxFps);

        var drift = new Opti.Fixes.Services.DriftService(reg, journal);
        Assert.Empty(drift.Check());

        var key = journal.GetAll().Single(r => r.FixId == FixIds.DisplayMaxHz).Target;
        map[ChangeType.Display].Seed(key, StateValue.Str("60"));                    // обновление драйвера сбросило частоту
        var found = drift.Check();
        Assert.Single(found);
        Assert.Equal(StateValue.Str("60"), found[0].Current);

        Assert.Equal(1, drift.Restore(found));
        Assert.Empty(drift.Check());
        Assert.Equal(StateValue.Str(p.Displays[0].MaxHzAtCurrentResolution.ToString()), map[ChangeType.Display].Read(key));

        new Opti.Fixes.Services.RollbackService(reg, journal).RollbackAll();         // исходное значение не потеряно
        Assert.Equal(StateValue.Str(p.Displays[0].CurrentHz.ToString()), map[ChangeType.Display].Read(key));
    }

    [Fact]
    public void Drift_IgnoresRolledBackAndUnreadableSettings_HomeShowsBanner()
    {
        using var dir = new TempDir();
        var svc = new ViewModelTests.FakeServices(dir.Path, "laptop-optimus", _ => 10);
        var main = new MainViewModel(svc);
        main.Home.Refresh();
        Assert.Null(main.Home.DriftText);

        var plan = svc.Workflow.Catalog.Single(f => f.Id == FixIds.DisplayMaxHz);
        var session = svc.Workflow.StartSession(Opti.Analysis.SystemAnalyzer.FromProfile(svc.Profile), Opti.Core.Strategy.UserGoal.MaxFps);
        svc.Workflow.Apply(session, new[] { plan });
        main.Home.Refresh();
        Assert.Null(main.Home.DriftText);                                            // всё на месте

        var target = svc.Journal.GetAll().Single(r => r.FixId == FixIds.DisplayMaxHz).Target;
        svc.Map[ChangeType.Display].Seed(target, StateValue.Str("60"));
        main.Home.Refresh();
        Assert.Contains("частота экрана", main.Home.DriftText);
        main.Home.RestoreDriftCommand.Execute(null);
        Assert.Null(main.Home.DriftText);
    }

    // ---------- EcoQoS ----------

    [Fact]
    public void Throttler_SetsEfficiencyModeAndClearsItAfterGame()
    {
        var f = new FakeProcs { EfficiencySupported = true }.Add(1, "chrome", 300).Add(10, "GTA5", 3000);
        var t = new BackgroundThrottler(f);
        t.SetGames(Array.Empty<string>());
        t.Tick();
        Assert.True(f.Efficiency.Contains(1));
        f.Procs.RemoveAll(p => p.Name == "GTA5");
        t.Tick();
        Assert.False(f.Efficiency.Contains(1));
        Assert.Equal(ProcPriority.Normal, f.Procs.Single().Priority);
    }

    [Fact]
    public void Throttler_WorksWhereEfficiencyModeIsUnavailable()
    {
        var f = new FakeProcs { EfficiencySupported = false }.Add(1, "chrome", 300).Add(10, "GTA5", 3000);
        var t = new BackgroundThrottler(f);
        t.SetGames(Array.Empty<string>());
        Assert.Equal(1, t.Tick().Throttled);
        Assert.Empty(f.Efficiency);
        t.Restore();
        Assert.Equal(ProcPriority.Normal, f.Procs.First().Priority);
    }

    // ---------- PCIe, NVMe, MSI, RSS ----------

    [Theory]
    [InlineData("16, 16\n", 16, 16)]
    [InlineData("4, 16", 4, 16)]
    public void ParsePcieWidth(string text, int cur, int max) => Assert.Equal((cur, max), ToolParsers.ParseNvidiaPcieWidth(text));

    [Fact]
    public void ParsePcieWidth_GarbageGivesNull() => Assert.Null(ToolParsers.ParseNvidiaPcieWidth("N/A, N/A"));

    [Theory]
    [InlineData(4, 16, false, true)]
    [InlineData(8, 16, false, true)]
    [InlineData(16, 16, false, false)]
    [InlineData(12, 16, false, false)]   // нестандартная ширина не диагностируем
    [InlineData(4, 16, true, false)]     // у ноутбуков ширина штатно меньше
    [InlineData(4, 8, false, false)]     // карта сама x8
    public void PcieWidth_OnlyOnDesktopWhenBelowCardMaximum(int cur, int max, bool laptop, bool expected)
    {
        var p = new SystemProfile { Device = { IsLaptop = laptop }, Gpus = { new GpuInfo { Name = "NVIDIA GeForce RTX 3070", Vendor = GpuVendor.Nvidia } }, Graphics = { PcieWidthCurrent = cur, PcieWidthMax = max } };
        Assert.Equal(expected, TestProfiles.Findings(p).Any(f => f.Id == "GPU_PCIE_WIDTH"));
    }

    [Fact]
    public void NvmeHot_UsesCurrentOrLifetimeMaximum_IgnoresSataAndColdDrives()
    {
        var p = new SystemProfile { Disks = { new DiskInfo { Model = "Fast NVMe", MediaType = DiskMediaType.Nvme, TemperatureC = 41, TemperatureMaxC = 84 }, new DiskInfo { Model = "Cold", MediaType = DiskMediaType.Nvme, TemperatureC = 38, TemperatureMaxC = 60 }, new DiskInfo { Model = "Sata", MediaType = DiskMediaType.Ssd, TemperatureC = 70 } } };
        var f = TestProfiles.Findings(p).Single(x => x.Id == "DISK_NVME_HOT");
        Assert.Single(f.Evidence);
        Assert.Contains("Fast NVMe", f.Evidence[0]);
        Assert.Contains("84", f.Evidence[0]);
    }

    [Fact]
    public void Msi_OnlyExplicitZeroIsReported()
    {
        var p = new SystemProfile { Gpus = { new GpuInfo { Name = "NVIDIA GeForce RTX 4070", Vendor = GpuVendor.Nvidia, MsiSupported = null } } };
        Assert.DoesNotContain(TestProfiles.Findings(p), f => f.Id == "GPU_MSI_OFF");   // значения нет — это «по умолчанию»
        p.Gpus[0].MsiSupported = 1;
        Assert.DoesNotContain(TestProfiles.Findings(p), f => f.Id == "GPU_MSI_OFF");
        p.Gpus[0].MsiSupported = 0;
        var f = TestProfiles.Findings(p).Single(x => x.Id == "GPU_MSI_OFF");
        Assert.Empty(f.FixIds);                                                         // реестр устройства Opti не правит
    }

    [Fact]
    public void Rss_FixAppliesOnlyWhereDisabled_AndRollsBack()
    {
        var p = new SystemProfile
        {
            Cpu = { Threads = 12 },
            Network = { new NetAdapterInfo { Name = "Ethernet", Kind = NetAdapterKind.Ethernet, IsUp = true, RssEnabled = false }, new NetAdapterInfo { Name = "Wi-Fi", Kind = NetAdapterKind.WiFi, IsUp = true, RssEnabled = true } },
        };
        Assert.Contains(FixIds.NetRssOn, TestProfiles.Findings(p).Single(f => f.Id == "NET_RSS_OFF").FixIds);
        var reg = Opti.Fixes.Backends.ProfileSeeder.CreateEmulation(p, out var map);
        var journal = new Opti.Fixes.Journal.InMemoryChangeJournal();
        new Opti.Fixes.Services.ApplyService(reg, journal, null).Apply(p, new[] { Opti.Fixes.Catalog.FixCatalog.Find(FixIds.NetRssOn)! }, Opti.Core.Strategy.UserGoal.MaxFps);
        Assert.Equal(StateValue.Str("Enabled"), map[ChangeType.NetAdapter].Read(Opti.Fixes.Backends.Targets.NetAdapter("Ethernet", "Rss")));
        Assert.Single(journal.GetAll());                                                // Wi-Fi уже был включён — не трогали
        new Opti.Fixes.Services.RollbackService(reg, journal).RollbackAll();
        Assert.Equal(StateValue.Str("Disabled"), map[ChangeType.NetAdapter].Read(Opti.Fixes.Backends.Targets.NetAdapter("Ethernet", "Rss")));

        p.Cpu.Threads = 2;                                                              // на двух потоках RSS ничего не даёт
        Assert.DoesNotContain(TestProfiles.Findings(p), f => f.Id == "NET_RSS_OFF");
    }

    // ---------- Лимит FPS и карточка ----------

    [Theory]
    [InlineData(144, true, 138)]
    [InlineData(165, true, 157)]
    [InlineData(240, true, 224)]
    [InlineData(144, false, 144)]
    [InlineData(144, null, 144)]
    [InlineData(0, true, 0)]
    public void FpsCap(int hz, bool? vrr, int expected) => Assert.Equal(expected, Opti.Core.Games.FpsCap.Recommend(hz, vrr));

    [Fact]
    public void GpuTips_UseVrrAwareCap()
    {
        var p = TestProfiles.Load("gta-majestic-pc");
        p.Displays[0].VrrSupported = true;
        var f = TestProfiles.Findings(p).Single(x => x.Id == "GPU_PANEL_TIPS");
        Assert.Contains(f.ManualSteps, s => s.Contains("138") && s.Contains("144 Гц"));
        Assert.Contains(TestProfiles.Findings(p).Single(x => x.Id == "GAME_GTA5_PROFILE").ManualSteps, s => s.Contains("около 138"));
    }

    [Fact]
    public void DiagnosticCard_IsShortAndAnonymous()
    {
        var p = TestProfiles.Load("gta-majestic-pc");
        p.Label = Environment.MachineName;
        var card = Opti.Workflow.Reports.DiagnosticCard.Build(p, TestProfiles.Findings(p), "1.7.0-beta");
        Assert.StartsWith("[Opti 1.7.0-beta", card);
        Assert.Contains("Ryzen 5 3600", card);
        Assert.Contains("GTX 1660", card);
        Assert.Contains("1920×1080 @ 144 Гц", card);
        Assert.DoesNotContain("Player", card);                                          // путь и имя пользователя из путей к играм не попадают
        Assert.DoesNotContain(@"C:\", card);
        Assert.True(card.Split('\n').Length <= 20);
    }

    // ---------- Находки первого запуска на Windows ----------

    [Fact]
    public void PowerShellError_IsReadableInsteadOfClixml()
    {
        const string raw = "#< CLIXML\r\n<Objs Version=\"1.1.0.1\" xmlns=\"http://schemas.microsoft.com/powershell/2004/04\"><S S=\"Error\">Set-NetAdapterPowerManagement : Не удается найти параметр, соответствующий имени параметра \"AllowComputerToTurnOffDevic_x000D__x000A_</S><S S=\"Error\">e\"._x000D__x000A_</S><S S=\"Error\">строка:1 знак:165_x000D__x000A_</S><S S=\"Error\">+ ... owerManagement -Name 'Ethernet' -AllowComputerToTurnOffDevice Disable ..._x000D__x000A_</S><S S=\"Error\">    + CategoryInfo          : InvalidArgument_x000D__x000A_</S></Objs>";
        var clean = ProcessRunner.CleanPowerShellError(raw);
        Assert.Equal("Set-NetAdapterPowerManagement : Не удается найти параметр, соответствующий имени параметра \"AllowComputerToTurnOffDevice\".", clean);
        Assert.Equal("обычная ошибка", ProcessRunner.CleanPowerShellError("  обычная ошибка \r\n"));
        Assert.Equal("", ProcessRunner.CleanPowerShellError(""));
    }

    [Fact]
    public void Drift_IgnoresFilesRewrittenByTheirOwner()
    {
        var reg = Opti.Fixes.Backends.InMemoryBackend.CreateRegistry(out var map);
        var journal = new Opti.Fixes.Journal.InMemoryChangeJournal();
        var ctx = new ChangeContext(reg, journal, "s", FixIds.ObsGameCapture, new SystemProfile());
        map[ChangeType.File].Seed("scene.json", StateValue.Str("old"));
        ctx.Set(ChangeType.File, "scene.json", StateValue.Str("new"));
        map[ChangeType.File].Seed("scene.json", StateValue.Str("rewritten by OBS"));   // OBS сохраняет сцену при каждом запуске
        Assert.Empty(new Opti.Fixes.Services.DriftService(reg, journal).Check());
    }

    [Theory]
    [InlineData(24, 250, 75)]      // 250 FPS → нагрузка втрое выше
    [InlineData(24, 100, 24)]      // слабому ПК базовая нагрузка
    [InlineData(24, 110, 24)]
    [InlineData(24, 5000, 1500)]
    [InlineData(3000, 400, 4000)]  // потолок
    public void BenchLoad_ScalesTowardsTargetFps(double baseLoad, double fps, double expected) =>
        Assert.Equal(expected, Opti.Workflow.Measuring.SyntheticRunner.ScaleLoad(baseLoad, fps));

    // ---------- Доступность сервисов и DNS ----------

    private static ServiceCheck Svc(string name, bool sysOk = true, int sysMs = 20, bool pubOk = true, int pubMs = 20, bool tcp = true, bool control = false) =>
        new() { Name = name, Host = name.ToLowerInvariant() + ".example", SystemDnsOk = sysOk, SystemDnsMs = sysMs, PublicDnsOk = pubOk, PublicDnsMs = pubMs, TcpOk = tcp, TcpMs = tcp ? 30 : 0, IsControl = control };

    private static List<Finding> Net(params ServiceCheck[] s)
    {
        var p = new SystemProfile();
        p.NetSettings.Services.AddRange(s);
        return TestProfiles.Findings(p);
    }

    [Fact]
    public void Services_AllHealthyGivesNothing() =>
        Assert.DoesNotContain(Net(Svc("Microsoft", control: true), Svc("Steam"), Svc("Discord")), f => f.Id.StartsWith("NET_DNS") || f.Id == "NET_SERVICE_BLOCKED");

    [Fact]
    public void Services_NameNotResolvedBySystemDns_PointsAtDns()
    {
        var f = Net(Svc("Microsoft", control: true), Svc("Discord", sysOk: false, sysMs: 0, pubMs: 35, tcp: false), Svc("Steam")).Single(x => x.Id == "NET_DNS_BROKEN");
        Assert.Equal(Severity.Important, f.Severity);
        Assert.Contains(f.Evidence, e => e.Contains("Discord") && e.Contains("35 мс"));
        Assert.Contains(f.ManualSteps, s => s.Contains("1.1.1.1"));
        Assert.Contains(f.ManualSteps, s => s.Contains("Clear-DnsClientCache"));
    }

    [Fact]
    public void Services_PortClosedWhileDnsAnswers_PointsAtFirewallOrBlock()
    {
        var f = Net(Svc("Microsoft", control: true), Svc("Steam"), Svc("Discord", tcp: false)).Single(x => x.Id == "NET_SERVICE_BLOCKED");
        Assert.Contains("файрвол", f.Explanation);
        Assert.Contains(f.ManualSteps, s => s.Contains("Test-NetConnection discord.example -Port 443"));
        Assert.DoesNotContain(Net(Svc("Microsoft", control: true), Svc("Discord", tcp: false)), x => x.Id == "NET_DNS_BROKEN");
    }

    [Fact]
    public void Services_NoInternetAtAll_IsNotBlamedOnServices()
    {
        var all = Net(Svc("Microsoft", sysOk: false, pubOk: false, tcp: false, control: true), Svc("Steam", sysOk: false, pubOk: false, tcp: false), Svc("Discord", sysOk: false, pubOk: false, tcp: false));
        Assert.DoesNotContain(all, f => f.Id.StartsWith("NET_DNS") || f.Id == "NET_SERVICE_BLOCKED");
    }

    [Fact]
    public void Services_SlowDnsNeedsTwoServicesAndClearGap()
    {
        Assert.Contains(Net(Svc("Microsoft", control: true), Svc("Steam", sysMs: 220, pubMs: 25), Svc("Discord", sysMs: 180, pubMs: 30)), f => f.Id == "NET_DNS_SLOW");
        Assert.DoesNotContain(Net(Svc("Microsoft", control: true), Svc("Steam", sysMs: 220, pubMs: 25), Svc("Discord")), f => f.Id == "NET_DNS_SLOW");        // один раз — случайность
        Assert.DoesNotContain(Net(Svc("Microsoft", control: true), Svc("Steam", sysMs: 120, pubMs: 100), Svc("Discord", sysMs: 130, pubMs: 110)), f => f.Id == "NET_DNS_SLOW");
    }

    [Fact]
    public void DnsScriptOutput_IsParsedForArrayAndSingleObject()
    {
        var many = Opti.Analysis.Collectors.ServiceReachabilityCollector.ParseDns("[{\"Host\":\"discord.com\",\"SysOk\":true,\"SysMs\":42,\"PubOk\":true,\"PubMs\":18},{\"Host\":\"x.y\",\"SysOk\":false,\"SysMs\":0,\"PubOk\":true,\"PubMs\":20}]");
        Assert.Equal(2, many.Count);
        Assert.Equal(42, many[0].SystemDnsMs);
        Assert.False(many[1].SystemDnsOk);
        var one = Opti.Analysis.Collectors.ServiceReachabilityCollector.ParseDns("{\"Host\":\"a.b\",\"SysOk\":true,\"SysMs\":5,\"PubOk\":true,\"PubMs\":6}");
        Assert.Single(one);
        Assert.Empty(Opti.Analysis.Collectors.ServiceReachabilityCollector.ParseDns(""));
        Assert.Empty(Opti.Analysis.Collectors.ServiceReachabilityCollector.ParseDns("не json"));
    }

    // ---------- Автозагрузка с переключателями ----------

    [Fact]
    public void Startup_ToggleOffAndOnRestoresOriginalValue()
    {
        var reg = Opti.Fixes.Backends.InMemoryBackend.CreateRegistry(out var map);
        const string key = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run|Spotify";
        var original = new StateValue(true, "Binary", "02" + new string('0', 22));
        map[ChangeType.Registry].Seed(key, original);
        var item = new StartupItem { Name = "Spotify", ApprovedKey = key, Enabled = true };
        var svc = new Opti.Fixes.Services.StartupService(reg, new Opti.Fixes.Journal.InMemoryChangeJournal());

        svc.Disable(item);
        Assert.StartsWith("03000000", map[ChangeType.Registry].Read(key).Value);
        svc.Enable(item);
        Assert.Equal(original, map[ChangeType.Registry].Read(key));
    }

    [Fact]
    public void Startup_EnableOfItemDisabledElsewhereWritesEnabledFlag_AndNoKeyThrows()
    {
        var reg = Opti.Fixes.Backends.InMemoryBackend.CreateRegistry(out var map);
        const string key = @"HKCU\x|Teams";
        map[ChangeType.Registry].Seed(key, new StateValue(true, "Binary", "03000000" + new string('0', 16)));
        var svc = new Opti.Fixes.Services.StartupService(reg, new Opti.Fixes.Journal.InMemoryChangeJournal());
        svc.Enable(new StartupItem { Name = "Teams", ApprovedKey = key });
        Assert.StartsWith("02", map[ChangeType.Registry].Read(key).Value);
        Assert.Throws<InvalidOperationException>(() => svc.Disable(new StartupItem { Name = "NoKey" }));
    }

    [Fact]
    public void GamePage_ListsStartupAndToggles()
    {
        using var dir = new TempDir();
        var svc = new ViewModelTests.FakeServices(dir.Path, "desktop-rtx", _ => 10);
        var main = new MainViewModel(svc);
        main.Game.OnShown();
        Assert.Empty(main.Game.StartupItems);                         // на тестовой системе списка нет
        Assert.Null(main.Game.StartupSummary);
    }

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
