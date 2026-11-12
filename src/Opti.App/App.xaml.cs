using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Opti.App.Services;
using Opti.Fixes.Backends;
using Opti.ViewModels;

namespace Opti.App;

public partial class App : Application
{
    private WindowsAppServices? _services;
    private MainViewModel? _vm;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        try
        {
            _services = new WindowsAppServices();
        }
        catch (Exception ex)
        {
            Log(ex);
            MessageBox.Show("Не удалось запустить Opti: " + ex.Message, "Opti", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        ApplyBrandTheme();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(ApplyBrandTheme);
        var vm = new MainViewModel(_services);
        _vm = vm;
        // «Приглушать фон во время игры»: раз в 5 секунд смотрим, идёт ли игра
        var gameTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        gameTimer.Tick += (_, _) => vm.Game.Tick();
        gameTimer.Start();
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
#if DEV
        window.Title = "Opti DEV";
        Services.DevExport.Start(_services, text => Dispatcher.BeginInvoke(() => window.Title = "Opti DEV — " + text));
#endif
        window.Show();
        try { await vm.InitializeAsync(); }
        catch (Exception ex) { Log(ex); vm.ShowMessage("Ошибка при запуске: " + ex.Message, error: true); }
    }

    /// <summary>
    /// Акцентный текст и ссылки в цветах бренда с достаточным контрастом: на светлом фоне — тёмно-синий,
    /// на тёмном — светло-бирюзовый. Заливки (кнопки, переключатели) одинаковые в обеих темах.
    /// </summary>
    private void ApplyBrandTheme()
    {
        bool light = true;
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = k?.GetValue("AppsUseLightTheme") is not int v || v != 0;
        }
        catch { /* оставляем светлую */ }
        var text = light ? Color.FromRgb(0x0B, 0x5C, 0xD6) : Color.FromRgb(0x4F, 0xD3, 0xF7);
        Resources["AccentTextFillColorPrimaryBrush"] = new SolidColorBrush(text);
        Resources["AccentTextFillColorSecondaryBrush"] = new SolidColorBrush(text) { Opacity = 0.9 };
        Resources["AccentTextFillColorTertiaryBrush"] = new SolidColorBrush(text) { Opacity = 0.8 };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _vm?.Game.Shutdown();
#if DEV
        Services.DevExport.Write("выход");
#endif
        _services?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
#if DEV
        Services.DevExport.Write("ошибка: " + e.Exception.GetType().Name);
#endif
        MessageBox.Show("Непредвиденная ошибка: " + e.Exception.Message + "\n\nПодробности записаны в журнал: " + OptiPaths.LogsDir,
            "Opti", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(OptiPaths.LogsDir);
            File.AppendAllText(Path.Combine(OptiPaths.LogsDir, $"opti-{DateTime.Now:yyyyMMdd}.log"), $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch { /* журнал недоступен */ }
    }
}
