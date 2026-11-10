using Opti.Core.Findings;
using Opti.Core.Fixes;
using Opti.Core.Network;
using Opti.Core.Profile;

namespace Opti.Analysis.Detectors;

/// <summary>
/// Нестабильная сеть: потери пакетов и скачки пинга. Сравнение «до роутера» и «до интернета»
/// показывает, где проблема: дома (Wi-Fi, кабель) или у провайдера.
/// </summary>
public sealed class NetUnstableDetector : DetectorBase
{
    public override string Id => "NET_UNSTABLE";
    /// <summary>Потери: одна потеря из двадцати — ещё случайность, поэтому порог выше 5 %.</summary>
    public const double LossThreshold = 5.1;
    public const double JitterThresholdMs = 15;
    public const double GatewayPingThresholdMs = 10;
    public const double GatewayJitterThresholdMs = 6;

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.NetQuality is not { } q || double.IsNaN(q.IdlePingMs)) yield break;
        bool lossBad = q.PacketLossPercent > LossThreshold, jitterBad = q.IdleJitterMs > JitterThresholdMs;
        bool localKnown = q.GatewayPingMs is not null;
        bool localBad = localKnown && (q.GatewayLossPercent > LossThreshold || q.GatewayJitterMs > GatewayJitterThresholdMs || q.GatewayPingMs > GatewayPingThresholdMs);
        if (!lossBad && !jitterBad && !localBad) yield break;

        var ev = new List<string> { $"До {q.Target}: пинг {q.IdlePingMs:0} мс, скачки ±{q.IdleJitterMs:0.#} мс, потери {q.PacketLossPercent:0.#} %" };
        if (localKnown) ev.Add($"До роутера ({q.Gateway}): пинг {q.GatewayPingMs:0.#} мс, скачки ±{q.GatewayJitterMs:0.#} мс, потери {q.GatewayLossPercent:0.#} %");
        bool onWifi = p.Network.Any(n => n.IsUp && n.Kind == NetAdapterKind.WiFi) && !p.Network.Any(n => n.IsUp && n.Kind == NetAdapterKind.Ethernet);

        if (localBad)
        {
            var f = new Finding
            {
                Id = "NET_LOCAL_UNSTABLE",
                Title = onWifi ? "Связь с роутером нестабильна (Wi-Fi)" : "Связь с роутером нестабильна",
                Severity = Severity.Important,
                Explanation = "Пакеты теряются или задерживаются уже на пути от компьютера до роутера — ещё до выхода в интернет. " +
                              "В игре это выглядит как телепорты, «резинка» и скачки пинга. Провайдер здесь ни при чём: причина в Wi-Fi, кабеле или самом роутере.",
                Evidence = ev,
                IsHardwareLimitation = true,
                Links = { Sources.WifiSettings },
            };
            if (onWifi)
            {
                f.ManualSteps.Add("Подключите компьютер кабелем — это убирает проблему полностью.");
                f.ManualSteps.Add("Если кабель невозможен: сеть 5 ГГц, роутер ближе и в прямой видимости, без стен и микроволновки на пути.");
                f.ManualSteps.Add("В настройках роутера смените канал Wi-Fi на свободный (или включите автовыбор) и перезагрузите роутер.");
            }
            else
            {
                f.ManualSteps.Add("Замените сетевой кабель и попробуйте другой порт роутера.");
                f.ManualSteps.Add("Перезагрузите роутер; если ему много лет — проблема может быть в нём.");
            }
            if (p.Network.Any(n => n.IsUp && n.AllowComputerToTurnOff == true)) f.FixIds.Add(FixIds.NetAdapterPowerOff);
            yield return f;
            yield break;
        }

        yield return new Finding
        {
            Id = "NET_ISP_UNSTABLE",
            Title = lossBad ? "Потери пакетов на линии провайдера" : "Пинг в интернете скачет",
            Severity = Severity.Important,
            Explanation = (localKnown
                              ? "До роутера связь ровная, а дальше пакеты теряются или задерживаются — значит, проблема на стороне провайдера или маршрута до сервера. "
                              : "Пакеты до интернета теряются или идут с задержкой. Роутер на пинг не отвечает, поэтому точнее определить место нельзя. ") +
                          "Настройками компьютера это не исправить.",
            Evidence = ev,
            IsHardwareLimitation = true,
            ManualSteps =
            {
                "Перезагрузите роутер и повторите анализ в другое время суток — вечером линии часто перегружены.",
                "Если повторяется — обратитесь к провайдеру и покажите эти цифры.",
                "Мобильный интернет и спутник дают такие скачки всегда — для игр нужен проводной канал.",
            },
        };
    }
}

/// <summary>Высокий пинг в покое до ближайшего узла Cloudflare — обычно VPN, мобильный интернет или дальний маршрут.</summary>
public sealed class NetHighPingDetector : DetectorBase
{
    public override string Id => "NET_HIGH_PING";
    public const double ThresholdMs = 70;

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (p.NetQuality is not { } q || double.IsNaN(q.IdlePingMs) || q.IdlePingMs <= ThresholdMs) yield break;
        // 1.1.1.1 — сеть с узлами в большинстве крупных городов; до своей цели порог не применяем
        if (q.Target is not ("1.1.1.1" or "1.0.0.1" or "8.8.8.8" or "8.8.4.4")) yield break;
        bool vpn = p.NetSettings.ActiveVpnAdapters.Count > 0 || p.NetSettings.ProxyEnabled == true;
        yield return new Finding
        {
            Id = Id,
            Title = "Высокий пинг даже без нагрузки",
            Severity = Severity.Important,
            Explanation = "До ближайшего узла крупной сети обычно 5–30 мс. У вас заметно больше — в играх пинг будет ещё выше. " +
                          (vpn ? "Сейчас включён VPN или прокси: весь трафик идёт в обход, и это самая вероятная причина."
                               : "Так бывает при мобильном или спутниковом интернете, перегруженной линии или неудачном маршруте провайдера."),
            Evidence = { $"Пинг до {q.Target}: {q.IdlePingMs:0} мс" + (q.GatewayPingMs is { } g ? $", до роутера {g:0.#} мс" : "") },
            IsHardwareLimitation = !vpn,
            ManualSteps = vpn
                ? new() { "Отключите VPN / прокси на время игры или добавьте игру в исключения (раздельное туннелирование)." }
                : new() { "Выбирайте в игре ближайший регион серверов.", "Если пинг вырос недавно — обратитесь к провайдеру." },
        };
    }
}

public sealed class WifiQualityDetector : DetectorBase
{
    public override string Id => "NET_WIFI_WEAK";
    public const int WeakSignalPercent = 60;

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var up = p.Network.Where(n => n.IsUp).ToList();
        var wifi = up.FirstOrDefault(n => n.Kind == NetAdapterKind.WiFi);
        if (wifi is null || up.Any(n => n.Kind == NetAdapterKind.Ethernet)) yield break;

        var ev = new List<string>();
        var steps = new List<string>();
        bool band24 = wifi.WifiBandGhz is { } b && b < 3;
        bool weak = wifi.WifiSignalPercent is { } s && s < WeakSignalPercent;
        bool oldStandard = wifi.WifiRadioType is "802.11n" or "802.11g" or "802.11b" or "802.11a";
        if (band24)
        {
            ev.Add("Диапазон 2,4 ГГц: он общий с соседскими сетями, Bluetooth и микроволновками");
            steps.Add("Подключитесь к сети 5 ГГц того же роутера (часто с суффиксом _5G). Если её нет — включите 5 ГГц в настройках роутера.");
        }
        if (weak)
        {
            ev.Add($"Уровень сигнала: {wifi.WifiSignalPercent} %");
            steps.Add("Поставьте роутер ближе или выше, уберите преграды; USB-адаптер Wi-Fi вынесите на удлинителе из-за системного блока.");
        }
        if (oldStandard && !band24)
        {
            ev.Add($"Старый стандарт Wi-Fi: {wifi.WifiRadioType}");
            steps.Add("Роутер или адаптер работает по старому стандарту: проверьте режим сети в роутере (должен быть 802.11ac/ax).");
        }
        if (ev.Count == 0) yield break;
        if (wifi.LinkSpeedMbps is { } speed) ev.Add($"Скорость соединения с роутером: {speed} Мбит/с");
        steps.Add("Самое надёжное — кабель Ethernet.");

        yield return new Finding
        {
            Id = Id,
            Title = "Слабое подключение Wi-Fi",
            Severity = weak || band24 ? Severity.Important : Severity.Advice,
            Explanation = "При таком подключении пинг скачет, а пакеты теряются — в онлайн-играх это рывки и «телепорты» даже при высоком FPS. " +
                          "Это исправляется расположением и настройкой роутера, а не Windows.",
            Evidence = ev,
            ManualSteps = steps,
            IsHardwareLimitation = true,
            Links = { Sources.WifiSettings },
        };
    }
}

/// <summary>Гигабитная сетевая карта, которая подключилась на 100 или 10 Мбит/с, — почти всегда кабель или порт.</summary>
public sealed class EthernetSlowLinkDetector : DetectorBase
{
    public override string Id => "NET_LINK_SLOW";
    private static readonly string[] GigabitMarkers =
        { "gbe", "gigabit", "2.5g", "5g", "10g", "i210", "i211", "i217", "i218", "i219", "i225", "i226", "8111", "8168", "8125", "8126", "killer e", "aqc", "aquantia" };

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var slow = p.Network.Where(n => n.IsUp && n.Kind == NetAdapterKind.Ethernet && n.LinkSpeedMbps is > 0 and <= 100
                                        && GigabitMarkers.Any(m => (n.Description ?? "").Contains(m, StringComparison.OrdinalIgnoreCase))).ToList();
        if (slow.Count == 0) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Сетевой кабель работает на пониженной скорости",
            Severity = Severity.Advice,
            Explanation = "Сетевая карта умеет 1 Гбит/с и больше, но соединение установилось на 100 Мбит/с или ниже. " +
                          "Обычно виноват кабель (старый, перегнутый, с четырьмя жилами вместо восьми) или порт роутера. " +
                          "Игры качаются медленнее, а при повреждённом кабеле возможны и потери пакетов.",
            Evidence = slow.Select(n => $"{n.Description ?? n.Name}: {n.LinkSpeedMbps} Мбит/с").ToList(),
            IsHardwareLimitation = true,
            ManualSteps = { "Замените кабель на Cat 5e или Cat 6.", "Проверьте другой порт роутера: часть портов бывает на 100 Мбит/с." },
        };
    }
}

public sealed class VpnProxyDetector : DetectorBase
{
    public override string Id => "NET_VPN_PROXY";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var s = p.NetSettings;
        var ev = s.ActiveVpnAdapters.Select(a => $"Активный VPN-адаптер: {a}").ToList();
        if (s.ProxyEnabled == true) ev.Add("Включён системный прокси" + (string.IsNullOrWhiteSpace(s.ProxyServer) ? "" : $": {s.ProxyServer}"));
        if (ev.Count == 0) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Включён VPN или прокси",
            Severity = Severity.Advice,
            Explanation = "Весь трафик, включая игровой, идёт через дополнительный сервер. Пинг вырастает на время пути до него, " +
                          "а при загруженном сервере добавляются потери пакетов. Если VPN не нужен именно для игры — выключайте его на время игры.",
            Evidence = ev,
            ManualSteps =
            {
                "Отключите VPN перед запуском игры или включите в нём раздельное туннелирование и исключите игру.",
                "Прокси: Параметры → Сеть и Интернет → Прокси-сервер.",
            },
            Links = { Sources.ProxySettings },
        };
    }
}

public sealed class BackgroundTrafficDetector : DetectorBase
{
    public override string Id => "NET_BACKGROUND_TRAFFIC";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var apps = p.Background.NetworkHeavyApps;
        if (apps.Count == 0) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "В фоне работают программы, которые занимают интернет-канал",
            Severity = Severity.Advice,
            Explanation = "Торренты, облачные диски и менеджеры загрузок качают и раздают данные постоянно. Когда канал занят, " +
                          "пинг в игре растёт и появляются потери пакетов — особенно при раздаче (исходящий канал обычно уже входящего).",
            Evidence = apps.Select(a => $"Запущено: {a}").ToList(),
            ManualSteps = apps.Select(a => $"{a}: {NetworkApps.HintFor(a) ?? "закройте на время игры"}.").ToList(),
        };
    }
}

/// <summary>Отключённая автонастройка окна TCP — «твик», который режет скорость загрузки на быстрых каналах.</summary>
public sealed class TcpAutoTuningDetector : DetectorBase
{
    public override string Id => "NET_AUTOTUNING_OFF";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var level = p.NetSettings.TcpAutoTuning;
        if (level is null || !(level.Equals("Disabled", StringComparison.OrdinalIgnoreCase) || level.Contains("Restricted", StringComparison.OrdinalIgnoreCase)))
            yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Отключена автонастройка окна приёма TCP",
            Severity = Severity.Important,
            Explanation = "Windows не может увеличивать окно приёма, и скорость загрузки упирается в 10–30 Мбит/с даже на быстром тарифе: " +
                          "игры и обновления качаются в разы дольше. Это популярный «твик для пинга», который пинг не снижает. Стандартное значение — normal.",
            Evidence = { $"Уровень автонастройки: {level}" },
            ManualSteps = { "Командная строка от администратора: netsh int tcp set global autotuninglevel=normal" },
            Links = { Sources.TcpAutoTuning },
        };
    }
}

public sealed class NetEnergySavingDetector : DetectorBase
{
    public override string Id => "NET_EEE_ON";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        var a = p.Network.Where(n => n.IsUp && n.Kind == NetAdapterKind.Ethernet && n.EnergySavingKeywords.Count > 0).ToList();
        if (a.Count == 0) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Сетевая карта экономит энергию на паузах в трафике",
            Severity = Severity.Advice,
            Explanation = "Energy Efficient Ethernet и «зелёный» режим усыпляют линию между пакетами. Игровой трафик — это редкие мелкие пакеты, " +
                          "и на части сетевых карт и роутеров каждое «пробуждение» добавляет задержку или вызывает короткие обрывы. Экономия при этом — доли ватта.",
            Evidence = a.Select(n => $"{n.Description ?? n.Name}: включено {string.Join(", ", n.EnergySavingKeywords.Select(k => k.TrimStart('*')))}").ToList(),
            FixIds = { FixIds.NetEeeOff },
        };
    }
}

/// <summary>
/// Алгоритм Нейгла и отложенные подтверждения TCP. Честно: помогает только играм на TCP,
/// поэтому находка — кандидат, а исправление по умолчанию отмечено лишь для цели «Минимальная задержка».
/// </summary>
public sealed class NagleCandidateDetector : DetectorBase
{
    public override string Id => "NET_NAGLE_CANDIDATE";

    protected override IEnumerable<Finding> Run(SystemProfile p, DetectionContext ctx)
    {
        if (!p.Games.Any(g => g.IsOnline)) yield break;
        var a = p.Network.Where(n => n.IsUp && n.InterfaceGuid is not null && n.NagleDisabled == false).ToList();
        if (a.Count == 0) yield break;
        yield return new Finding
        {
            Id = Id,
            Title = "Задержку в играх на TCP можно уменьшить",
            Severity = Severity.Advice,
            Explanation = "По умолчанию Windows придерживает мелкие пакеты и подтверждения, чтобы отправлять их пачками (алгоритм Нейгла, отложенные ACK до 200 мс). " +
                          "В играх, которые общаются с сервером по TCP (MMO вроде World of Warcraft, часть MOBA и стратегий), это добавляет задержку. " +
                          "Шутеры на UDP (CS2, Valorant, Fortnite, Apex) разницы не заметят — им это исправление не нужно.",
            Evidence = a.Select(n => $"{n.Description ?? n.Name}: стандартные отложенные подтверждения TCP").ToList(),
            FixIds = { FixIds.NetNagleOff },
            Links = { Sources.TcpAckFrequency },
        };
    }
}
