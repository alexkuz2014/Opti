using Opti.Core.Findings;
using Opti.Core.Profile;
using Opti.Core.Stutter;
using Opti.ViewModels;
using Opti.Workflow.Game;

namespace Opti.Tests;

public class StutterTests
{
    private static StutterSample Calm() => new(30, 1, 1, 10, 0.2, 100, 8000, "game", 20, null, 0);
    private static List<StutterSample> Run(int seconds, Func<int, StutterSample> make) => Enumerable.Range(0, seconds).Select(make).ToList();
    private static StutterInfo Analyze(List<StutterSample> s, Func<string, bool>? game = null, double ram = 16000) => StutterAnalyzer.Analyze(s, game ?? (n => n == "GTA5"), ram);

    [Fact]
    public void CalmSystem_HasNoCauses() => Assert.Empty(Analyze(Run(60, _ => Calm())).Causes);

    [Fact]
    public void SingleSpike_IsNoise_ButThreeSecondsIsACause()
    {
        Assert.Empty(Analyze(Run(60, i => i == 10 ? Calm() with { DiskBusyPercent = 100 } : Calm())).Causes);
        var info = Analyze(Run(60, i => i is >= 10 and < 14 ? Calm() with { DiskBusyPercent = 100, DiskQueue = 5, TopDiskProcess = "MsMpEng" } : Calm()));
        var c = Assert.Single(info.Causes);
        Assert.Equal("disk", c.Kind);
        Assert.Equal(4, c.Seconds);
        Assert.Equal(new[] { "MsMpEng" }, c.Culprits);
    }

    [Fact]
    public void LongerRunsNeedFivePercent()
    {
        // 120 секунд: порог 6 секунд, а не 3
        Assert.Empty(Analyze(Run(120, i => i < 4 ? Calm() with { DpcPercent = 20 } : Calm())).Causes);
        Assert.Single(Analyze(Run(120, i => i < 7 ? Calm() with { DpcPercent = 20 } : Calm())).Causes);
    }

    [Fact]
    public void Memory_ByPagingOrLowAvailableRam()
    {
        Assert.Equal("memory", Analyze(Run(30, i => i < 5 ? Calm() with { PagesPerSec = 4000, TopCpuProcess = "chrome" } : Calm())).Causes.Single().Kind);
        var low = Analyze(Run(30, i => i < 5 ? Calm() with { AvailableMb = 900 } : Calm()), ram: 16000);        // 5,6 % свободно
        Assert.Equal("memory", low.Causes.Single().Kind);
        Assert.Empty(Analyze(Run(30, i => i < 5 ? Calm() with { AvailableMb = 900 } : Calm()), ram: 0).Causes);  // объём памяти неизвестен
    }

    [Fact]
    public void Cpu_GameVersusBackground()
    {
        var game = Analyze(Run(30, i => i < 6 ? Calm() with { CpuPercent = 99, TopCpuProcess = "GTA5", TopCpuProcessPercent = 80 } : Calm())).Causes.Single();
        Assert.Equal("cpu-game", game.Kind);
        var bg = Analyze(Run(30, i => i < 6 ? Calm() with { CpuPercent = 98, TopCpuProcess = "MsMpEng" } : Calm())).Causes.Single();
        Assert.Equal("cpu-background", bg.Kind);
        Assert.Equal(new[] { "MsMpEng" }, bg.Culprits);
    }

    [Fact]
    public void Detector_TurnsCausesIntoFindingsWithSteps()
    {
        var info = new StutterInfo { Seconds = 60, Causes = { new StutterCause { Kind = "disk", Seconds = 12, Detail = "диск был занят на 98 %", Culprits = { "OneDrive" } }, new StutterCause { Kind = "dpc", Seconds = 5, Detail = "драйверы до 22 %" } } };
        // Детектор причин фризов не входит в обычный анализ: его запускает только страница «Фризы»
        var findings = Opti.Analysis.Detectors.DetectorCatalog.RunAll(new SystemProfile { Stutter = info }, new Opti.Core.Findings.IDetector[] { new Opti.Analysis.Detectors.StutterCauseDetector() }, TestProfiles.Now);
        var disk = findings.Single(f => f.Id == "STUTTER_DISK");
        Assert.Contains(disk.Evidence, e => e.Contains("12 из 60"));
        Assert.Contains(disk.ManualSteps, s => s.Contains("OneDrive"));
        Assert.Equal(Severity.Critical, findings.Single(f => f.Id == "STUTTER_DPC").Severity);
        Assert.DoesNotContain(TestProfiles.Findings(new SystemProfile { Stutter = info }), f => f.Id.StartsWith("STUTTER"));
    }

    private sealed class FakeSampler : IStutterSampler
    {
        public int Calls;
        public Func<int, StutterSample> Make { get; init; } = _ => Calm();
        public StutterSample Sample() => Make(Calls++);
    }

    [Fact]
    public async Task Check_CollectsSamplesAndDpc_AndCanBeCancelled()
    {
        var sampler = new FakeSampler { Make = i => i < 6 ? Calm() with { DiskBusyPercent = 99, TopDiskProcess = "TiWorker" } : Calm() };
        var dpc = new DpcLatencyInfo { Drivers = { new DriverLatency { Driver = "ndis.sys", MaxDpcUs = 4000 } } };
        var r = await StutterCheck.RunAsync(sampler, (_, _) => Task.FromResult<DpcLatencyInfo?>(dpc), 10, _ => false, 16000, null, CancellationToken.None, TimeSpan.FromMilliseconds(1));
        Assert.Equal(10, r.Info.Seconds);
        Assert.Contains(r.Findings, f => f.Id == "STUTTER_DISK");
        Assert.Contains(r.Findings, f => f.Id == "DPC_LATENCY_HIGH");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = await StutterCheck.RunAsync(new FakeSampler(), null, 30, _ => false, 0, null, cts.Token, TimeSpan.FromMilliseconds(1));
        Assert.True(cancelled.Info.Seconds < 30);
    }

    [Fact]
    public async Task Check_SurvivesFailingSamplerAndDpc()
    {
        var bad = new FakeSampler { Make = i => i % 2 == 0 ? throw new InvalidOperationException() : Calm() };
        var r = await StutterCheck.RunAsync(bad, (_, _) => throw new UnauthorizedAccessException(), 6, _ => false, 0, null, CancellationToken.None, TimeSpan.FromMilliseconds(1));
        Assert.Equal(3, r.Info.Seconds);                // удачные секунды остались
        Assert.Null(r.Dpc);
    }

    [Fact]
    public async Task Page_RunsCheckAndShowsFindingsOrReassurance()
    {
        var dir = Directory.CreateTempSubdirectory("opti-stutter-").FullName;
        try
        {
            var svc = new ViewModelTests.FakeServices(dir, "gta-majestic-pc", _ => 10)
            {
                StutterSampler = new FakeSampler { Make = i => Calm() with { PagesPerSec = 5000, TopCpuProcess = "chrome" } },
            };
            var main = new MainViewModel(svc);
            main.Stutter.Seconds = 5;
            main.Stutter.OnShown();
            Assert.True(main.Stutter.IsAvailable);
            await main.Stutter.StartCommand.ExecuteAsync(null);
            Assert.True(main.Stutter.HasResult);
            Assert.Contains(main.Stutter.Findings, f => f.Finding.Id == "STUTTER_MEMORY");
            Assert.Contains("администратора", main.Stutter.Summary);          // DPC не записан

            svc.StutterSampler = new FakeSampler();
            await main.Stutter.StartCommand.ExecuteAsync(null);
            Assert.Empty(main.Stutter.Findings);
            Assert.Contains("перегрузок системы не нашли", main.Stutter.Summary);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Page_UnavailableWithoutSampler()
    {
        var dir = Directory.CreateTempSubdirectory("opti-stutter-").FullName;
        try
        {
            var main = new MainViewModel(new ViewModelTests.FakeServices(dir, "desktop-rtx", _ => 10));
            main.Stutter.OnShown();
            Assert.False(main.Stutter.IsAvailable);
            Assert.False(main.Stutter.StartCommand.CanExecute(null));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ---------- Микрозамирания ----------

    [Fact]
    public void StallTracker_CountsGapsInsideSpinLoopsButNotSleeps()
    {
        const long f = 1_000_000;                 // 1 МГц: один тик = 1 мкс
        var t = new StallTracker(f);
        t.Skip(1000);
        for (long x = 1001; x < 1500; x++) t.Observe(x);          // ровный цикл, паузы по 1 мкс
        t.Observe(1500 + 5_000);                                  // пауза 5 мс
        t.Skip(10_000);                                           // сон — это не замирание
        t.Observe(10_001);
        t.Observe(10_001 + 20_000);                               // пауза 20 мс
        t.Observe(10_001 + 20_000 + 1_000);                       // 1 мс — ниже порога записи
        var s = t.Summary(2);
        Assert.Equal(2, s.Over4Ms);
        Assert.Equal(1, s.Over16Ms);
        Assert.Equal(20, s.MaxMs);
        Assert.Equal(2, s.Worst.Count);
    }

    [Fact]
    public void Stalls_BecomeCauseOnlyWhenRepeatedOrLongerThanAFrame()
    {
        Assert.Empty(StutterAnalyzer.Analyze(Run(60, _ => Calm()), _ => false, 0, new StallSummary { Seconds = 60, Over4Ms = 4, MaxMs = 6 }).Causes);
        var many = StutterAnalyzer.Analyze(Run(60, _ => Calm()), _ => false, 0, new StallSummary { Seconds = 60, Over4Ms = 12, Over16Ms = 0, MaxMs = 9, Worst = { (10.5, 9) } });
        Assert.Equal("stall", many.Causes.Single().Kind);
        Assert.Contains("12 раз", many.Causes.Single().Detail);
        Assert.Equal("stall", StutterAnalyzer.Analyze(Run(60, _ => Calm()), _ => false, 0, new StallSummary { Seconds = 60, Over16Ms = 1, MaxMs = 30 }).Causes.Single().Kind);
    }

    private sealed class FakeStalls : IStallProbe
    {
        public StallSummary Summary { get; init; } = new();
        public StallSummary Run(TimeSpan duration, CancellationToken ct) => Summary;
    }

    [Fact]
    public async Task Check_UsesStallProbe_AndFindingExplainsMicroFreezes()
    {
        var probe = new FakeStalls { Summary = new StallSummary { Seconds = 60, Over4Ms = 30, Over16Ms = 3, MaxMs = 42 } };
        var r = await StutterCheck.RunAsync(new FakeSampler(), null, 4, _ => false, 0, null, CancellationToken.None, TimeSpan.FromMilliseconds(1), probe);
        var f = r.Findings.Single(x => x.Id == "STUTTER_STALLS");
        Assert.Equal(Severity.Important, f.Severity);
        Assert.Contains(f.Evidence, e => e.Contains("42"));
        Assert.Contains(f.ManualSteps, s => s.Contains("Максимальная производительность"));
    }

    [Fact]
    public async Task Check_SurvivesFailingStallProbe()
    {
        var r = await StutterCheck.RunAsync(new FakeSampler(), null, 3, _ => false, 0, null, CancellationToken.None, TimeSpan.FromMilliseconds(1), new ThrowingProbe());
        Assert.Equal(3, r.Info.Seconds);
        Assert.Null(r.Info.Stalls);
    }

    private sealed class ThrowingProbe : IStallProbe { public StallSummary Run(TimeSpan d, CancellationToken ct) => throw new InvalidOperationException(); }

    [Fact]
    public void RealProbe_RunsAndReturnsQuickly()
    {
        var s = new Opti.Measure.Capture.SpinStallProbe().Run(TimeSpan.FromMilliseconds(200), CancellationToken.None);
        Assert.InRange(s.Seconds, 0.15, 5);
    }

    // ---------- Wi-Fi, батарея, антивирусы ----------

    private const string Scan = """
        SSID 1 : MyHome
            Network type            : Infrastructure
            BSSID 1                 : aa:aa:aa:aa:aa:01
                 Signal             : 88%
                 Radio type         : 802.11n
                 Channel            : 6
        SSID 2 : Neighbor1
            BSSID 1                 : bb:bb:bb:bb:bb:01
                 Signal             : 70%
                 Channel            : 6
        SSID 3 : Neighbor2
            BSSID 1                 : bb:bb:bb:bb:bb:02
                 Сигнал             : 55%
                 Канал              : 5
        SSID 4 : Neighbor3
            BSSID 1                 : bb:bb:bb:bb:bb:03
                 Signal             : 60%
                 Channel            : 8
        SSID 5 : Far
            BSSID 1                 : cc:cc:cc:cc:cc:01
                 Signal             : 15%
                 Channel            : 6
        SSID 6 : Other5G
            BSSID 1                 : dd:dd:dd:dd:dd:01
                 Signal             : 90%
                 Channel            : 36
        """;

    [Fact]
    public void WifiScan_ParsesEnglishAndRussian_AndCountsOverlappingRivals()
    {
        var list = Opti.Core.Network.WifiScan.Parse(Scan);
        Assert.Equal(6, list.Count);
        Assert.Equal(5, list[2].Channel);                         // русские «Канал» и «Сигнал»
        // Свой канал 6, 2,4 ГГц: свою точку (88 %) не считаем; соперники — 6, 5, 8; слабая (15 %) и 5 ГГц — нет
        Assert.Equal(3, Opti.Core.Network.WifiScan.Interfering(6, list));
        Assert.Equal(0, Opti.Core.Network.WifiScan.Interfering(36, list));  // на 5 ГГц своя точка одна
        Assert.Equal(0, Opti.Core.Network.WifiScan.Interfering(0, list));
    }

    [Fact]
    public void WifiCrowded_OnlyWithoutCableAndAtLeastThreeRivals()
    {
        var p = new SystemProfile { Network = { new NetAdapterInfo { Name = "Wi-Fi", Kind = NetAdapterKind.WiFi, IsUp = true, WifiChannel = 6 } } };
        p.NetSettings.WifiNeighbors = Opti.Core.Network.WifiScan.Parse(Scan);
        var f = TestProfiles.Findings(p).Single(x => x.Id == "NET_WIFI_CROWDED");
        Assert.Contains(f.ManualSteps, s => s.Contains("1, 6 или 11"));
        p.Network.Add(new NetAdapterInfo { Name = "Ethernet", Kind = NetAdapterKind.Ethernet, IsUp = true });
        Assert.DoesNotContain(TestProfiles.Findings(p), x => x.Id == "NET_WIFI_CROWDED");
    }

    [Theory]
    [InlineData("40000,50000", 80)]
    [InlineData("25000,50000", 50)]
    [InlineData("55000,50000", 100)]       // оценка производителя выше проектной — не больше 100
    [InlineData("0,50000", null)]
    [InlineData("мусор", null)]
    [InlineData("500000,50000", null)]     // нереальное значение
    public void BatteryHealthParsing(string text, int? expected) => Assert.Equal(expected, Opti.Analysis.Collectors.PowerCollector.ParseBatteryHealth(text));

    [Fact]
    public void BatteryWorn_OnlyOnLaptopsBelowSixtyPercent()
    {
        var p = new SystemProfile { Device = { IsLaptop = true }, Power = { BatteryHealthPercent = 35 } };
        Assert.Equal(Severity.Important, TestProfiles.Findings(p).Single(f => f.Id == "BATTERY_WORN").Severity);
        p.Power.BatteryHealthPercent = 55;
        Assert.Equal(Severity.Advice, TestProfiles.Findings(p).Single(f => f.Id == "BATTERY_WORN").Severity);
        p.Power.BatteryHealthPercent = 85;
        Assert.DoesNotContain(TestProfiles.Findings(p), f => f.Id == "BATTERY_WORN");
        p.Device.IsLaptop = false; p.Power.BatteryHealthPercent = 20;
        Assert.DoesNotContain(TestProfiles.Findings(p), f => f.Id == "BATTERY_WORN");
    }

    [Fact]
    public void TwoThirdPartyAntivirusesAreFlagged_DefenderPlusOneIsFine()
    {
        var p = new SystemProfile();
        p.Security.AntiVirus.AddRange(new[] { "Windows Defender", "Kaspersky Free" });
        Assert.DoesNotContain(TestProfiles.Findings(p), f => f.Id == "AV_MULTIPLE");
        p.Security.AntiVirus.Add("Avast Free Antivirus");
        Assert.Equal(2, TestProfiles.Findings(p).Single(f => f.Id == "AV_MULTIPLE").Evidence.Count);
    }
}
