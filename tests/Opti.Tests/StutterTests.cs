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
}
