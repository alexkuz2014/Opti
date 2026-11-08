using System.Text;
using GameTune.Analysis;
using GameTune.Cli;
using GameTune.Core.Fixes;
using GameTune.Core.Json;
using GameTune.Core.Profile;
using GameTune.Core.Strategy;
using GameTune.Fixes.Backends;
using GameTune.Fixes.Catalog;
using GameTune.Fixes.Journal;
using GameTune.Fixes.Services;
using GameTune.Measure.Capture;
using GameTune.Measure.Stats;

Console.OutputEncoding = Encoding.UTF8;
var args1 = new Args(args);

try
{
    return args1.Command switch
    {
        "analyze" => Commands.Analyze(args1),
        "plan" => Commands.Plan(args1),
        "apply" => Commands.Apply(args1),
        "rollback" => Commands.Rollback(args1),
        "history" => Commands.History(args1),
        "measure" => await Commands.Measure(args1),
        "compare" => Commands.Compare(args1),
        "latency" => Commands.Latency(args1),
        "net" => await Commands.Net(args1),
        _ => Commands.Help(),
    };
}
catch (Exception ex) when (ex is PlatformNotSupportedException or UnauthorizedAccessException or FileNotFoundException or ArgumentException)
{
    Output.Color(ConsoleColor.Red, "Ошибка: " + ex.Message + "\n");
    return 2;
}

namespace GameTune.Cli
{
    internal static class Commands
    {
        public static int Help()
        {
            Console.WriteLine("""
                GameTune — диагностика лагов, точечные исправления и честный замер «до / после».

                Команды:
                  analyze  [--out profile.json] [--dpc 30] [--net]       Анализ системы и отчёт о проблемах
                  plan     --goal <цель> [--profile profile.json]         План исправлений под цель
                  apply    --goal <цель> [--fix ID,ID] [--option ID:key=value] [--yes]
                                                                           Применить отмеченные исправления (журнал + точка восстановления)
                  rollback --fix ID | --session ID | --all                Откат
                  history                                                  Сессии и журнал изменений
                  measure  --out before.json [--runs 3] [--duration 60] [--warmup 10] [--process game.exe]
                                                                           Замер кадров через ETW
                  compare  before.json after.json [--csv out.csv]          Честное сравнение замеров
                  latency  [--seconds 60]                                  Тест задержки системы: DPC/ISR + точность таймера
                  net      [--target 1.1.1.1] [--no-load]                  Пинг, джиттер, bufferbloat

                Цели: latency (мин. задержка), smooth (плавность), fps (макс. FPS), stream (стрим/запись), quiet (тихо и холодно).
                """);
            return 0;
        }

        private static UserGoal ParseGoal(string? s) => (s ?? "smooth").ToLowerInvariant() switch
        {
            "latency" => UserGoal.LowLatency,
            "smooth" => UserGoal.Smoothness,
            "fps" => UserGoal.MaxFps,
            "stream" => UserGoal.Streaming,
            "quiet" => UserGoal.QuietCool,
            var x => throw new ArgumentException($"Неизвестная цель «{x}». Варианты: latency, smooth, fps, stream, quiet."),
        };

        private static AnalysisResult LoadOrCollect(Args a)
        {
            if (a.Get("profile") is { } path)
            {
                var profile = GameTuneJson.Deserialize<SystemProfile>(File.ReadAllText(path));
                return SystemAnalyzer.FromProfile(profile);
            }
            Console.Write("Анализ системы: ");
            var r = SystemAnalyzer.Collect(progress: name => Console.Write(name + "… "));
            Console.WriteLine("готово.");
            return r;
        }

        public static int Analyze(Args a)
        {
            var result = LoadOrCollect(a);
            if (a.Get("dpc") is { } dpc && OperatingSystem.IsWindows())
            {
                Console.WriteLine($"Тест DPC/ISR {dpc} с — не нагружайте компьютер…");
                result.Profile.Dpc = EtwDpcCapture.Run(TimeSpan.FromSeconds(int.Parse(dpc)));
            }
            if (a.Has("net"))
            {
                Console.WriteLine("Тест сети (~20 с)…");
                result.Profile.NetQuality = NetworkTest.RunAsync().GetAwaiter().GetResult();
            }
            if (a.Has("dpc") || a.Has("net")) result = SystemAnalyzer.FromProfile(result.Profile);

            PrintProfileSummary(result.Profile);
            Output.PrintFindings(result.Findings);
            if (result.Profile.CollectorErrors.Count > 0)
            {
                Output.Header("Не удалось собрать");
                foreach (var e in result.Profile.CollectorErrors) Output.Color(ConsoleColor.DarkGray, "  " + e + "\n");
            }

            var outPath = a.Get("out") ?? Path.Combine(GameTunePaths.ReportsDir, $"analysis-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            File.WriteAllText(outPath, GameTuneJson.Serialize(new { profile = result.Profile, findings = result.Findings, timings = result.Timings }));
            Console.WriteLine($"\nОтчёт сохранён: {outPath}");
            return 0;
        }

        private static void PrintProfileSummary(SystemProfile p)
        {
            Output.Header("Профиль ПК");
            Console.WriteLine($"  {DeviceClassification.From(p).Describe()}");
            Console.WriteLine($"  {p.Device.Manufacturer} {p.Device.Model}");
            Console.WriteLine($"  {p.Os.ProductName} {p.Os.DisplayVersion} (сборка {p.Os.Build}.{p.Os.Ubr}){(p.Os.ModifiedBuild is { } m ? $", модифицированная: {m}" : "")}");
            Console.WriteLine($"  CPU: {p.Cpu.Name} — {p.Cpu.Cores} ядер / {p.Cpu.Threads} потоков");
            foreach (var g in p.Gpus)
                Console.WriteLine($"  GPU: {g.Name} ({(g.IsIntegrated ? "встроенная" : "дискретная")}, драйвер {g.DriverVersion} от {g.DriverDate:yyyy-MM-dd})");
            foreach (var d in p.Displays)
                Console.WriteLine($"  Экран: {d.FriendlyName ?? d.DeviceName} {d.Width}×{d.Height} @ {d.CurrentHz} Гц (макс. {d.MaxHzAtCurrentResolution}){(d.IsInternal == true ? ", встроенный" : "")}");
            Console.WriteLine($"  ОЗУ: {p.Memory.TotalMb / 1024.0:0.#} ГБ, свободно {p.Memory.AvailablePercent:0} %, модулей {p.Memory.Modules.Count}");
            if (p.SystemDisk is { } sd) Console.WriteLine($"  Системный диск: {sd.Model} ({sd.MediaType}), свободно {sd.FreeGb:0} ГБ");
            Console.WriteLine($"  Питание: {(p.Power.OnAcPower == true ? "от сети" : p.Power.OnAcPower == false ? "от батареи" : "?")}, схема {p.Power.ActiveSchemeName ?? p.Power.ActiveSchemeGuid}");
            Console.WriteLine($"  Игр найдено: {p.Games.Count}");
        }

        private static StrategyPlan BuildPlan(AnalysisResult r, UserGoal goal) =>
            new StrategyEngine(FixCatalog.All()).Build(r.Profile, r.Findings, goal);

        public static int Plan(Args a)
        {
            var r = LoadOrCollect(a);
            var plan = BuildPlan(r, ParseGoal(a.Get("goal")));
            Output.PrintPlan(plan);

            // Точный список изменений без записи: на сохранённом профиле — эмуляция, в Windows — сухой прогон по реальной системе.
            bool emulate = true;
            IBackendRegistry backends;
            if (!a.Has("profile") && OperatingSystem.IsWindows())
            {
                emulate = false;
                backends = new DryRunBackendRegistry(WindowsBackends.Create());
            }
            else backends = ProfileSeeder.CreateEmulation(r.Profile, out _);
            var journal = new InMemoryChangeJournal();
            var preview = new ApplyService(backends, journal, restore: null)
                .Apply(r.Profile, plan.Fixes.Where(f => f.Selected).Select(f => f.Fix), plan.Goal);
            Output.Header(emulate ? "Что будет изменено (эмуляция на сохранённом профиле)" : "Что будет изменено (сухой прогон, ничего не записано)");
            foreach (var res in preview.Results)
            {
                Console.WriteLine($"  {res.FixId}: {res.Outcome}{(res.Error is null ? "" : " — " + res.Error)}");
                foreach (var c in res.Changes) Output.Color(ConsoleColor.DarkGray, $"     {c.Type} {c.Target}: {c.Before} → {c.After}\n");
            }
            return 0;
        }

        public static int Apply(Args a)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Применение исправлений возможно только в Windows.");
            var r = LoadOrCollect(a);
            var plan = BuildPlan(r, ParseGoal(a.Get("goal")));
            Output.PrintPlan(plan);

            var requested = a.Get("fix")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var chosen = plan.Fixes.Where(p => requested is null ? p.Selected : requested.Contains(p.Fix.Id)).ToList();
            if (requested is not null)
                foreach (var id in requested.Where(id => chosen.All(c => !c.Fix.Id.Equals(id, StringComparison.OrdinalIgnoreCase))))
                    Output.Color(ConsoleColor.Yellow, $"{id}: не входит в план для этого ПК и цели — пропущено.\n");
            // Спорные настройки — по одному, с отдельным замером
            var separate = chosen.Where(c => c.ApplySeparately).ToList();
            if (separate.Count > 1 || (separate.Count == 1 && chosen.Count > 1))
            {
                Output.Color(ConsoleColor.Yellow, "Спорные настройки применяются по одному с отдельным замером. Сейчас будут применены только остальные: " +
                                                  string.Join(", ", separate.Select(s => s.Fix.Id)) + " — запустите позже через --fix.\n");
                chosen = chosen.Except(separate).ToList();
            }
            if (chosen.Count == 0) { Console.WriteLine("Нечего применять."); return 0; }

            var options = ParseOptions(a);
            Console.WriteLine();
            Console.WriteLine("Будут применены: " + string.Join(", ", chosen.Select(c => c.Fix.Id)));
            if (!a.Has("yes") && !Output.Confirm("Продолжить? Перед изменениями будет создана точка восстановления.")) return 1;

            using var journal = new SqliteChangeJournal(GameTunePaths.JournalPath);
            var service = new ApplyService(WindowsBackends.Create(), journal, new WindowsRestorePointService());
            var report = service.Apply(r.Profile, chosen.Select(c => c.Fix), plan.Goal, options);

            Output.Header($"Сессия {report.SessionId}");
            if (report.RestorePoint is not null) Console.WriteLine($"Точка восстановления: {report.RestorePoint}");
            if (report.RestorePointWarning is not null) Output.Color(ConsoleColor.Yellow, report.RestorePointWarning + "\n");
            foreach (var res in report.Results)
            {
                var color = res.Outcome switch { FixOutcome.Applied => ConsoleColor.Green, FixOutcome.Failed => ConsoleColor.Red, _ => ConsoleColor.Gray };
                Output.Color(color, $"  {res.FixId}: {res.Outcome}");
                Console.WriteLine(res.Error is null ? "" : " — " + res.Error);
                foreach (var c in res.Changes) Output.Color(ConsoleColor.DarkGray, $"     {c.Type} {c.Target}: {c.Before} → {c.After}\n");
            }
            if (report.RebootRequired) Output.Color(ConsoleColor.Yellow, "\nДля части изменений нужна перезагрузка. После неё выполните замер «после».\n");
            Console.WriteLine($"Откат этой сессии: gametune rollback --session {report.SessionId}");
            return report.Results.Any(x => x.Outcome == FixOutcome.Failed) ? 3 : 0;
        }

        private static Dictionary<string, IReadOnlyDictionary<string, string>> ParseOptions(Args a)
        {
            var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in a.GetAll("option"))
            {
                // ID:key=value  (для списка значений используйте ; — например, STARTUP_DISABLE:items=Spotify;Teams)
                int c = o.IndexOf(':'), e = o.IndexOf('=');
                if (c <= 0 || e <= c) throw new ArgumentException($"Неверная опция «{o}». Формат: FIX_ID:ключ=значение");
                var fix = o[..c];
                if (!map.TryGetValue(fix, out var d)) map[fix] = d = new(StringComparer.OrdinalIgnoreCase);
                d[o[(c + 1)..e]] = o[(e + 1)..].Replace(';', '\n');
            }
            return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, string>)kv.Value, StringComparer.OrdinalIgnoreCase);
        }

        public static int Rollback(Args a)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Откат возможен только в Windows.");
            using var journal = new SqliteChangeJournal(GameTunePaths.JournalPath);
            var svc = new RollbackService(WindowsBackends.Create(), journal);
            RollbackReport rep;
            if (a.Get("fix") is { } fix) rep = svc.RollbackFix(fix, a.Get("session"));
            else if (a.Get("session") is { } s) rep = svc.RollbackSession(s);
            else if (a.Has("all"))
            {
                if (!a.Has("yes") && !Output.Confirm("Откатить ВСЕ изменения GameTune?")) return 1;
                rep = svc.RollbackAll();
            }
            else throw new ArgumentException("Укажите --fix ID, --session ID или --all");

            foreach (var r in rep.Restored) Output.Color(ConsoleColor.Green, $"  восстановлено {r.Type} {r.Target} = {r.Before}\n");
            foreach (var r in rep.SkippedSupersededBy) Output.Color(ConsoleColor.Yellow, $"  оставлено {r.Type} {r.Target}: позже изменено другим исправлением\n");
            foreach (var e in rep.Errors) Output.Color(ConsoleColor.Red, $"  ошибка: {e}\n");
            if (rep.Restored.Count + rep.SkippedSupersededBy.Count + rep.Errors.Count == 0) Console.WriteLine("Нечего откатывать.");
            return rep.Errors.Count > 0 ? 3 : 0;
        }

        public static int History(Args a)
        {
            if (!File.Exists(GameTunePaths.JournalPath)) { Console.WriteLine("Журнал пуст."); return 0; }
            using var journal = new SqliteChangeJournal(GameTunePaths.JournalPath);
            var all = journal.GetAll();
            foreach (var s in journal.GetSessions())
            {
                Output.Header($"Сессия {s.SessionId} — {s.StartedAt:yyyy-MM-dd HH:mm}, цель {s.Goal}{(s.RestorePoint is null ? "" : ", есть точка восстановления")}");
                foreach (var r in all.Where(r => r.SessionId == s.SessionId))
                    Console.WriteLine($"  {(r.RolledBack ? "↩" : "✓")} {r.FixId} {r.Type} {r.Target}: {r.Before} → {r.After}");
            }
            return 0;
        }

        public static async Task<int> Measure(Args a)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Замер кадров через ETW возможен только в Windows.");
            int runs = a.GetInt("runs", 3), duration = a.GetInt("duration", 60), warmup = a.GetInt("warmup", 10);
            var outPath = a.Get("out") ?? throw new ArgumentException("Укажите --out файл.json");
            var scenario = a.Get("scenario") ?? "игра";
            var list = new List<MeasurementRun>();
            for (int i = 1; i <= runs; i++)
            {
                Console.WriteLine($"\nПрогон {i}/{runs}: запустите игру, встаньте на одно место (или повторите тот же отрезок) и нажмите Enter.");
                Console.ReadLine();
                using var cap = new EtwPresentCapture();
                cap.Start();
                var gpuBefore = GpuThrottleProbe.IsThrottling();
                for (int s = duration + warmup; s > 0; s -= 5)
                {
                    Console.Write($"\r  Запись… осталось {s,3} с ");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(5, s)));
                }
                var gpuAfter = GpuThrottleProbe.IsThrottling();
                cap.Stop();
                Console.WriteLine();

                var proc = cap.GuessGameProcess(a.Get("process"));
                if (proc is null) { Output.Color(ConsoleColor.Red, "  Не найдено ни одного процесса с выводом кадров. Игра запущена?\n"); i--; continue; }
                var frames = MeasurementRun.TrimWarmup(MeasurementRun.FromTimestamps(cap.GetTimestamps(proc.Pid)), warmup);
                var mode = GameTune.Native.DisplayNative.GetDisplays().FirstOrDefault(d => d.IsPrimary) is { } disp
                    ? (disp.DeviceName, GameTune.Native.DisplayNative.GetCurrentMode(disp.DeviceName)) : (null, null);
                var run = new MeasurementRun
                {
                    Label = $"{Path.GetFileNameWithoutExtension(outPath)} #{i}",
                    FrameTimesMs = frames,
                    ThermalThrottled = gpuBefore == true || gpuAfter == true,
                    Conditions = new RunConditions
                    {
                        ProcessName = proc.Name, Scenario = scenario,
                        Width = mode.Item2?.Width, Height = mode.Item2?.Height, RefreshHz = mode.Item2?.Hz, DisplayDevice = mode.Item1,
                        OnAcPower = GameTune.Native.PowerNative.GetStatus().OnAc,
                    },
                };
                list.Add(run);
                var m = Metrics.ComputeAll(frames);
                Console.WriteLine($"  {proc.Name}: {frames.Count} кадров, средний FPS {m[MetricId.AvgFps]:0.#}, 1 % low {m[MetricId.Low1Fps]:0.#}, p99 {m[MetricId.FrameTimeP99]:0.##} мс" +
                                  (run.ThermalThrottled ? "  [троттлинг GPU — прогон ненадёжен]" : ""));
            }
            File.WriteAllText(outPath, GameTuneJson.Serialize(list));
            Console.WriteLine($"\nЗамер сохранён: {outPath}");
            return 0;
        }

        public static int Compare(Args a)
        {
            if (a.Positional.Count < 2) throw new ArgumentException("Использование: compare before.json after.json");
            var before = GameTuneJson.Deserialize<List<MeasurementRun>>(File.ReadAllText(a.Positional[0]));
            var after = GameTuneJson.Deserialize<List<MeasurementRun>>(File.ReadAllText(a.Positional[1]));
            var result = new Comparer().Compare(before, after);

            Output.Header("Сравнение «до / после»");
            foreach (var m in result.Metrics)
            {
                var color = m.Verdict switch { Verdict.Better => ConsoleColor.Green, Verdict.Worse => ConsoleColor.Red, _ => ConsoleColor.Gray };
                Output.Color(color, "  " + m.Format() + "\n");
                Output.Color(ConsoleColor.DarkGray, $"     95 % ДИ: до [{m.BeforeCi.Low:0.##}; {m.BeforeCi.High:0.##}], после [{m.AfterCi.Low:0.##}; {m.AfterCi.High:0.##}], разброс прогонов {m.RunSpread:0.##}\n");
            }
            foreach (var w in result.Warnings) Output.Color(ConsoleColor.Yellow, "  ! " + w + "\n");
            var overall = result.Overall switch
            {
                Verdict.Better => "Стало лучше — улучшение больше шума измерений.",
                Verdict.Worse => "Стало хуже — рекомендуется откатить последние изменения (gametune rollback --session …).",
                Verdict.NoChange => "Без изменений — разница в пределах шума.",
                _ => "Недостаточно данных для вывода.",
            };
            Console.WriteLine($"\nИтог: {overall}{(result.Reliable ? "" : " (сравнение помечено как ненадёжное)")}");

            if (a.Get("csv") is { } csv)
            {
                using var w = new StreamWriter(csv);
                w.WriteLine("set,run,frame,frametime_ms");
                foreach (var (set, runs) in new[] { ("before", before), ("after", after) })
                    for (int r = 0; r < runs.Count; r++)
                        for (int f = 0; f < runs[r].FrameTimesMs.Count; f++)
                            w.WriteLine(FormattableString.Invariant($"{set},{r + 1},{f},{runs[r].FrameTimesMs[f]:0.###}"));
                Console.WriteLine($"Сырые фреймтаймы: {csv}");
            }
            return 0;
        }

        public static int Latency(Args a)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Тест DPC/ISR возможен только в Windows.");
            int seconds = a.GetInt("seconds", 60);
            Console.WriteLine($"Тест задержки системы, {seconds} с. Не запускайте тяжёлые программы.");
            var dpc = EtwDpcCapture.Run(TimeSpan.FromSeconds(seconds));
            var timer = TimerAccuracyTest.Run();
            Output.Header("DPC/ISR — худшие драйверы");
            foreach (var d in dpc.Drivers.Take(10))
            {
                var bad = Math.Max(d.MaxDpcUs, d.MaxIsrUs) > 1000;
                Output.Color(bad ? ConsoleColor.Red : ConsoleColor.Gray, $"  {d.Driver,-28} DPC {d.MaxDpcUs,8:0} мкс   ISR {d.MaxIsrUs,8:0} мкс\n");
            }
            Output.Header("Точность таймера (Sleep(1))");
            Console.WriteLine($"  среднее {timer.MeanMs:0.###} мс, p99 {timer.P99Ms:0.###} мс, максимум {timer.MaxMs:0.###} мс" +
                              (timer.CurrentResolutionMs is { } res ? $", текущее разрешение таймера {res:0.###} мс" : ""));
            var profile = new SystemProfile { Dpc = dpc };
            Output.PrintFindings(GameTune.Analysis.Detectors.DetectorCatalog.RunAll(profile, new[] { new GameTune.Analysis.Detectors.DpcLatencyDetector() }));
            return 0;
        }

        public static async Task<int> Net(Args a)
        {
            var target = a.Get("target") ?? NetworkTest.DefaultTarget;
            Console.WriteLine($"Пинг до {target} в простое{(a.Has("no-load") ? "" : " и под нагрузкой (скачивание ~200 МБ)")}…");
            var q = await NetworkTest.RunAsync(target, a.Has("no-load") ? null : NetworkTest.DefaultLoadUrl);
            Console.WriteLine($"  В простое: {q.IdlePingMs:0.#} мс, джиттер {q.IdleJitterMs:0.#} мс, потери {q.PacketLossPercent:0.#} %");
            if (q.LoadedPingMs is { } l) Console.WriteLine($"  Под нагрузкой: {l:0.#} мс (+{l - q.IdlePingMs:0.#} мс)");
            Output.PrintFindings(GameTune.Analysis.Detectors.DetectorCatalog.RunAll(new SystemProfile { NetQuality = q },
                new[] { new GameTune.Analysis.Detectors.BufferbloatDetector() }));
            return 0;
        }
    }

    /// <summary>Проверка теплового троттлинга GPU (NVIDIA через nvidia-smi). Для других GPU — null.</summary>
    internal static class GpuThrottleProbe
    {
        public static bool? IsThrottling()
        {
            try
            {
                var r = GameTune.Native.ProcessRunner.Instance.Run("nvidia-smi",
                    "--query-gpu=clocks_throttle_reasons.hw_thermal_slowdown,clocks_throttle_reasons.sw_thermal_slowdown --format=csv,noheader", TimeSpan.FromSeconds(5));
                return r.Ok ? r.StdOut.Contains("Active", StringComparison.OrdinalIgnoreCase) : null;
            }
            catch { return null; }
        }
    }
}
