using System.Runtime.Versioning;
using Opti.Core.Profile;
using Opti.Native;

namespace Opti.Analysis.Collectors;

/// <summary>Включён ли Resizable BAR у видеокарты NVIDIA: смотрим размер окна BAR1 (256 МБ — выключен, равен видеопамяти — включён).</summary>
[SupportedOSPlatform("windows")]
public sealed class ResizableBarCollector : ICollector
{
    public string Name => "Resizable BAR";

    public void Collect(SystemProfile p)
    {
        if (p.DiscreteGpu is not { Vendor: GpuVendor.Nvidia }) return;
        try
        {
            var r = ProcessRunner.Instance.Run("nvidia-smi", "-q -d MEMORY", TimeSpan.FromSeconds(15));
            var link = ProcessRunner.Instance.Run("nvidia-smi", "--query-gpu=pcie.link.width.current,pcie.link.width.max --format=csv,noheader,nounits", TimeSpan.FromSeconds(15));
            if (link.Ok && ToolParsers.ParseNvidiaPcieWidth(link.StdOut) is { } w)
            {
                p.Graphics.PcieWidthCurrent = w.Current;
                p.Graphics.PcieWidthMax = w.Max;
            }
            if (!r.Ok) return;
            if (ToolParsers.ParseNvidiaBar1TotalMb(r.StdOut) is { } bar)
            {
                p.Graphics.Bar1TotalMb = bar;
                p.Graphics.ResizableBarEnabled = bar > 256;
            }
        }
        catch { /* nvidia-smi не установлен — вывода о Resizable BAR не делаем */ }
    }
}
