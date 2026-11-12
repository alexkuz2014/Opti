using Opti.Core.Fixes;
using Opti.Core.Profile;
using Opti.Fixes.Backends;

namespace Opti.Fixes.Catalog;

/// <summary>Отключение алгоритма Нейгла и отложенных подтверждений TCP на активных сетевых адаптерах.</summary>
public sealed class NagleOffFix : FixBase
{
    public const string InterfacesKey = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\";

    public override string Id => FixIds.NetNagleOff;
    public override EvidenceLevel Evidence => EvidenceLevel.Varies;
    public override string Title => "Отправлять пакеты TCP без задержки (отключить алгоритм Нейгла)";
    public override string Explanation => "Для активных сетевых адаптеров ставит TcpAckFrequency = 1 и TCPNoDelay = 1: Windows перестаёт придерживать мелкие пакеты и подтверждения. " +
                                          "Снижает задержку в играх на TCP (MMO, часть MOBA и стратегий). Шутерам на UDP не помогает и не мешает. " +
                                          "Скорость скачивания не меняется. Вступает в силу после перезагрузки.";
    public override RiskLevel Risk => RiskLevel.Moderate;
    public override bool RequiresReboot => true;
    public override FixCategory Category => FixCategory.Network;

    private static IEnumerable<NetAdapterInfo> Candidates(SystemProfile p) =>
        p.Network.Where(n => n.IsUp && !string.IsNullOrEmpty(n.InterfaceGuid) && n.Kind is NetAdapterKind.Ethernet or NetAdapterKind.WiFi);

    public override bool IsApplicable(SystemProfile p) => Candidates(p).Any();
    public override bool IsAlreadyApplied(SystemProfile p) => Candidates(p).All(n => n.NagleDisabled == true);

    protected override void Execute(ChangeContext ctx)
    {
        foreach (var a in Candidates(ctx.Profile).Where(n => n.NagleDisabled != true))
        {
            SetDword(ctx, InterfacesKey + a.InterfaceGuid, "TcpAckFrequency", 1);
            SetDword(ctx, InterfacesKey + a.InterfaceGuid, "TCPNoDelay", 1);
        }
    }
}

/// <summary>Отключение Energy Efficient Ethernet и «зелёных» режимов сетевой карты.</summary>
public sealed class NetEeeOffFix : FixBase
{
    public override string Id => FixIds.NetEeeOff;
    public override EvidenceLevel Evidence => EvidenceLevel.Varies;
    public override string Title => "Отключить энергосбережение сетевой карты (EEE, Green Ethernet)";
    public override string Explanation => "Выключает Energy Efficient Ethernet и «зелёный» режим в драйвере проводной сетевой карты: линия не засыпает между пакетами. " +
                                          "Скорость соединения не меняется. Вступает в силу после перезагрузки.";
    public override bool RequiresReboot => true;
    public override FixCategory Category => FixCategory.Network;

    private static IEnumerable<NetAdapterInfo> Candidates(SystemProfile p) =>
        p.Network.Where(n => n.IsUp && n.Kind == NetAdapterKind.Ethernet && n.EnergySavingKeywords.Count > 0);

    public override bool IsApplicable(SystemProfile p) => p.Network.Any(n => n.IsUp && n.Kind == NetAdapterKind.Ethernet);
    public override bool IsAlreadyApplied(SystemProfile p) => !Candidates(p).Any();

    protected override void Execute(ChangeContext ctx)
    {
        foreach (var a in Candidates(ctx.Profile))
            foreach (var kw in a.EnergySavingKeywords)
                ctx.Set(ChangeType.NetAdapter, Targets.NetAdapterAdvanced(a.Name, kw), StateValue.Str("0"));
    }
}

/// <summary>
/// Снимает ограничения загрузки, оставленные через msconfig или «оптимизаторы»: число процессоров меньше имеющегося,
/// предел памяти, режим одного процессора.
/// </summary>
public sealed class BcdBootLimitsResetFix : FixBase
{
    public override string Id => FixIds.BcdBootLimitsReset;
    public override string Title => "Снять ограничения загрузки (число процессоров, предел памяти)";
    public override string Explanation => "Удаляет из параметров загрузки значения numproc, maxmem, onecpu, truncatememory, которые ограничивают Windows. " +
                                          "Сами по себе они ничего не ускоряют: Windows и так использует все ядра и всю память. Вступает в силу после перезагрузки.";
    public override RiskLevel Risk => RiskLevel.Moderate;
    public override bool RequiresReboot => true;
    public override bool TouchesBcd => true;
    public override FixCategory Category => FixCategory.Timers;
    public override int Priority => -5;

    public override bool IsApplicable(SystemProfile p) => Opti.Core.Os.BootLimits.Find(p).Count > 0;
    public override bool IsAlreadyApplied(SystemProfile p) => !IsApplicable(p);

    protected override void Execute(ChangeContext ctx)
    {
        if (ctx.Profile.Security.BitLockerOnSystemDrive == true && !ctx.Options.ContainsKey("bitlockerAcknowledged"))
            throw new ChangeAbortedException("Включён BitLocker: подтвердите, что ключ восстановления под рукой (опция bitlockerAcknowledged).");
        foreach (var limit in Opti.Core.Os.BootLimits.Find(ctx.Profile))
        {
            var t = Targets.Bcd(limit.Element);
            if (ctx.Read(ChangeType.Bcd, t).Exists) ctx.Delete(ChangeType.Bcd, t);
        }
    }
}

/// <summary>Включение Receive Side Scaling: входящие сетевые пакеты обрабатываются не одним ядром процессора.</summary>
public sealed class NetRssOnFix : FixBase
{
    public override string Id => FixIds.NetRssOn;
    public override string Title => "Включить масштабирование приёма пакетов (RSS)";
    public override string Explanation => "RSS по умолчанию включён: он распределяет обработку входящих сетевых пакетов по ядрам процессора. Если его отключили, всё идёт через одно ядро, и в мультиплеере с большим числом игроков возможны сетевые статтеры. " +
                                          "Сеть может пропасть на секунду при включении.";
    public override EvidenceLevel Evidence => EvidenceLevel.Varies;
    public override FixCategory Category => FixCategory.Network;

    private static IEnumerable<NetAdapterInfo> Candidates(SystemProfile p) =>
        p.Network.Where(n => n.IsUp && n.RssEnabled == false && n.Kind is NetAdapterKind.Ethernet or NetAdapterKind.WiFi);

    public override bool IsApplicable(SystemProfile p) => p.Cpu.Threads >= 4 && Candidates(p).Any();
    public override bool IsAlreadyApplied(SystemProfile p) => !Candidates(p).Any();

    protected override void Execute(ChangeContext ctx)
    {
        foreach (var a in Candidates(ctx.Profile))
            ctx.Set(ChangeType.NetAdapter, Targets.NetAdapter(a.Name, "Rss"), StateValue.Str("Enabled"));
    }
}

/// <summary>Включение TRIM для SSD.</summary>
public sealed class TrimOnFix : FixBase
{
    public const string Key = @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem";
    public const string ValueName = "DisableDeleteNotification";

    public override string Id => FixIds.TrimOn;
    public override string Title => "Включить TRIM для SSD";
    public override string Explanation => "Возвращает стандартную настройку Windows: система сообщает SSD об освободившихся блоках. Скорость записи перестаёт падать со временем. " +
                                          "Вступает в силу после перезагрузки.";
    public override bool RequiresReboot => true;
    public override FixCategory Category => FixCategory.Background;
    public override bool IsApplicable(SystemProfile p) => p.Disks.Any(d => d.MediaType is DiskMediaType.Ssd or DiskMediaType.Nvme);
    public override bool IsAlreadyApplied(SystemProfile p) => !p.Disks.Any(d => d.MediaType is DiskMediaType.Ssd or DiskMediaType.Nvme && d.TrimEnabled == false);
    protected override void Execute(ChangeContext ctx) => SetDword(ctx, Key, ValueName, 0);
}
