using GameTune.Core.Profile;

namespace GameTune.Core.Strategy;

public enum UserGoal
{
    /// <summary>Минимальная задержка (соревновательные игры).</summary>
    LowLatency,
    /// <summary>Ровный фреймтайм (одиночные игры).</summary>
    Smoothness,
    MaxFps,
    /// <summary>Стрим / запись.</summary>
    Streaming,
    /// <summary>Тихо и холодно.</summary>
    QuietCool,
}

public enum DeviceClass
{
    HybridLaptop,
    IgpuLaptop,
    DesktopDgpu,
    DesktopIgpu,
}

public sealed record DeviceClassification(DeviceClass Class, bool IsWeak, bool IsModifiedOs, bool? HasMux)
{
    public static DeviceClassification From(SystemProfile p)
    {
        bool hasDgpu = p.DiscreteGpu is not null;
        DeviceClass cls = p.Device.IsLaptop
            ? (hasDgpu && p.IntegratedGpu is not null ? DeviceClass.HybridLaptop : hasDgpu ? DeviceClass.DesktopDgpu /* ноутбук только с dGPU ведёт себя как ПК */ : DeviceClass.IgpuLaptop)
            : (hasDgpu ? DeviceClass.DesktopDgpu : DeviceClass.DesktopIgpu);

        var sys = p.SystemDisk;
        bool weak = (p.Cpu.Cores > 0 && p.Cpu.Cores <= 4)
                    || (p.Memory.TotalMb > 0 && p.Memory.TotalMb <= 8 * 1024 + 512)
                    || sys?.MediaType == DiskMediaType.Hdd;

        return new DeviceClassification(cls, weak, p.Os.ModifiedBuild is not null, p.Hybrid.HasMux);
    }

    public string Describe()
    {
        var name = Class switch
        {
            DeviceClass.HybridLaptop => "Ноутбук с гибридной графикой" + (HasMux == true ? " (есть MUX)" : HasMux == false ? " (без MUX)" : ""),
            DeviceClass.IgpuLaptop => "Ноутбук только со встроенной графикой",
            DeviceClass.DesktopDgpu => "Настольный ПК с дискретной видеокартой",
            DeviceClass.DesktopIgpu => "Настольный ПК со встроенной графикой",
            _ => Class.ToString(),
        };
        if (IsWeak) name += ", слабая конфигурация";
        if (IsModifiedOs) name += ", модифицированная сборка Windows";
        return name;
    }
}

public static class GoalNames
{
    public static string Ru(this UserGoal g) => g switch
    {
        UserGoal.LowLatency => "Минимальная задержка",
        UserGoal.Smoothness => "Плавность",
        UserGoal.MaxFps => "Максимальный FPS",
        UserGoal.Streaming => "Стрим / запись",
        UserGoal.QuietCool => "Тихо и холодно",
        _ => g.ToString(),
    };
}
