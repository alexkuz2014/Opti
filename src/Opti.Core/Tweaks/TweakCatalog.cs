namespace Opti.Core.Tweaks;

public enum TweakCategory { ContextMenu, Explorer, StartAndTaskbar, Responsiveness, System, Gaming }

/// <summary>Что нужно, чтобы твик вступил в силу.</summary>
public enum TweakApplyMode { Immediate, RestartExplorer, SignOut, Reboot }

/// <summary>Значение реестра во включённом состоянии твика. Kind: DWord или String.</summary>
public sealed record TweakValue(string Key, string Name, string Kind, string On);

/// <summary>
/// Твик удобства: заметное изменение поведения Windows, которое человек включает сам.
/// Каждый — через журнал изменений: выключение возвращает ровно то значение, что было до Opti.
/// </summary>
public sealed class Tweak
{
    public required string Id { get; init; }
    public required TweakCategory Category { get; init; }
    public required string Title { get; init; }
    /// <summary>Что изменится — одной-двумя фразами, без обещаний «ускорения на 30 %».</summary>
    public required string Description { get; init; }
    public required IReadOnlyList<TweakValue> Values { get; init; }
    public TweakApplyMode ApplyMode { get; init; } = TweakApplyMode.Immediate;
    /// <summary>Минимальная сборка Windows (22000 — Windows 11).</summary>
    public int MinBuild { get; init; }
    public bool Recommended { get; init; }
    public Opti.Core.Fixes.EvidenceLevel Evidence { get; init; } = Opti.Core.Fixes.EvidenceLevel.Solid;
    public string? Link { get; init; }

    public string FixId => "TWEAK_" + Id;
}

public static class TweakCatalog
{
    private const string Adv = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string Cdm = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    public static string CategoryTitle(TweakCategory c) => c switch
    {
        TweakCategory.ContextMenu => "Контекстное меню",
        TweakCategory.Explorer => "Проводник",
        TweakCategory.StartAndTaskbar => "Пуск и панель задач",
        TweakCategory.Responsiveness => "Отзывчивость",
        TweakCategory.Gaming => "Игры и фоновая нагрузка",
        _ => "Система",
    };

    public static string CategoryIcon(TweakCategory c) => c switch
    {
        TweakCategory.ContextMenu => "",
        TweakCategory.Explorer => "",
        TweakCategory.StartAndTaskbar => "",
        TweakCategory.Responsiveness => "",
        TweakCategory.Gaming => "",
        _ => "",
    };

    public static string ApplyModeText(TweakApplyMode m) => m switch
    {
        TweakApplyMode.RestartExplorer => "перезапуск Проводника",
        TweakApplyMode.SignOut => "после повторного входа",
        TweakApplyMode.Reboot => "после перезагрузки",
        _ => "сразу",
    };

    private static readonly Tweak[] BaseTweaks = new Tweak[]
    {
        // ---------- Контекстное меню ----------
        new()
        {
            Id = "CLASSIC_CONTEXT_MENU", Category = TweakCategory.ContextMenu, MinBuild = 22000, Recommended = true,
            Title = "Полное контекстное меню, как в Windows 10",
            Description = "Правый клик сразу показывает все пункты (7-Zip, «Открыть с помощью», команды программ) — без лишнего «Показать дополнительные параметры».",
            Values = new[] { new TweakValue(@"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", "String", "") },
            ApplyMode = TweakApplyMode.RestartExplorer,
        },
        new()
        {
            Id = "FAST_MENUS", Category = TweakCategory.ContextMenu, Recommended = true,
            Title = "Быстрое открытие подменю",
            Description = "Подменю (например, «Отправить» или «Создать») открываются через 100 мс вместо 400 мс.",
            Values = new[] { new TweakValue(@"HKCU\Control Panel\Desktop", "MenuShowDelay", "String", "100") },
            ApplyMode = TweakApplyMode.SignOut,
        },

        // ---------- Проводник ----------
        new()
        {
            Id = "SHOW_EXTENSIONS", Category = TweakCategory.Explorer, Recommended = true,
            Title = "Показывать расширения файлов",
            Description = "Видно «setup.exe» и «фото.jpg.exe» — проще отличить программу от документа и заметить подделку.",
            Values = new[] { new TweakValue(Adv, "HideFileExt", "DWord", "0") },
            ApplyMode = TweakApplyMode.RestartExplorer,
        },
        new()
        {
            Id = "EXPLORER_THIS_PC", Category = TweakCategory.Explorer,
            Title = "Проводник открывается на «Этот компьютер»",
            Description = "Сразу видны диски и свободное место вместо списка недавних файлов.",
            Values = new[] { new TweakValue(Adv, "LaunchTo", "DWord", "1") },
            ApplyMode = TweakApplyMode.RestartExplorer,
        },

        // ---------- Пуск и панель задач ----------
        new()
        {
            Id = "NO_WEB_SEARCH", Category = TweakCategory.StartAndTaskbar, Recommended = true,
            Title = "Поиск в «Пуске» без интернета (Bing)",
            Description = "Поиск ищет только программы, настройки и файлы на компьютере — быстрее и без веб-результатов.",
            Values = new[] { new TweakValue(@"HKCU\Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", "DWord", "1") },
            ApplyMode = TweakApplyMode.RestartExplorer,
            Link = "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-search",
        },
        new()
        {
            Id = "TASKBAR_END_TASK", Category = TweakCategory.StartAndTaskbar, MinBuild = 22631, Recommended = true,
            Title = "«Завершить задачу» на панели задач",
            Description = "Правый клик по зависшей игре или программе на панели задач → «Завершить задачу». Без Диспетчера задач.",
            Values = new[] { new TweakValue(Adv + @"\TaskbarDeveloperSettings", "TaskbarEndTask", "DWord", "1") },
        },
        new()
        {
            Id = "NO_SUGGESTED_APPS", Category = TweakCategory.StartAndTaskbar, Recommended = true,
            Title = "Без рекламы и автоустановки приложений",
            Description = "Windows перестаёт сама ставить «рекомендованные» игры и программы и показывать советы-рекламу в «Пуске» и на экране блокировки.",
            Values = new[]
            {
                new TweakValue(Cdm, "SilentInstalledAppsEnabled", "DWord", "0"),
                new TweakValue(Cdm, "SystemPaneSuggestionsEnabled", "DWord", "0"),
                new TweakValue(Cdm, "SubscribedContent-338388Enabled", "DWord", "0"),
                new TweakValue(Cdm, "SubscribedContent-338389Enabled", "DWord", "0"),
                new TweakValue(Cdm, "SubscribedContent-310093Enabled", "DWord", "0"),
            },
        },
        new()
        {
            Id = "TASKBAR_LEFT", Category = TweakCategory.StartAndTaskbar, MinBuild = 22000,
            Title = "Значки панели задач слева",
            Description = "Кнопка «Пуск» и значки — в левом углу, как в Windows 10.",
            Values = new[] { new TweakValue(Adv, "TaskbarAl", "DWord", "0") },
        },

        // ---------- Отзывчивость ----------
        new()
        {
            Id = "INSTANT_MINIMIZE", Category = TweakCategory.Responsiveness,
            Title = "Мгновенное сворачивание окон",
            Description = "Окна сворачиваются и разворачиваются без анимации — переключение между игрой и рабочим столом ощущается быстрее.",
            Values = new[] { new TweakValue(@"HKCU\Control Panel\Desktop\WindowMetrics", "MinAnimate", "String", "0") },
            ApplyMode = TweakApplyMode.SignOut,
        },
        new()
        {
            Id = "NO_STARTUP_DELAY", Category = TweakCategory.Responsiveness,
            Title = "Автозагрузка без искусственной паузы",
            Description = "Windows не ждёт ~10 секунд перед запуском программ из автозагрузки. На медленном HDD с большой автозагрузкой лучше не включать.",
            Values = new[] { new TweakValue(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", "DWord", "0") },
            ApplyMode = TweakApplyMode.SignOut,
        },

        // ---------- Система ----------
        new()
        {
            Id = "NO_FAST_STARTUP", Category = TweakCategory.System,
            Title = "Отключить «Быстрый запуск»",
            Description = "Выключение становится настоящим: драйверы и ядро загружаются заново. Лечит «после выключения всё равно глючит, помогает только перезагрузка». Включение ПК — на пару секунд дольше.",
            Values = new[] { new TweakValue(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", "DWord", "0") },
            ApplyMode = TweakApplyMode.Reboot,
            Link = "https://learn.microsoft.com/en-us/troubleshoot/windows-client/setup-upgrade-and-drivers/disable-and-re-enable-hibernation",
        },
    };

    private const string EdgePolicy = @"HKLM\SOFTWARE\Policies\Microsoft\Edge";

    /// <summary>Твики, которые освобождают память, процессор и интернет-канал для игр. Все — документированные настройки и политики.</summary>
    private static readonly Tweak[] GamingTweaks =
    {
        new()
        {
            Id = "NO_UPDATE_SHARING", Category = TweakCategory.Gaming, Recommended = true,
            Title = "Не раздавать обновления Windows в интернет",
            Description = "Windows перестаёт отдавать скачанные обновления чужим компьютерам в интернете — исходящий канал остаётся игре, пинг не скачет. " +
                          "Свои обновления качаются как обычно, обмен внутри домашней сети остаётся.",
            Values = new[] { new TweakValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", "DWord", "1") },
            ApplyMode = TweakApplyMode.Reboot,
            Link = "https://learn.microsoft.com/en-us/windows/deployment/do/waas-delivery-optimization-reference#download-mode",
        },
        new()
        {
            Id = "EDGE_NO_BACKGROUND", Category = TweakCategory.Gaming, Recommended = true,
            Title = "Edge не работает в фоне",
            Description = "Microsoft Edge перестаёт держать процессы в памяти после закрытия окна и не запускается заранее при входе в Windows. " +
                          "Освобождает сотни мегабайт памяти. Сам браузер работает как раньше, первое открытие — на секунду дольше.",
            Values = new[]
            {
                new TweakValue(EdgePolicy, "StartupBoostEnabled", "DWord", "0"),
                new TweakValue(EdgePolicy, "BackgroundModeEnabled", "DWord", "0"),
            },
            ApplyMode = TweakApplyMode.Reboot,
            Link = "https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies#startupboostenabled",
        },
        new()
        {
            Id = "NO_WIDGETS", Category = TweakCategory.Gaming, MinBuild = 22000, Evidence = Opti.Core.Fixes.EvidenceLevel.Varies,
            Title = "Отключить мини-приложения (виджеты)",
            Description = "Убирает панель новостей и погоды вместе с её фоновыми процессами. Полезно на компьютерах с 8 ГБ памяти и меньше.",
            Values = new[] { new TweakValue(@"HKLM\SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", "DWord", "0") },
            ApplyMode = TweakApplyMode.Reboot,
            Link = "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-newsandinterests",
        },
        new()
        {
            Id = "NO_TRANSPARENCY", Category = TweakCategory.Gaming, Evidence = Opti.Core.Fixes.EvidenceLevel.Varies,
            Title = "Без эффектов прозрачности",
            Description = "Панель задач, «Пуск» и окна становятся непрозрачными. На встроенной графике и старых видеокартах снимает часть нагрузки с видеочипа, " +
                          "когда игра идёт в окне или поверх неё что-то открыто.",
            Values = new[] { new TweakValue(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", "DWord", "0") },
        },
        new()
        {
            Id = "NO_BACKGROUND_APPS", Category = TweakCategory.Gaming, Evidence = Opti.Core.Fixes.EvidenceLevel.Varies,
            Title = "Запретить фоновую работу приложений из Microsoft Store",
            Description = "Приложения из Магазина (почта, погода, «Фото» и т. п.) перестают просыпаться и работать в фоне. Освобождает немного памяти и процессора; на ПК с 8 ГБ памяти и меньше заметнее. " +
                          "Уведомления и обновления плиток этих приложений могут приходить с задержкой.",
            Values = new[] { new TweakValue(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", "DWord", "2") },
            ApplyMode = TweakApplyMode.Reboot,
            Link = "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy#letappsruninbackground",
        },
        new()
        {
            Id = "MMCSS_GAMES", Category = TweakCategory.Gaming, Evidence = Opti.Core.Fixes.EvidenceLevel.Hypothesis,
            Title = "Приоритет игр в планировщике мультимедиа (MMCSS)",
            Description = "Поднимает приоритет задачи «Games» в службе планирования мультимедиа: приоритет 6, планирование «Высокое», приоритет диска «Высокий». " +
                          "Это значения, которые советуют в гайдах; измерений, что они дают FPS, нет — эффект, если есть, в стабильности кадров. Выключатель вернёт стандартные значения Windows.",
            Values = new[]
            {
                new TweakValue(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Priority", "DWord", "6"),
                new TweakValue(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Scheduling Category", "String", "High"),
                new TweakValue(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "SFIO Priority", "String", "High"),
            },
            ApplyMode = TweakApplyMode.Reboot,
        },
        new()
        {
            Id = "NO_NETWORK_THROTTLING", Category = TweakCategory.Gaming, Evidence = Opti.Core.Fixes.EvidenceLevel.Hypothesis,
            Title = "Снять ограничение сети для мультимедиа (NetworkThrottlingIndex)",
            Description = "Windows по умолчанию ограничивает сетевой трафик во время воспроизведения мультимедиа. Опция снимает это ограничение. Измерений выигрыша для игр нет; " +
                          "если проблема в Wi-Fi, роутере или провайдере, она ничего не изменит — сначала смотрите результаты проверки сети.",
            Values = new[] { new TweakValue(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", "DWord", "4294967295") },
            ApplyMode = TweakApplyMode.Reboot,
        },
        new()
        {
            Id = "NO_GAMEBAR_BUTTON", Category = TweakCategory.Gaming,
            Title = "Кнопка Xbox на геймпаде не открывает Game Bar",
            Description = "Случайное нажатие центральной кнопки геймпада больше не перекрывает игру панелью Xbox Game Bar. Сочетание Win+G продолжает работать.",
            Values = new[] { new TweakValue(@"HKCU\Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", "DWord", "0") },
        },
    };

    /// <summary>Сначала игровые твики, затем твики удобства. Объявлено после обоих массивов: статические поля инициализируются по порядку.</summary>
    public static IReadOnlyList<Tweak> All { get; } = GamingTweaks.Concat(BaseTweaks).ToArray();

    /// <summary>
    /// Твики, которые включает «Максимум оптимизации»: только про память, процессор и сеть, и только те, у кого эффект известен.
    /// «Гипотезы» (MMCSS, сетевое ограничение мультимедиа) и твики удобства (контекстное меню, значки слева…) сюда не входят.
    /// </summary>
    public static readonly IReadOnlyList<string> MaxOptimizeIds = new[]
        { "NO_UPDATE_SHARING", "EDGE_NO_BACKGROUND", "NO_BACKGROUND_APPS", "NO_WIDGETS", "NO_TRANSPARENCY", "NO_GAMEBAR_BUTTON", "FAST_MENUS", "INSTANT_MINIMIZE" };

    public static Tweak? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}
