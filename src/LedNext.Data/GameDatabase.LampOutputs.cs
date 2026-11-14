using System.Globalization;
using LedNext.Core.Outputs;
using Microsoft.Data.Sqlite;

namespace LedNext.Data;

/// <summary>Known lamps: every output an emulator reported, per emulator and game (schema 8, <c>lamp_outputs</c>).</summary>
public sealed partial class GameDatabase
{
    /// <summary>
    /// Stores the outputs one game session reported: new ones are added, known ones get their last-seen time, value
    /// range, change count and current mapping updated. Never touched by an import.
    /// </summary>
    public void RecordLampOutputs(string emulator, string game, string mapSource, IReadOnlyList<LampOutputSeen> outputs,
        DateTime? when = null)
    {
        if (outputs.Count == 0 || string.IsNullOrWhiteSpace(emulator) || string.IsNullOrWhiteSpace(game)) return;
        var now = (when ?? DateTime.UtcNow).ToString("o", CultureInfo.InvariantCulture);
        using var c = Connect();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO lamp_outputs(emulator, game, output, mapped_to, map_source, first_seen, last_seen, sessions,
                                     min_value, max_value, changes)
            VALUES($e, $g, $o, $m, $s, $t, $t, 1, $min, $max, $ch)
            ON CONFLICT(emulator, game, output) DO UPDATE SET
                mapped_to = excluded.mapped_to, map_source = excluded.map_source, last_seen = excluded.last_seen,
                sessions = sessions + 1, min_value = MIN(min_value, excluded.min_value),
                max_value = MAX(max_value, excluded.max_value), changes = changes + excluded.changes
            """;
        cmd.Parameters.AddWithValue("$e", emulator.Trim());
        cmd.Parameters.AddWithValue("$g", game.Trim());
        cmd.Parameters.AddWithValue("$s", mapSource);
        cmd.Parameters.AddWithValue("$t", now);
        var po = cmd.Parameters.Add("$o", SqliteType.Text);
        var pm = cmd.Parameters.Add("$m", SqliteType.Text);
        var pmin = cmd.Parameters.Add("$min", SqliteType.Integer);
        var pmax = cmd.Parameters.Add("$max", SqliteType.Integer);
        var pch = cmd.Parameters.Add("$ch", SqliteType.Integer);
        foreach (var o in outputs)
        {
            po.Value = o.Output;
            pm.Value = o.MappedTo;
            pmin.Value = o.Min;
            pmax.Value = o.Max;
            pch.Value = o.Changes;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Known lamps, newest first. <paramref name="text"/> filters emulator, game, output or mapping.</summary>
    public List<KnownLamp> ListLampOutputs(string? emulator = null, string? text = null, bool unmappedOnly = false, int limit = 2000)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(emulator)) { where.Add("emulator = $e"); cmd.Parameters.AddWithValue("$e", emulator.Trim()); }
        if (!string.IsNullOrWhiteSpace(text))
        {
            where.Add("(emulator LIKE $t ESCAPE '\\' OR game LIKE $t ESCAPE '\\' OR output LIKE $t ESCAPE '\\' OR mapped_to LIKE $t ESCAPE '\\')");
            cmd.Parameters.AddWithValue("$t", "%" + text.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
        }
        if (unmappedOnly) where.Add("mapped_to = ''");
        cmd.CommandText = "SELECT emulator, game, output, mapped_to, map_source, first_seen, last_seen, sessions, min_value, max_value, changes " +
                          "FROM lamp_outputs" + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "") +
                          " ORDER BY last_seen DESC, emulator, game, output LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));
        var list = new List<KnownLamp>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new KnownLamp(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                ParseTime(r.GetString(5)), ParseTime(r.GetString(6)), r.GetInt32(7), r.GetInt32(8), r.GetInt32(9), r.GetInt32(10)));
        return list;
    }

    public int LampOutputCount()
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM lamp_outputs";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>The emulators with known lamps, for the Lamps tab's filter.</summary>
    public List<string> LampEmulators()
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT emulator FROM lamp_outputs ORDER BY emulator COLLATE NOCASE";
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    /// <summary>Stored as UTC; returned as local time (for the Lamps tab).</summary>
    private static DateTime ParseTime(string s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime() : DateTime.MinValue;
}
