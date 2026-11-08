using System.Windows;
using System.Windows.Threading;
using GameTune.App.Services;
using GameTune.Fixes.Backends;
using GameTune.ViewModels;

namespace GameTune.App;

public partial class App : Application
{
    private WindowsAppServices? _services;

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
            MessageBox.Show("Не удалось запустить GameTune: " + ex.Message, "GameTune", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        var vm = new MainViewModel(_services);
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();
        try { await vm.InitializeAsync(); }
        catch (Exception ex) { Log(ex); vm.ShowMessage("Ошибка при запуске: " + ex.Message, error: true); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        MessageBox.Show("Непредвиденная ошибка: " + e.Exception.Message + "\n\nПодробности записаны в журнал: " + GameTunePaths.LogsDir,
            "GameTune", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(GameTunePaths.LogsDir);
            File.AppendAllText(Path.Combine(GameTunePaths.LogsDir, $"gametune-{DateTime.Now:yyyyMMdd}.log"), $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch { /* журнал недоступен */ }
    }
}
