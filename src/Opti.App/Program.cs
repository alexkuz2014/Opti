using Opti.Bench;

namespace Opti.App;

/// <summary>
/// Точка входа. «Opti.exe --bench …» запускает встроенный синтетический тест без окна приложения —
/// так весь Opti остаётся одним портативным файлом.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (BenchProgram.IsBenchInvocation(args)) return BenchProgram.Run(args);
        var app = new App();
        app.InitializeComponent();
        // Тема Fluent по системной (светлая/тёмная, акцентный цвет, тёмный заголовок окна).
        // Задаётся после загрузки ресурсов приложения, чтобы словарь темы не был перезаписан.
        app.ThemeMode = System.Windows.ThemeMode.System;
        return app.Run();
    }
}
