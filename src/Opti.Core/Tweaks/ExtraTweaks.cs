namespace Opti.Core.Tweaks;

/// <summary>
/// Дополнительные твики: приватность и реклама, Проводник, панель задач, ввод, диагностика.
/// Все — обычные переключатели Windows, записанные в реестр; на FPS не влияют и в «Максимум» не входят.
/// </summary>
public static class ExtraTweaks
{
    private const string Cv = @"HKCU\Software\Microsoft\Windows\CurrentVersion";
    private const string Adv = Cv + @"\Explorer\Advanced";
    private const string Cdm = Cv + @"\ContentDeliveryManager";
    private const string Personalize = Cv + @"\Themes\Personalize";

    private static Tweak T(string id, TweakCategory cat, string title, string description, TweakApplyMode mode, int minBuild, bool recommended, params TweakValue[] values) => new()
    {
        Id = id, Category = cat, Title = title, Description = description, Values = values, ApplyMode = mode, MinBuild = minBuild, Recommended = recommended,
    };
    private static TweakValue D(string key, string name, int on) => new(key, name, "DWord", on.ToString());
    private static TweakValue S(string key, string name, string on) => new(key, name, "String", on);

    private const TweakApplyMode Now = TweakApplyMode.Immediate, Explorer = TweakApplyMode.RestartExplorer, SignOut = TweakApplyMode.SignOut, Reboot = TweakApplyMode.Reboot;

    public static readonly Tweak[] All =
    {
        // ---------- Приватность и реклама ----------
        T("NO_AD_ID", TweakCategory.Privacy, "Без рекламного идентификатора",
            "Приложения не получают ваш рекламный идентификатор для подбора объявлений. То же, что переключатель в «Параметры → Конфиденциальность → Общие».", Now, 0, true,
            D(Cv + @"\AdvertisingInfo", "Enabled", 0)),
        T("NO_LOCKSCREEN_ADS", TweakCategory.Privacy, "Экран блокировки без советов и рекламы",
            "Убирает «интересные факты, советы и подсказки» с экрана блокировки. Картинка остаётся.", Now, 0, true,
            D(Cdm, "RotatingLockScreenOverlayEnabled", 0), D(Cdm, "SubscribedContent-338387Enabled", 0)),
        T("NO_SETTINGS_ADS", TweakCategory.Privacy, "«Параметры» без рекомендуемого содержимого",
            "Убирает предложения приложений и сервисов внутри окна «Параметры».", Now, 0, true,
            D(Cdm, "SubscribedContent-338393Enabled", 0), D(Cdm, "SubscribedContent-353694Enabled", 0), D(Cdm, "SubscribedContent-353696Enabled", 0)),
        T("NO_FINISH_SETUP_NAG", TweakCategory.Privacy, "Без окна «Давайте завершим настройку»",
            "Windows перестаёт после обновлений предлагать на весь экран OneDrive, Microsoft 365 и вход в учётную запись.", Now, 0, true,
            D(Cv + @"\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0)),
        T("NO_TAILORED_EXPERIENCES", TweakCategory.Privacy, "Без «персонализированных предложений»",
            "Microsoft не использует диагностические данные для подбора советов и рекламы.", Now, 0, false,
            D(Cv + @"\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0)),
        T("NO_FEEDBACK_PROMPTS", TweakCategory.Privacy, "Windows не просит оставить отзыв",
            "Отключает всплывающие просьбы оценить Windows.", Now, 0, false,
            D(@"HKCU\Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0)),
        T("NO_APP_LAUNCH_TRACKING", TweakCategory.Privacy, "Не отслеживать запуски приложений",
            "Windows не ведёт статистику запусков. Побочный эффект: в «Пуске» пропадёт список «Часто используемые».", Now, 0, false,
            D(Adv, "Start_TrackProgs", 0)),
        T("NO_EXPLORER_ADS", TweakCategory.Privacy, "Проводник без уведомлений поставщика синхронизации",
            "Убирает рекламные плашки OneDrive и Microsoft 365 в окнах Проводника.", Now, 0, true,
            D(Adv, "ShowSyncProviderNotifications", 0)),
        T("NO_START_RECOMMENDATIONS", TweakCategory.Privacy, "«Пуск» без рекомендаций приложений и советов",
            "В разделе «Рекомендуем» остаются только ваши недавние файлы — без предложений из Microsoft Store.", Now, 22621, true,
            D(Adv, "Start_IrisRecommendations", 0)),
        T("NO_SEARCH_HIGHLIGHTS", TweakCategory.Privacy, "Поиск без «главных событий»",
            "Убирает картинки и новости дня из поля и окна поиска.", Now, 0, true,
            D(Cv + @"\SearchSettings", "IsDynamicSearchBoxEnabled", 0)),
        T("NO_LANGUAGE_LIST_ACCESS", TweakCategory.Privacy, "Сайты не видят список языков системы",
            "Браузерам не передаётся список установленных языков — один из признаков, по которым вас узнают сайты.", Now, 0, false,
            D(@"HKCU\Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1)),
        T("NO_ACTIVITY_HISTORY", TweakCategory.Privacy, "Не вести журнал действий",
            "Windows не сохраняет историю открытых приложений и файлов для временной шкалы.", Now, 0, false,
            D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0)),

        // ---------- Проводник ----------
        T("SHOW_HIDDEN_FILES", TweakCategory.Explorer, "Показывать скрытые файлы и папки",
            "Нужны, чтобы добраться до AppData, где лежат сохранения и настройки игр.", Now, 0, false,
            D(Adv, "Hidden", 1)),
        T("EXPLORER_COMPACT", TweakCategory.Explorer, "Компактный вид списков",
            "Уменьшает отступы между файлами — на экран помещается больше строк.", Now, 22000, false,
            D(Adv, "UseCompactMode", 1)),
        T("NO_RECENT_IN_QUICK_ACCESS", TweakCategory.Explorer, "Без недавних файлов на главной Проводника",
            "Проводник не показывает недавно открытые файлы и часто используемые папки.", Now, 0, false,
            D(Cv + @"\Explorer", "ShowRecent", 0), D(Cv + @"\Explorer", "ShowFrequent", 0)),
        T("EXPLORER_SEPARATE_PROCESS", TweakCategory.Explorer, "Окна папок в отдельном процессе",
            "Зависшая папка не утянет за собой панель задач и рабочий стол. Цена — немного больше памяти.", Explorer, 0, false,
            D(Adv, "SeparateProcess", 1)),
        T("EXPLORER_FULL_PATH", TweakCategory.Explorer, "Полный путь в заголовке окна",
            "В заголовке и на вкладке виден весь путь к папке, а не только её имя.", Now, 0, false,
            D(Cv + @"\Explorer\CabinetState", "FullPath", 1)),
        T("NO_AUTOPLAY", TweakCategory.Explorer, "Без автозапуска флешек и дисков",
            "Windows не открывает окно и ничего не запускает при подключении носителя. Заодно чуть безопаснее.", Now, 0, false,
            D(Cv + @"\Explorer\AutoplayHandlers", "DisableAutoplay", 1)),
        T("LONG_PATHS", TweakCategory.System, "Длинные пути к файлам (больше 260 символов)",
            "Снимает старое ограничение длины пути. Помогает с модами и глубоко вложенными папками; программам, которые его не поддерживают, не мешает.", Reboot, 14393, false,
            D(@"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 1)),

        // ---------- Пуск и панель задач ----------
        T("NO_TASKVIEW_BUTTON", TweakCategory.StartAndTaskbar, "Убрать кнопку «Представление задач»",
            "Кнопка исчезает с панели задач; Win+Tab продолжает работать.", Now, 0, false,
            D(Adv, "ShowTaskViewButton", 0)),
        T("NO_SEARCH_BOX", TweakCategory.StartAndTaskbar, "Убрать поле поиска с панели задач",
            "Освобождает место на панели. Поиск остаётся: нажмите Win и начните печатать.", Now, 0, false,
            D(Cv + @"\Search", "SearchboxTaskbarMode", 0)),
        T("NO_COPILOT_BUTTON", TweakCategory.StartAndTaskbar, "Убрать кнопку Copilot",
            "Кнопка исчезает с панели задач.", Now, 22621, false,
            D(Adv, "ShowCopilotButton", 0)),
        T("NO_CHAT_BUTTON", TweakCategory.StartAndTaskbar, "Убрать кнопку «Чат»",
            "Убирает значок чата Teams с панели задач.", Now, 22000, false,
            D(Adv, "TaskbarMn", 0)),
        T("CLOCK_SECONDS", TweakCategory.StartAndTaskbar, "Секунды в часах на панели задач",
            "Часы показывают секунды. По данным Microsoft, это немного увеличивает расход энергии на ноутбуке.", Now, 22621, false,
            D(Adv, "ShowSecondsInSystemClock", 1)),
        T("TASKBAR_NO_COMBINE", TweakCategory.StartAndTaskbar, "Не объединять кнопки окон на панели задач",
            "Каждое окно — отдельная кнопка с подписью, как в Windows 10.", Now, 22631, false,
            D(Adv, "TaskbarGlomLevel", 2)),
        T("ALT_TAB_WINDOWS_ONLY", TweakCategory.StartAndTaskbar, "Alt+Tab только по окнам, без вкладок Edge",
            "Вкладки браузера не засоряют переключение между программами и игрой.", Now, 0, true,
            D(Adv, "MultiTaskingAltTabFilter", 3)),
        T("NO_SNAP_FLYOUT", TweakCategory.StartAndTaskbar, "Без макетов прикрепления у кнопки «Развернуть»",
            "Панель с вариантами раскладки не всплывает при наведении на кнопку окна.", Now, 22000, false,
            D(Adv, "EnableSnapAssistFlyout", 0)),
        T("NO_SNAP_SUGGESTIONS", TweakCategory.StartAndTaskbar, "Не предлагать, что прикрепить рядом",
            "После прикрепления окна к краю Windows не показывает список остальных окон.", Now, 0, false,
            D(Adv, "SnapAssist", 0)),
        T("NO_SHAKE_MINIMIZE", TweakCategory.StartAndTaskbar, "Не сворачивать окна при «встряхивании»",
            "Случайное резкое движение окна мышью больше не сворачивает все остальные.", Now, 0, false,
            D(Adv, "DisallowShaking", 1)),
        T("DARK_THEME", TweakCategory.StartAndTaskbar, "Тёмная тема Windows и приложений",
            "Тёмное оформление системы и программ, которые его поддерживают.", Now, 0, false,
            D(Personalize, "AppsUseLightTheme", 0), D(Personalize, "SystemUsesLightTheme", 0)),

        // ---------- Ввод и удобство ----------
        T("NUMLOCK_ON", TweakCategory.Responsiveness, "Num Lock включён при входе",
            "Цифровой блок клавиатуры работает сразу после входа в систему.", SignOut, 0, false,
            S(@"HKCU\Control Panel\Keyboard", "InitialKeyboardIndicators", "2")),
        T("FAST_KEY_REPEAT", TweakCategory.Responsiveness, "Самый быстрый автоповтор клавиш",
            "Минимальная задержка перед повтором и максимальная скорость повтора при удержании клавиши — те же ползунки, что в «Клавиатура» панели управления.", SignOut, 0, false,
            S(@"HKCU\Control Panel\Keyboard", "KeyboardDelay", "0"), S(@"HKCU\Control Panel\Keyboard", "KeyboardSpeed", "31")),
        T("CLIPBOARD_HISTORY", TweakCategory.Responsiveness, "Журнал буфера обмена (Win+V)",
            "Windows помнит последние скопированные фрагменты; вставка нужного — по Win+V.", Now, 17763, false,
            D(@"HKCU\Software\Microsoft\Clipboard", "EnableClipboardHistory", 1)),
        T("PRINTSCREEN_SNIPPING", TweakCategory.Responsiveness, "Print Screen открывает «Ножницы»",
            "Клавиша Print Screen сразу даёт выделить область экрана.", SignOut, 17763, false,
            D(@"HKCU\Control Panel\Keyboard", "PrintScreenKeyForSnippingEnabled", 1)),

        // ---------- Игры ----------
        T("NO_GAMEBAR_TIPS", TweakCategory.Gaming, "Game Bar без подсказок при запуске игры",
            "Не появляется всплывающее «Нажмите Win+G…» поверх игры.", Now, 0, true,
            D(@"HKCU\Software\Microsoft\GameBar", "ShowStartupPanel", 0)),
        new()
        {
            Id = "NO_STORE_AUTO_UPDATE", Category = TweakCategory.Gaming, Evidence = Opti.Core.Fixes.EvidenceLevel.Varies,
            Title = "Microsoft Store не обновляет приложения сам",
            Description = "Приложения из Store не скачивают обновления в фоне, пока вы играете. Обновлять придётся вручную в Store — не забывайте, иначе останетесь без исправлений.",
            Values = new[] { D(@"HKLM\SOFTWARE\Policies\Microsoft\WindowsStore", "AutoDownload", 2) },
        },

        // ---------- Система и диагностика ----------
        T("VERBOSE_STATUS", TweakCategory.System, "Подробные сообщения при включении и выключении",
            "Вместо «Подождите…» Windows пишет, что именно делает. Помогает понять, на чём зависает загрузка или выключение.", Now, 0, false,
            D(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "VerboseStatus", 1)),
        T("BSOD_NO_AUTO_REBOOT", TweakCategory.System, "Синий экран не перезагружает ПК сам",
            "При сбое компьютер остаётся на экране ошибки — можно прочитать или сфотографировать код остановки.", Now, 0, false,
            D(@"HKLM\SYSTEM\CurrentControlSet\Control\CrashControl", "AutoReboot", 0)),
        T("NO_LOGON_BLUR", TweakCategory.System, "Экран входа без размытия фона",
            "Фон экрана входа показывается чётким; на слабой встроенной графике вход становится чуть плавнее.", Now, 18362, false,
            D(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "DisableAcrylicBackgroundOnLogon", 1)),
    };
}
