using System.Globalization;
using GameTune.Core.Fixes;
using GameTune.Core.Json;
using Microsoft.Data.Sqlite;

namespace GameTune.Fixes.Journal;

/// <summary>Журнал изменений в SQLite (C:\ProgramData\GameTune\journal.db).</summary>
public sealed class SqliteChangeJournal : IChangeJournal, IDisposable
{
    private readonly SqliteConnection _db;

    public SqliteChangeJournal(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _db.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("PRAGMA synchronous=FULL;");
        Exec("""
            CREATE TABLE IF NOT EXISTS changes(
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              fix_id TEXT NOT NULL,
              type TEXT NOT NULL,
              target TEXT NOT NULL COLLATE NOCASE,
              before_json TEXT NOT NULL,
              after_json TEXT NOT NULL,
              ts TEXT NOT NULL,
              session_id TEXT NOT NULL,
              rolled_back INTEGER NOT NULL DEFAULT 0,
              rolled_back_at TEXT NULL);
            CREATE INDEX IF NOT EXISTS ix_changes_target ON changes(type, target);
            CREATE TABLE IF NOT EXISTS sessions(
              session_id TEXT PRIMARY KEY,
              started_at TEXT NOT NULL,
              goal TEXT NULL,
              restore_point TEXT NULL,
              notes TEXT NULL);
            """);
    }

    public StateValue? GetActiveOriginal(ChangeType type, string target)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT before_json FROM changes WHERE type=$t AND target=$g AND rolled_back=0 ORDER BY id LIMIT 1";
        cmd.Parameters.AddWithValue("$t", type.ToString());
        cmd.Parameters.AddWithValue("$g", target);
        return cmd.ExecuteScalar() is string json ? GameTuneJson.Deserialize<StateValue>(json) : null;
    }

    public long Append(ChangeRecord r)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO changes(fix_id,type,target,before_json,after_json,ts,session_id)
            VALUES($f,$t,$g,$b,$a,$ts,$s); SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$f", r.FixId);
        cmd.Parameters.AddWithValue("$t", r.Type.ToString());
        cmd.Parameters.AddWithValue("$g", r.Target);
        cmd.Parameters.AddWithValue("$b", GameTuneJson.Serialize(r.Before));
        cmd.Parameters.AddWithValue("$a", GameTuneJson.Serialize(r.After));
        cmd.Parameters.AddWithValue("$ts", r.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("$s", r.SessionId);
        return (long)cmd.ExecuteScalar()!;
    }

    public void Remove(long recordId)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM changes WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", recordId);
        cmd.ExecuteNonQuery();
    }

    public void MarkRolledBack(long recordId, DateTimeOffset at)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "UPDATE changes SET rolled_back=1, rolled_back_at=$at WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", recordId);
        cmd.Parameters.AddWithValue("$at", at.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<ChangeRecord> GetAll()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id,fix_id,type,target,before_json,after_json,ts,session_id,rolled_back,rolled_back_at FROM changes ORDER BY id";
        using var rd = cmd.ExecuteReader();
        var list = new List<ChangeRecord>();
        while (rd.Read())
        {
            list.Add(new ChangeRecord
            {
                Id = rd.GetInt64(0),
                FixId = rd.GetString(1),
                Type = Enum.Parse<ChangeType>(rd.GetString(2)),
                Target = rd.GetString(3),
                Before = GameTuneJson.Deserialize<StateValue>(rd.GetString(4)),
                After = GameTuneJson.Deserialize<StateValue>(rd.GetString(5)),
                Timestamp = DateTimeOffset.Parse(rd.GetString(6), CultureInfo.InvariantCulture),
                SessionId = rd.GetString(7),
                RolledBack = rd.GetInt64(8) != 0,
                RolledBackAt = rd.IsDBNull(9) ? null : DateTimeOffset.Parse(rd.GetString(9), CultureInfo.InvariantCulture),
            });
        }
        return list;
    }

    public void SaveSession(SessionInfo s)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sessions(session_id,started_at,goal,restore_point,notes) VALUES($id,$at,$g,$rp,$n)
            ON CONFLICT(session_id) DO UPDATE SET goal=excluded.goal, restore_point=excluded.restore_point, notes=excluded.notes;
            """;
        cmd.Parameters.AddWithValue("$id", s.SessionId);
        cmd.Parameters.AddWithValue("$at", s.StartedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$g", (object?)s.Goal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rp", (object?)s.RestorePoint ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$n", (object?)s.Notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<SessionInfo> GetSessions()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT session_id,started_at,goal,restore_point,notes FROM sessions ORDER BY started_at";
        using var rd = cmd.ExecuteReader();
        var list = new List<SessionInfo>();
        while (rd.Read())
            list.Add(new SessionInfo
            {
                SessionId = rd.GetString(0),
                StartedAt = DateTimeOffset.Parse(rd.GetString(1), CultureInfo.InvariantCulture),
                Goal = rd.IsDBNull(2) ? null : rd.GetString(2),
                RestorePoint = rd.IsDBNull(3) ? null : rd.GetString(3),
                Notes = rd.IsDBNull(4) ? null : rd.GetString(4),
            });
        return list;
    }

    private void Exec(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
