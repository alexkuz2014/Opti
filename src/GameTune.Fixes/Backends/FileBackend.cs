using GameTune.Core.Fixes;

namespace GameTune.Fixes.Backends;

/// <summary>
/// Файлы (конфиги OBS и т. п.). Read делает резервную копию и возвращает {Kind=BackupPath, Value=путь к копии}.
/// Write: Kind=Text — записать текст, Kind=BackupPath — восстановить из копии, Missing — удалить файл.
/// </summary>
public sealed class FileBackend : IStateBackend
{
    private readonly string _backupDir;
    public FileBackend(string backupDir) => _backupDir = backupDir;
    public ChangeType Type => ChangeType.File;

    public StateValue Read(string target)
    {
        if (!File.Exists(target)) return StateValue.Missing;
        Directory.CreateDirectory(_backupDir);
        var backup = Path.Combine(_backupDir, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}_{Guid.NewGuid():N}_{Path.GetFileName(target)}");
        File.Copy(target, backup);
        return new StateValue(true, "BackupPath", backup);
    }

    public void Write(string target, StateValue value)
    {
        if (!value.Exists) { if (File.Exists(target)) File.Delete(target); return; }
        switch (value.Kind)
        {
            case "Text":
                var tmp = target + ".gametune.tmp";
                File.WriteAllText(tmp, value.Value ?? "");
                File.Move(tmp, target, overwrite: true);
                break;
            case "BackupPath":
                if (!File.Exists(value.Value)) throw new FileNotFoundException("Резервная копия не найдена", value.Value);
                File.Copy(value.Value!, target, overwrite: true);
                break;
            default:
                throw new NotSupportedException($"Тип {value.Kind} для файла не поддерживается");
        }
    }

    public static string ReadText(StateValue v) => v.Kind switch
    {
        "BackupPath" => File.ReadAllText(v.Value!),
        "Text" => v.Value ?? "",
        _ => throw new NotSupportedException(v.Kind),
    };
}
