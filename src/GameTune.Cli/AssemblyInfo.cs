// CLI рассчитан на Windows. Команды, которые работают и в других ОС (plan/analyze на сохранённом профиле,
// compare двух файлов), не вызывают Windows-API; остальные проверяют ОС и выдают понятную ошибку.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]
