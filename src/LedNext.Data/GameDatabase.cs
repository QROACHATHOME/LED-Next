using System.Globalization;
using LedNext.Core;
using LedNext.Core.Games;
using Microsoft.Data.Sqlite;

namespace LedNext.Data;

public sealed record PlatformSummary(string Key, string Name, int Games, int LitGames);

/// <summary>
/// The game library in a SQLite file (<c>config/games.db</c>). Thread safe: every call opens its own
/// (pooled) connection, so the importer can write on a background thread while the UI reads.
/// </summary>
/// <remarks>
/// Schema version 1 holds imported data only; an import replaces everything. User edits (phase 3,
/// next step) will go in their own tables so a re-import never loses them.
/// </remarks>
public sealed class GameDatabase : IGameLibrary
{
    public const int SchemaVersion = 1;
    private readonly string _connectionString;
    private readonly object _cacheLock = new();
    private Dictionary<string, LightProfile?> _lightCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, PlatformInfo>? _platforms;

    private GameDatabase(string path)
    {
        Path = path;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString();
    }

    public string Path { get; }

    public static GameDatabase Open(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var db = new GameDatabase(path);
        using var c = db.Connect();
        Exec(c, """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE IF NOT EXISTS platforms(
                key TEXT PRIMARY KEY COLLATE NOCASE, name TEXT NOT NULL, parent TEXT,
                uses_mame INTEGER NOT NULL, default_active TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS games(
                platform TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL COLLATE NOCASE,
                description TEXT NOT NULL, parent TEXT, players INTEGER NOT NULL, info TEXT NOT NULL,
                sources TEXT NOT NULL, lighting INTEGER NOT NULL, PRIMARY KEY(platform, name));
            CREATE INDEX IF NOT EXISTS games_description ON games(description COLLATE NOCASE);
            CREATE TABLE IF NOT EXISTS lights(
                platform TEXT NOT NULL COLLATE NOCASE, game TEXT NOT NULL COLLATE NOCASE, seq INTEGER NOT NULL,
                control TEXT NOT NULL, color TEXT, color_name TEXT, label TEXT, always_active INTEGER NOT NULL,
                source TEXT NOT NULL, PRIMARY KEY(platform, game, seq));
            INSERT OR IGNORE INTO meta(key, value) VALUES('schema_version', '1');
            """);
        return db;
    }

    private SqliteConnection Connect()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private static void Exec(SqliteConnection c, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ---- Import ---------------------------------------------------------------------------------

    /// <summary>Replaces the whole library in one transaction.</summary>
    public void ReplaceAll(InMemoryGameLibrary library, string importedFrom)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        Exec(c, "DELETE FROM lights; DELETE FROM games; DELETE FROM platforms;", tx);

        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO platforms VALUES($k, $n, $p, $m, $d)";
            var k = cmd.Parameters.Add("$k", SqliteType.Text);
            var n = cmd.Parameters.Add("$n", SqliteType.Text);
            var p = cmd.Parameters.Add("$p", SqliteType.Text);
            var m = cmd.Parameters.Add("$m", SqliteType.Integer);
            var d = cmd.Parameters.Add("$d", SqliteType.Text);
            foreach (var pl in library.Platforms.Values)
            {
                k.Value = pl.Key; n.Value = pl.Name; p.Value = (object?)pl.ParentKey ?? DBNull.Value;
                m.Value = pl.UsesMameData ? 1 : 0; d.Value = pl.DefaultActive.ToHex();
                cmd.ExecuteNonQuery();
            }
        }

        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR REPLACE INTO games VALUES($pl, $n, $d, $pa, $pr, $i, $s, $l)";
            var pl = cmd.Parameters.Add("$pl", SqliteType.Text);
            var n = cmd.Parameters.Add("$n", SqliteType.Text);
            var d = cmd.Parameters.Add("$d", SqliteType.Text);
            var pa = cmd.Parameters.Add("$pa", SqliteType.Text);
            var pr = cmd.Parameters.Add("$pr", SqliteType.Integer);
            var i = cmd.Parameters.Add("$i", SqliteType.Text);
            var s = cmd.Parameters.Add("$s", SqliteType.Text);
            var l = cmd.Parameters.Add("$l", SqliteType.Integer);
            foreach (var g in library.Games.Values)
            {
                pl.Value = g.Platform; n.Value = g.Name; d.Value = g.Description; pa.Value = (object?)g.Parent ?? DBNull.Value;
                pr.Value = g.Players; i.Value = g.Info; s.Value = g.Sources; l.Value = (int)g.Lighting;
                cmd.ExecuteNonQuery();
            }
        }

        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR REPLACE INTO lights VALUES($pl, $g, $seq, $c, $col, $cn, $lab, $a, $src)";
            var pl = cmd.Parameters.Add("$pl", SqliteType.Text);
            var g = cmd.Parameters.Add("$g", SqliteType.Text);
            var seq = cmd.Parameters.Add("$seq", SqliteType.Integer);
            var ctl = cmd.Parameters.Add("$c", SqliteType.Text);
            var col = cmd.Parameters.Add("$col", SqliteType.Text);
            var cn = cmd.Parameters.Add("$cn", SqliteType.Text);
            var lab = cmd.Parameters.Add("$lab", SqliteType.Text);
            var a = cmd.Parameters.Add("$a", SqliteType.Integer);
            var src = cmd.Parameters.Add("$src", SqliteType.Text);
            foreach (var (key, profile) in library.Lights)
            {
                var parts = key.Split('\u0001');
                pl.Value = parts[0]; g.Value = parts[1]; src.Value = profile.Source;
                for (int index = 0; index < profile.Controls.Count; index++)
                {
                    var cl = profile.Controls[index];
                    seq.Value = index; ctl.Value = cl.Control;
                    col.Value = cl.Color is { } rgb ? rgb.ToHex() : DBNull.Value;
                    cn.Value = (object?)cl.ColorName ?? DBNull.Value;
                    lab.Value = (object?)cl.Label ?? DBNull.Value;
                    a.Value = cl.AlwaysActive ? 1 : 0;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR REPLACE INTO meta VALUES('imported_from', $f), ('imported_at', $t)";
            cmd.Parameters.AddWithValue("$f", importedFrom);
            cmd.Parameters.AddWithValue("$t", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();

        lock (_cacheLock)
        {
            _lightCache = new(StringComparer.OrdinalIgnoreCase);
            _platforms = null;
        }
    }

    // ---- Queries --------------------------------------------------------------------------------

    public string? Meta(string key)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public int GameCount()
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM games";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Platforms with game counts, MAME first, then by name.</summary>
    public List<PlatformSummary> GetPlatformSummaries()
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT p.key, p.name, COUNT(g.name), COALESCE(SUM(g.lighting > 0), 0)
            FROM platforms p LEFT JOIN games g ON g.platform = p.key
            GROUP BY p.key ORDER BY p.key <> 'MAME', p.name COLLATE NOCASE
            """;
        using var r = cmd.ExecuteReader();
        var list = new List<PlatformSummary>();
        while (r.Read()) list.Add(new PlatformSummary(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3)));
        return list;
    }

    /// <summary>Games whose name or description contains <paramref name="text"/>, sorted by description.</summary>
    /// <returns>Up to <paramref name="limit"/> games and the total number of matches.</returns>
    public (List<GameInfo> Games, int Total) Search(string? platform, string? text, bool litOnly, int limit = 500)
    {
        using var c = Connect();
        const string where = """
            WHERE ($p IS NULL OR platform = $p) AND ($lit = 0 OR lighting > 0)
              AND ($t IS NULL OR name LIKE $t ESCAPE '\' OR description LIKE $t ESCAPE '\')
            """;
        void Bind(SqliteCommand cmd)
        {
            cmd.Parameters.AddWithValue("$p", (object?)platform ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lit", litOnly ? 1 : 0);
            var t = string.IsNullOrWhiteSpace(text) ? null
                : "%" + text.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            cmd.Parameters.AddWithValue("$t", (object?)t ?? DBNull.Value);
        }

        int total;
        using (var count = c.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM games " + where;
            Bind(count);
            total = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM games " + where + " ORDER BY description COLLATE NOCASE, name LIMIT $limit";
        Bind(cmd);
        cmd.Parameters.AddWithValue("$limit", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<GameInfo>();
        while (r.Read()) list.Add(ReadGame(r));
        return (list, total);
    }

    private static GameInfo ReadGame(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
        r.GetInt32(4), r.GetString(5), r.GetString(6), (GameLighting)r.GetInt32(7));

    // ---- IGameLibrary ---------------------------------------------------------------------------

    public PlatformInfo? GetPlatform(string key)
    {
        lock (_cacheLock)
        {
            if (_platforms is null)
            {
                var all = new Dictionary<string, PlatformInfo>(StringComparer.OrdinalIgnoreCase);
                using var c = Connect();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT key, name, parent, uses_mame, default_active FROM platforms";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    all[r.GetString(0)] = new PlatformInfo(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                        r.GetInt32(3) != 0, Rgb.ParseHex(r.GetString(4)));
                _platforms = all;
            }
            return _platforms.GetValueOrDefault(key);
        }
    }

    public GameInfo? GetGame(string platform, string name)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM games WHERE platform = $p AND name = $n";
        cmd.Parameters.AddWithValue("$p", platform);
        cmd.Parameters.AddWithValue("$n", name);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadGame(r) : null;
    }

    public LightProfile? GetLights(string platform, string? game)
    {
        var key = InMemoryGameLibrary.Key(platform, game);
        lock (_cacheLock)
            if (_lightCache.TryGetValue(key, out var cached)) return cached;

        LightProfile? profile = null;
        using (var c = Connect())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = """
                SELECT control, color, color_name, label, always_active, source FROM lights
                WHERE platform = $p AND game = $g ORDER BY seq
                """;
            cmd.Parameters.AddWithValue("$p", platform);
            cmd.Parameters.AddWithValue("$g", game ?? "");
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                profile ??= new LightProfile { Source = r.GetString(5) };
                profile.Controls.Add(new ControlLight(r.GetString(0), r.IsDBNull(1) ? null : Rgb.ParseHex(r.GetString(1)),
                    r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetInt32(4) != 0));
            }
        }
        lock (_cacheLock) _lightCache[key] = profile;
        return profile;
    }
}
