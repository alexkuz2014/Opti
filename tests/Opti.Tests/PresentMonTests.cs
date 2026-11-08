using Opti.Measure.Capture;

namespace Opti.Tests;

public class PresentMonTests
{
    [Fact]
    public void ParsesPresentMon2Csv()
    {
        const string csv = """
            Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,FrameType,CPUStartTime,MsPCLatency,MsBetweenPresents,MsBetweenAppStart,MsInPresentAPI,MsDisplayLatency
            cs2.exe,4242,0x1,DXGI,0,512,1,Hardware: Independent Flip,Application,1.0,14.2,6.9,6.9,0.2,9.5
            cs2.exe,4242,0x1,DXGI,0,512,1,Hardware: Independent Flip,Application,1.007,13.8,7.1,7.1,0.2,9.1
            cs2.exe,4242,0x1,DXGI,0,512,1,Composed: Flip,Application,1.014,NA,7.0,7.0,0.2,NA
            dwm.exe,1000,0x2,Other,1,0,0,Composed: Flip,Application,1.0,NA,16.6,16.6,0,NA
            """;
        var data = PresentMonCsv.Parse(new StringReader(csv));
        var game = data[0];
        Assert.Equal("cs2.exe", game.Application);
        Assert.Equal(new[] { 6.9, 7.1, 7.0 }, game.FrameTimesMs);
        Assert.Equal(2, game.PresentModes["Hardware: Independent Flip"]);
        Assert.Equal(2.0 / 3, game.IndependentFlipShare!.Value, 6);
        Assert.Equal(new[] { 14.2, 13.8 }, game.PcLatencyMs);
        Assert.Equal(2, game.DisplayLatencyMs.Count);
    }

    [Fact]
    public void ParsesPresentMon1Csv_WithQuotedNames()
    {
        const string csv = "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,Dropped,TimeInSeconds,msInPresentAPI,msBetweenPresents,msUntilRenderComplete,msUntilDisplayed\n" +
                           "\"Game, The.exe\",77,0x1,DXGI,1,0,0,Composed: Flip,0,0.5,0.1,16.67,2.0,20.1\n" +
                           "\"Game, The.exe\",77,0x1,DXGI,1,0,0,Composed: Flip,0,0.52,0.1,16.66,2.0,19.9\n";
        var game = PresentMonCsv.Parse(new StringReader(csv)).Single();
        Assert.Equal("Game, The.exe", game.Application);
        Assert.Equal(2, game.FrameTimesMs.Count);
        Assert.Equal(0, game.IndependentFlipShare);
        Assert.Equal(20.0, game.DisplayLatencyMs.Average(), 6);
        Assert.Empty(game.PcLatencyMs);
    }

    [Fact]
    public void MissingFrameColumn_Throws() =>
        Assert.Throws<InvalidDataException>(() => PresentMonCsv.Parse(new StringReader("Application,ProcessID\nx,1\n")));
}
