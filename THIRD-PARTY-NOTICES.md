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

## PresentMon (необязательно)

Если рядом с программой лежит `PresentMon*.exe`, Opti использует его как вспомогательную программу для режима «Игра через PresentMon». PresentMon — проект Intel / GameTechDev, лицензия MIT: https://github.com/GameTechDev/PresentMon. Если релиз собран в CI с PresentMon, его лицензия действует для этого файла:

> Copyright (C) 2017-2024 Intel Corporation. Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.
