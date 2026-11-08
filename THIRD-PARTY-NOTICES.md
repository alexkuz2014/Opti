# Сторонние компоненты

Opti использует следующие библиотеки. Их лицензии разрешают распространение в составе программы.

| Компонент | Лицензия | Назначение |
|---|---|---|
| .NET 8 Runtime, WPF, System.Management, System.Diagnostics.EventLog | MIT | Платформа, интерфейс, WMI, журнал событий |
| Microsoft.Diagnostics.Tracing.TraceEvent | MIT | ETW: кадры (DXGI/D3D9 Present), DPC/ISR |
| Microsoft.Data.Sqlite | MIT | Журнал изменений |
| SQLitePCLRaw | Apache-2.0 | Нативная обвязка SQLite |
| SQLite | Public Domain | База журнала |
| Vortice.Windows (DXGI, Direct3D11, D3DCompiler) | MIT | DXGI, синтетический тест D3D11 |
| SharpGen.Runtime | MIT | Обвязка Vortice |
| CommunityToolkit.Mvvm | MIT | MVVM в интерфейсе |
| xUnit (только тесты) | Apache-2.0 | Тесты |

## PresentMon (необязательно, только командная строка)

Если рядом с `opti-cli.exe` лежит `PresentMon*.exe` (проект Intel / GameTechDev, лицензия MIT, https://github.com/GameTechDev/PresentMon), команда `opti-cli measure --mode presentmon` использует его как вспомогательную программу. В состав Opti PresentMon не входит.
