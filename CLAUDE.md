# Opti — карта репозитория

Портативная программа для Windows 10/11 (один `Opti.exe`): диагностика причин лагов → исправления с откатом → честный замер «до / после». Бывший GameTune. Версия — `Directory.Build.props` (`<Version>`), сейчас 1.8.0-beta. Язык интерфейса, комментариев, коммитов и документации — русский.

## Команды

```bash
dotnet test                      # 322 теста, xUnit, ~20 с; работает в Linux
dotnet build src/Opti.Cli        # CLI
dotnet build src/Opti.App        # WPF; в Linux собирается (EnableWindowsTargeting), но не запускается
build/publish.sh [версия]        # win-x64 single-file → artifacts/Opti.exe, opti-cli.exe, SHA256SUMS.txt
DEV=1 build/publish.sh           # + artifacts/Opti.Dev.exe: ведёт dev.zip рядом с exe (Workflow/Dev/DevBundle, App/Services/DevExport, #if DEV)
```

win-x64 публикация из Linux работает (~1 мин, exe ~69 МБ). Dev-сборка отличается только константой DEV (`-p:DevBuild=true`, отдельный obj/dev); в Opti.exe кода dev.zip нет. Имя процесса dev-сборки `Opti.Dev` добавлено в списки игнорируемых процессов.

Нужен .NET 10 SDK. В облачном контейнере его нет по умолчанию: `apt-get install -y dotnet-sdk-10.0` (dot.net/builds.dotnet.microsoft.com закрыты прокси, apt работает).

В Linux нельзя проверить: коллекторы (WMI/реестр), Windows-бэкенды, ETW, D3D11-бенч, сам интерфейс. Всё это проверяется только тестами на профилях и CI на `windows-latest`.

## Слои (зависимости сверху вниз)

| Проект | Что внутри |
|---|---|
| `Opti.Core` | Без зависимостей. `SystemProfile` (модель ПК), `Finding`/`IDetector`, справочники (`Os/WindowsLifecycle`, `Os/BootLimits`, `Games/GameProfiles`, `Games/GameStorage` (где лежат игры: тип диска и место), `Hardware/GpuSupport`, `Network/NetworkApps`, `Network/PingProbe`, `Drivers/`, `Registry/`, `Tweaks/`, `Software/`), `IFix`/`FixIds`/`ChangeContext`/`ChangeRecord`/`StateValue`, `IStateBackend`/`IChangeJournal`, `StrategyEngine`, `UserGoal`, `Sources` (ссылки на первоисточники) |
| `Opti.Native` | P/Invoke (экран, DXGI, HAGS, питание), `ProcessRunner`, `ToolParsers` (разбор вывода bcdedit/powercfg/nvidia-smi и т. п.) |
| `Opti.Analysis` | `Collectors/` — 19 коллекторов заполняют `SystemProfile` (только Windows; `NetworkQualityCollector` — быстрый пинг до роутера и интернета, отключается настройкой); `Detectors/` — 65 детекторов, чистые функции от профиля; `SystemAnalyzer`, `DetectorCatalog.All()` |
| `Opti.Fixes` | `Catalog/` — 26 исправлений (`FixBase`); `Backends/` — Windows-бэкенды + `InMemoryBackend`/`ProfileSeeder` для тестов; `Journal/` — SQLite и in-memory; `ApplyService`, `RollbackService`, точка восстановления |
| `Opti.Measure` | `Stats/` — `FrameMetrics`, блочный бутстреп, `Comparison` (вердикт лучше/шум/хуже); `Capture/` — ETW (кадры, DPC/ISR), PresentMon CSV, сеть, таймер |
| `Opti.Bench` | Синтетический тест D3D11 (Vortice); встроен в `Opti.exe`, запускается ключом `--bench` |
| `Opti.Workflow` | `TuneWorkflow` (сессия: анализ → план → до → применение → перезагрузка/resume → после → сравнение → откат; A/B), `Sessions/`, `Measuring/` (раннеры), `Game/` («Перед игрой» и приглушение фона через шов `IProcessControl`), `Cleanup/`, `Reports/` (HTML + SVG), `Settings`, `AppInfo`/`UpdateChecker` |
| `Opti.ViewModels` | MVVM (CommunityToolkit.Mvvm), `IAppServices` — шов для тестов без Windows |
| `Opti.App` | WPF Fluent, `net10.0-windows`. Views без кода — только XAML; `WindowsAppServices` — реальная реализация `IAppServices` |
| `Opti.Cli` | `opti-cli`; все команды в `Commands.cs` (run, resume, analyze, plan, measure, apply, ab, compare, report, rollback, history, latency, net, version, doctor) |
| `tests/Opti.Tests` | Тесты + `Profiles/*.json` — 8 эталонных ПК (laptop-optimus, desktop-rtx, laptop-igpu, weak-pc, revios, fresh-install, old-gaming-pc, gta-majestic-pc); `TestProfiles.Now` — фиксированное время |

## Инварианты, которые нельзя ломать

- **Журнал до изменения.** Любая запись в систему идёт через `ChangeContext.Set`: чтение текущего → запись в журнал → запись в бэкенд. Не сохранилось — не меняем (`ChangeAbortedException`). Исправления не обращаются к реестру/BCD напрямую.
- **Оригинал фиксируется один раз** (`GetActiveOriginal`): повторное применение не затирает исходное значение.
- **Детекторы не бросают исключений** и работают на неполном профиле (`DetectorBase` ловит и пишет в `CollectorErrors`). Коллектор, упавший целиком, не роняет анализ.
- **Каждое исправление привязано к находке.** `StrategyEngine` строит план только из `Finding.FixIds`; рискованные, A/B, `NeedsUserChoice`, BCD при BitLocker, таймеры на модифицированных сборках — не отмечены по умолчанию.
- **«Лучше/хуже» — только при непересекающихся 95 % ДИ и разнице больше межпрогонного разброса.** Троттлинг и разные условия делают сравнение ненадёжным.
- **Рекомендации со ссылкой на источник** из `Sources`. Никаких «твиков из интернета»: чистки реестра, массового отключения служб, сетевых твиков без доказанного эффекта.
- Очистка: только старые файлы, без перехода по ссылкам, занятые пропускаются.

## Как добавить

- **Детектор:** класс от `DetectorBase` в `Analysis/Detectors/*` → зарегистрировать в `DetectorCatalog.All()` → при необходимости поле в `SystemProfile` + коллектор → тест на профилях (`DetectorTests`/`SetupDetectorTests`). Счётчик детекторов в README и здесь.
- **Исправление:** константа в `FixIds` → класс от `FixBase` в `Fixes/Catalog/*` → регистрация в `FixCatalog.All()` (`FixBase.cs`) → находка с этим `FixId` → при необходимости `ProfileSeeder` (начальное состояние in-memory бэкенда) → `RollbackTests`. Счётчик исправлений в README и здесь.
- **Твик:** запись в `TweakCatalog` (только значения реестра; игровые — в `GamingTweaks`). Раздел «Твики» и откат подхватят сами.
- **Экран интерфейса:** ViewModel в `Opti.ViewModels` (+ тест в `ViewModelTests`), XAML в `Opti.App/Views`, навигация в `MainViewModel`.

## Выпуск

- CI (`ci.yml`): push в `main`/`opti-prototype` и PR → `dotnet test` + сборка App и Cli на Windows.
- Релиз (`release.yml`) собирает и `Opti.Dev.exe` (`DEV=1`), он публикуется рядом с `Opti.exe`. Пуш в ветку `release` или тег `v*` → тесты → `build/publish.ps1` → GitHub Release; описание берётся из `docs/release-notes/v<версия>.md` (иначе `CHANGELOG.md`).
- При смене версии: `Directory.Build.props`, `CHANGELOG.md`, `docs/release-notes/`, статус в README.

## Мелочи, о которые можно споткнуться

- `origin` — `alexkuz2014/Opti`, но в коде и README зашит `allora-dev/optim` (`AppInfo.Repository`, `RepositoryUrl`, ссылка на релизы): проверка обновлений смотрит туда.
- Остатки старого имени: `Opti.Core/Json/GameTuneJson.cs`, миграция данных GameTune 1.0.0-beta — оставлены намеренно ради отката старых изменений.
- Данные программы: `C:\ProgramData\Opti\` (`journal.db`, сессии, отчёты) — пути в `OptiPaths` (`Fixes/Backends/WindowsBackends.cs`).
- Страница «Игра»: `GamePrep` закрывает выбранные программы, `BackgroundThrottler` понижает приоритет браузерам и лаунчерам на время игры (таймер 5 с в `App.xaml.cs`, возврат при выходе). Список защищённых процессов `GamePrep.Protected` — не сокращать; Discord и OBS из приглушения исключены намеренно. «Очистку ОЗУ» не добавлять: миф.
- Метки надёжности (`EvidenceLevel`: Solid / Varies / Hypothesis) у исправлений и твиков: новый твик без измерений — только Hypothesis и выключен по умолчанию.
- Из PC-Optimization-Hub/PC-Tuning сознательно не берём: xHCI IMOD через RWEverything (требует отключить защиту от уязвимых драйверов), разнесение прерываний по ядрам, режим MSI вручную, отключение виртуализации в BIOS (ломает античиты), dual-boot. Подсказки по BIOS/USB-топологии — только текстом.
- Загрузка игр: `GameStorage` + детекторы `GAME_LOAD_TIPS`, `DISK_BUSY`, `GAME_DISK_FULL`. Проверено поиском: HDD→SSD заметно (20 % … 2–3×, зависит от игры), SATA→NVMe — секунды, кроме DirectStorage; `-StraightIntoFreemode` и `-novid` для CS2 не подтверждены — не советовать; сторонний фикс загрузки GTA Online правит файлы игры и грозит баном — не советовать. Исключения антивируса — только текстом.
- «Максимум оптимизации» (`HomeViewModel.MaximumAsync`): анализ с целью LowLatency → автоцикл (`TuneSession.Maximum`) → после исправлений `MainViewModel.RunMaximumExtrasAsync` → `MaxOptimizer` (твики `TweakCatalog.MaxOptimizeIds` в сессии оптимизации, очистка `DefaultSelected` целей, драйверы через `WindowsUpdateDriverUpdater` — COM Windows Update Agent, видеодрайверы и EULA пропускаются) → автоперезагрузка с отсчётом (`ApplyViewModel.RunAutoRebootAsync`). Не добавлять в «Максимум» гипотезы, A/B и рискованное; драйверы с сайтов производителей Opti не качает.
- Главная: кнопка «Оптимизировать» (`HomeViewModel.OptimizeAsync`) = анализ + автоцикл с рекомендованными исправлениями и быстрым тестом 2×20 с, без страницы настройки. На главной три пресета (`GoalOption.Presets`), все пять целей — в настройках (`GoalOption.All`).
- Синтетический тест в интерфейсе подбирает нагрузку (`SyntheticRunner.AutoCalibrate`, цель ~80 FPS) и хранит её в `TuneSession.BenchGpuLoad`; CLI нагрузку не подбирает, чтобы отдельные запуски `measure` были сравнимы.
- Найдено при первом запуске на Windows (исправлено, не повторять): `AllowComputerToTurnOffDevice` — свойство объекта `Get-NetAdapterPowerManagement`, а не параметр `Set-…`; stderr PowerShell приходит в CLIXML (`ProcessRunner.CleanPowerShellError`); системный шаблон `ListBox` в выключенном состоянии рисует белый фон — у `ChoiceList` свой шаблон; файлы (`ChangeType.File`) в проверке сброса не участвуют.
- Контроль сброса: `DriftService` (Fixes/Services) сверяет последние действующие записи журнала с системой; восстановление только через `ChangeContext`. Показывается на главной и в `opti-cli verify`.
- Из внешней спецификации сознательно не делали: USB-топологию (CfgMgr32, высокий риск ошибок вслепую), проверку MTU (для PPPoE 1492 ложные срабатывания), совет менять размер кэша шейдеров (доказательства слабые), запись `EnhancedPowerManagementEnabled` в ветки драйверов USB (чужие ключи драйвера), коррелятор причин фризов в ETW и тест опроса мыши (большие задачи, нужен отдельный этап).
- Сознательно не делаем (антиочит и безопасность): приоритет процесса игры через IFEO, исключения Defender, отключение Defender/Update, правка файлов игры.
- Советы по играм (`GameProfiles`) — только про настройки в самой игре и системную часть. Файлы игры (settings.xml и т. п.) Opti не меняет: решение владельца, пользователям это может не понравиться. Добавляя игру, ссылаться на обзоры, цифры без источника не писать.
- Эвристики про «твики из роликов» проверены поиском: `msconfig → число процессоров` не ускоряет (Windows использует все ядра), MMCSS/Win32PrioritySeparation/NetworkThrottling дают нестабильный эффект, Ultimate Performance — единицы процентов, VBS/HVCI — около 5 % (до 10 %), HAGS — по-разному. Крупные выигрыши — настройки графики в игре и исправление неверной конфигурации.
- Ссылки `Gta5Guide`, `ReflexTest`, `HvciBenchmark` взяты из выдачи поиска, страницы из контейнера не открывались.
- Сроки поддержки Windows (`WindowsLifecycle`) и списки снятых с поддержки видеокарт (`GpuSupport`) — данные с датами: при появлении новых выпусков их нужно дополнять. Неизвестная новая сборка Windows считается поддерживаемой.
- Потери пакетов: порог выше 5 %, потому что одна потеря из 20 пингов — случайность. Роутер, не отвечающий на пинг, — это «нет данных», а не 100 % потерь.
- Ссылки в `Sources` из облачного контейнера не проверить (microsoft.com закрыт прокси) — новые ссылки проверять вручную.
- XAML: сборка не проверяет ключи `{StaticResource}` и имена привязок — ошибка всплывёт только при запуске на Windows. После правок стилей проверять, что каждый используемый ключ определён в `App.xaml`. `Expander` и карточки-кнопки — собственные шаблоны в `App.xaml` (неявный стиль `Expander` действует на все страницы).
- `LangVersion` 12, `Nullable` включён, предупреждения не считаются ошибками.
- `*.db`, `artifacts/`, `bin/`, `obj/` — в `.gitignore`.
