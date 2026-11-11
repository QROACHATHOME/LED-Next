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
/// Imported data (platforms, games, lights) is replaced as a whole by each import. The user's edits
/// live in user_profiles / user_lights (schema 2), which an import never touches; a customized
/// profile replaces the imported one at the same level (game or platform default).
/// </remarks>
public sealed class GameDatabase : IGameLibrary
{
    /// <summary>2: user edits · 3: games.buttons / tags · 4: launchbox_games · 5: launchbox_games.genre · 6: launchbox_games.emulator
    /// · 7: button_banners.</summary>
    public const int SchemaVersion = 7;
    private const string GameColumns = """
        g.platform, g.name, g.description, g.parent, g.players, g.info, g.sources, g.lighting,
        EXISTS(SELECT 1 FROM user_profiles u WHERE u.platform = g.platform AND u.game = g.name), g.buttons, g.tags,
        (SELECT MAX(lb.date_added) FROM launchbox_games lb WHERE lb.platform = g.platform AND lb.game = g.name)
        """;
    private readonly string _connectionString;
    private readonly object _cacheLock = new();
    private Dictionary<string, LightProfile?> _lightCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, PlatformInfo>? _platforms;
    private Dictionary<string, int>? _bannerButtons;

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
                sources TEXT NOT NULL, lighting INTEGER NOT NULL,
                buttons INTEGER NOT NULL DEFAULT 0, tags TEXT NOT NULL DEFAULT '', PRIMARY KEY(platform, name));
            CREATE INDEX IF NOT EXISTS games_description ON games(description COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS games_name ON games(name);
            CREATE TABLE IF NOT EXISTS lights(
                platform TEXT NOT NULL COLLATE NOCASE, game TEXT NOT NULL COLLATE NOCASE, seq INTEGER NOT NULL,
                control TEXT NOT NULL, color TEXT, color_name TEXT, label TEXT, always_active INTEGER NOT NULL,
                source TEXT NOT NULL, PRIMARY KEY(platform, game, seq));
            CREATE TABLE IF NOT EXISTS user_profiles(
                platform TEXT NOT NULL COLLATE NOCASE, game TEXT NOT NULL COLLATE NOCASE,
                updated_at TEXT NOT NULL, PRIMARY KEY(platform, game));
            CREATE TABLE IF NOT EXISTS user_lights(
                platform TEXT NOT NULL COLLATE NOCASE, game TEXT NOT NULL COLLATE NOCASE, seq INTEGER NOT NULL,
                control TEXT NOT NULL, color TEXT, color_name TEXT, label TEXT, always_active INTEGER NOT NULL,
                PRIMARY KEY(platform, game, seq));
            CREATE TABLE IF NOT EXISTS templates(
                name TEXT PRIMARY KEY COLLATE NOCASE, description TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS template_lights(
                name TEXT NOT NULL COLLATE NOCASE, seq INTEGER NOT NULL, control TEXT NOT NULL, color TEXT,
                color_name TEXT, label TEXT, always_active INTEGER NOT NULL, PRIMARY KEY(name, seq));
            CREATE TABLE IF NOT EXISTS smart_lists(
                name TEXT PRIMARY KEY COLLATE NOCASE, filter TEXT NOT NULL, seq INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS launchbox_games(
                id TEXT NOT NULL, title TEXT NOT NULL, lb_platform TEXT NOT NULL COLLATE NOCASE, app_path TEXT NOT NULL,
                rom TEXT NOT NULL, date_added TEXT, platform TEXT NOT NULL COLLATE NOCASE, game TEXT NOT NULL COLLATE NOCASE,
                matched_by TEXT NOT NULL, platform_how TEXT NOT NULL, genre TEXT NOT NULL DEFAULT '', emulator TEXT NOT NULL DEFAULT '');
            CREATE INDEX IF NOT EXISTS launchbox_games_game ON launchbox_games(platform, game);
            CREATE INDEX IF NOT EXISTS launchbox_games_added ON launchbox_games(date_added);
            CREATE TABLE IF NOT EXISTS button_banners(
                rom TEXT PRIMARY KEY COLLATE NOCASE, buttons INTEGER NOT NULL, positions TEXT NOT NULL);
            """);

        var oldVersion = ReadMeta(c, "schema_version");
        // Schema 3 added games.buttons / games.tags. Older files get the columns (empty until the next import).
        bool addedColumns = !HasColumn(c, "games", "buttons");
        if (addedColumns)
            Exec(c, "ALTER TABLE games ADD COLUMN buttons INTEGER NOT NULL DEFAULT 0; ALTER TABLE games ADD COLUMN tags TEXT NOT NULL DEFAULT '';");
        // Schema 5 added launchbox_games.genre (empty until LaunchBox is read again).
        if (!HasColumn(c, "launchbox_games", "genre"))
        {
            Exec(c, "ALTER TABLE launchbox_games ADD COLUMN genre TEXT NOT NULL DEFAULT '';");
            AppLog.Info("Read LaunchBox again to fill the genre filter (new in schema 5).");
        }
        // Schema 6 added launchbox_games.emulator: the program that runs the game, for its MAME install's keys.
        if (!HasColumn(c, "launchbox_games", "emulator"))
        {
            Exec(c, "ALTER TABLE launchbox_games ADD COLUMN emulator TEXT NOT NULL DEFAULT '';");
            AppLog.Info("Read LaunchBox again to learn each game's emulator (new in schema 6).");
        }
        var newVersion = SchemaVersion.ToString(CultureInfo.InvariantCulture);
        if (oldVersion is not null && oldVersion != newVersion)
            AppLog.Info($"Game library upgraded from schema {oldVersion} to {newVersion}.");
        if (addedColumns)
            AppLog.Info("Re-import from LEDBlinky to fill the button and control filters (new in schema 3).");
        Exec(c, $"INSERT OR REPLACE INTO meta(key, value) VALUES('schema_version', '{newVersion}');");
        db.SeedBuiltIns(c);
        return db;
    }

    private static string? ReadMeta(SqliteConnection c, string key)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private static bool HasColumn(SqliteConnection c, string table, string column)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $c";
        cmd.Parameters.AddWithValue("$c", column);
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>Adds the starter templates and smart lists once. Deleted ones stay deleted.</summary>
    private void SeedBuiltIns(SqliteConnection c)
    {
        string? seeded;
        using (var check = c.CreateCommand())
        {
            check.CommandText = "SELECT value FROM meta WHERE key = 'seeded_builtins'";
            seeded = check.ExecuteScalar() as string;
        }
        if (seeded is null)
        {
            foreach (var t in BuiltIns.Templates) SaveTemplate(t);
            int seq = 0;
            foreach (var (name, filter) in BuiltIns.SmartLists) SaveSmartList(name, filter, seq++);
        }
        if (seeded is null or "1")
        {
            // The LaunchBox lists go first: they are the everyday "what's new" view.
            int seq = -BuiltIns.SmartListsV2.Count;
            foreach (var (name, filter) in BuiltIns.SmartListsV2) SaveSmartList(name, filter, seq++);
        }
        if (seeded is null or "1" or "2")
        {
            // Right after the 6-button list (seq 0) it narrows down.
            foreach (var (name, filter) in BuiltIns.SmartListsV3) SaveSmartList(name, filter, 0);
            Exec(c, "INSERT OR REPLACE INTO meta(key, value) VALUES('seeded_builtins', '3');");
        }
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
            cmd.CommandText = """
                INSERT OR REPLACE INTO games(platform, name, description, parent, players, info, sources, lighting, buttons, tags)
                VALUES($pl, $n, $d, $pa, $pr, $i, $s, $l, $b, $tg)
                """;
            var b = cmd.Parameters.Add("$b", SqliteType.Integer);
            var tg = cmd.Parameters.Add("$tg", SqliteType.Text);
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
                b.Value = g.Buttons; tg.Value = g.Tags;
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
        // The import replaced the games table; put the LaunchBox-only games back.
        MergeLaunchBoxGames();
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
    public (List<GameInfo> Games, int Total) Search(string? platform, string? text, bool litOnly, int limit = 500, bool customizedOnly = false) =>
        Search(new GameFilter { Platform = platform, Text = text, LitOnly = litOnly, CustomizedOnly = customizedOnly }, limit);

    /// <summary>Games matching a filter (a smart list), sorted by description.</summary>
    /// <returns>Up to <paramref name="limit"/> games and the total number of matches.</returns>
    public (List<GameInfo> Games, int Total) Search(GameFilter f, int limit = 500)
    {
        using var c = Connect();
        const string custom = "EXISTS(SELECT 1 FROM user_profiles u WHERE u.platform = g.platform AND u.game = g.name)";
        const string where = $"""
            WHERE ($p IS NULL OR g.platform = $p) AND ($lit = 0 OR g.lighting > 0 OR {custom})
              AND ($custom = 0 OR {custom})
              AND ($noclones = 0 OR g.parent IS NULL)
              AND ($minp IS NULL OR g.players >= $minp) AND ($maxp IS NULL OR g.players <= $maxp)
              AND ($minb IS NULL OR g.buttons >= $minb) AND ($maxb IS NULL OR g.buttons <= $maxb)
              AND ($tag IS NULL OR (' ' || g.tags || ' ') LIKE $tag)
              AND ($t IS NULL OR g.name LIKE $t ESCAPE '\' OR g.description LIKE $t ESCAPE '\')
              AND ($needs = 0 OR (g.lighting = 0 AND NOT {custom}))
              AND ($inlb = 0 OR EXISTS(SELECT 1 FROM launchbox_games lb WHERE lb.platform = g.platform AND lb.game = g.name))
              AND ($since IS NULL OR EXISTS(SELECT 1 FROM launchbox_games lb
                    WHERE lb.platform = g.platform AND lb.game = g.name AND lb.date_added >= $since))
              AND ($genre IS NULL OR EXISTS(SELECT 1 FROM launchbox_games lb
                    WHERE lb.platform = g.platform AND lb.genre LIKE $genre ESCAPE '\'
                      AND (lb.game = g.name OR EXISTS(SELECT 1 FROM games k
                            WHERE k.platform = g.platform AND k.parent = g.name AND k.name = lb.game))))
            """;
        void Bind(SqliteCommand cmd)
        {
            static object N(object? v) => v ?? DBNull.Value;
            cmd.Parameters.AddWithValue("$p", N(f.Platform));
            cmd.Parameters.AddWithValue("$lit", f.LitOnly ? 1 : 0);
            cmd.Parameters.AddWithValue("$custom", f.CustomizedOnly ? 1 : 0);
            cmd.Parameters.AddWithValue("$noclones", f.HideClones ? 1 : 0);
            cmd.Parameters.AddWithValue("$minp", N(f.MinPlayers));
            cmd.Parameters.AddWithValue("$maxp", N(f.MaxPlayers));
            cmd.Parameters.AddWithValue("$minb", N(f.MinButtons));
            cmd.Parameters.AddWithValue("$maxb", N(f.MaxButtons));
            cmd.Parameters.AddWithValue("$tag", N(string.IsNullOrWhiteSpace(f.Control) ? null : $"% {f.Control.Trim().ToLowerInvariant()} %"));
            static string? Like(string? s) => string.IsNullOrWhiteSpace(s) ? null
                : "%" + s.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            cmd.Parameters.AddWithValue("$t", N(Like(f.Text)));
            cmd.Parameters.AddWithValue("$genre", N(Like(f.Genre)));
            cmd.Parameters.AddWithValue("$needs", f.NeedsSetupOnly ? 1 : 0);
            cmd.Parameters.AddWithValue("$inlb", f.InLaunchBoxOnly ? 1 : 0);
            cmd.Parameters.AddWithValue("$since", N(f.AddedWithinDays is { } days ? ToUtc(DateTimeOffset.UtcNow.AddDays(-days)) : null));
        }

        int total;
        using (var count = c.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM games g " + where;
            Bind(count);
            total = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using var cmd = c.CreateCommand();
        // "Recently added" lists show the newest first.
        var order = f.AddedWithinDays is not null || f.InLaunchBoxOnly
            ? " ORDER BY 12 DESC, g.description COLLATE NOCASE, g.name"
            : " ORDER BY g.description COLLATE NOCASE, g.name";
        cmd.CommandText = $"SELECT {GameColumns} FROM games g " + where + order + " LIMIT $limit";
        Bind(cmd);
        cmd.Parameters.AddWithValue("$limit", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<GameInfo>();
        while (r.Read()) list.Add(ReadGame(r));
        return (list, total);
    }

    private static GameInfo ReadGame(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
        r.GetInt32(4), r.GetString(5), r.GetString(6), (GameLighting)r.GetInt32(7), r.GetInt32(8) != 0,
        r.GetInt32(9), r.GetString(10), r.IsDBNull(11) ? null : ParseUtc(r.GetString(11)));

    private const string UtcFormat = "yyyy-MM-ddTHH:mm:ssZ";
    private static string ToUtc(DateTimeOffset d) => d.UtcDateTime.ToString(UtcFormat, CultureInfo.InvariantCulture);
    private static DateTimeOffset? ParseUtc(string s) =>
        DateTimeOffset.TryParseExact(s, UtcFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;

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
        cmd.CommandText = $"SELECT {GameColumns} FROM games g WHERE g.platform = $p AND g.name = $n";
        cmd.Parameters.AddWithValue("$p", platform);
        cmd.Parameters.AddWithValue("$n", name);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadGame(r) : null;
    }

    // ---- Button banners --------------------------------------------------------------------------

    /// <summary>Buttons a MAME rom uses according to its button banner (null: no banner read for it).</summary>
    public int? GetBannerButtons(string rom)
    {
        Dictionary<string, int>? all;
        lock (_cacheLock) all = _bannerButtons;
        if (all is null)
        {
            all = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using var c = Connect();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT rom, buttons FROM button_banners";
            using var r = cmd.ExecuteReader();
            while (r.Read()) all[r.GetString(0)] = r.GetInt32(1);
            lock (_cacheLock) _bannerButtons = all;
        }
        return all.TryGetValue(rom, out var n) ? n : null;
    }

    public int BannerCount()
    {
        GetBannerButtons("");
        lock (_cacheLock) return _bannerButtons?.Count ?? 0;
    }

    /// <summary>
    /// Replaces the button banners read from <paramref name="folder"/> (rom → used positions 1-6). An empty map
    /// with a null folder removes them. <paramref name="stamp"/> lets start-up skip reading an unchanged folder.
    /// </summary>
    public void ReplaceBannerButtons(IReadOnlyDictionary<string, IReadOnlyList<int>> banners, string? folder, string? stamp)
    {
        using (var c = Connect())
        using (var tx = c.BeginTransaction())
        {
            Exec(c, "DELETE FROM button_banners", tx);
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR REPLACE INTO button_banners(rom, buttons, positions) VALUES($r, $b, $p)";
            var pr = cmd.Parameters.Add("$r", SqliteType.Text);
            var pb = cmd.Parameters.Add("$b", SqliteType.Integer);
            var pp = cmd.Parameters.Add("$p", SqliteType.Text);
            foreach (var (rom, positions) in banners)
            {
                pr.Value = rom;
                pb.Value = positions.Count;
                pp.Value = string.Join(",", positions);
                cmd.ExecuteNonQuery();
            }
            SetMeta(c, "banner_folder", folder, tx);
            SetMeta(c, "banner_stamp", stamp, tx);
            tx.Commit();
        }
        lock (_cacheLock) _bannerButtons = null;
    }

    private static void SetMeta(SqliteConnection c, string key, string? value, SqliteTransaction tx)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = value is null ? "DELETE FROM meta WHERE key = $k" : "INSERT OR REPLACE INTO meta(key, value) VALUES($k, $v)";
        cmd.Parameters.AddWithValue("$k", key);
        if (value is not null) cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public LightProfile? GetLights(string platform, string? game)
    {
        var key = InMemoryGameLibrary.Key(platform, game);
        lock (_cacheLock)
            if (_lightCache.TryGetValue(key, out var cached)) return cached;

        LightProfile? profile = null;
        using (var c = Connect())
        {
            bool customized;
            using (var check = c.CreateCommand())
            {
                check.CommandText = "SELECT 1 FROM user_profiles WHERE platform = $p AND game = $g";
                check.Parameters.AddWithValue("$p", platform);
                check.Parameters.AddWithValue("$g", game ?? "");
                customized = check.ExecuteScalar() is not null;
            }
            if (customized) profile = new LightProfile { Source = "LED-Next", Customized = true };

            using var cmd = c.CreateCommand();
            cmd.CommandText = customized
                ? "SELECT control, color, color_name, label, always_active, 'LED-Next' FROM user_lights WHERE platform = $p AND game = $g ORDER BY seq"
                : "SELECT control, color, color_name, label, always_active, source FROM lights WHERE platform = $p AND game = $g ORDER BY seq";
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

    // ---- LaunchBox ------------------------------------------------------------------------------

    public sealed record LaunchBoxPlatformSummary(string LaunchBoxPlatform, string Platform, string How, int Games, int ByRom, int ByTitle, int New);

    /// <summary>
    /// Replaces the stored LaunchBox library: matches every game to an LED-Next platform and game,
    /// then adds games the library doesn't have (they get the platform default until set up).
    /// </summary>
    /// <param name="overrides">Your platform corrections (LaunchBox name → LED-Next key).</param>
    public List<LaunchBoxPlatformSummary> ImportLaunchBox(IReadOnlyList<Core.Import.LaunchBox.LaunchBoxGame> games,
        IReadOnlyDictionary<string, string> overrides, string folder)
    {
        var platformKeys = GetPlatformSummaries().Select(p => p.Key).ToList();
        var titleIndex = TitleIndex();

        var platformFor = new Dictionary<string, (string Key, string How)>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<(Core.Import.LaunchBox.LaunchBoxMatch Match, string How)>();
        foreach (var g in games)
        {
            if (!platformFor.TryGetValue(g.Platform, out var p))
                platformFor[g.Platform] = p = Core.Import.LaunchBox.PlatformMatcher.Match(g.Platform, platformKeys, overrides);
            matches.Add((Core.Import.LaunchBox.GameMatcher.Match(g, p.Key, this, titleIndex), p.How));
        }

        using (var c = Connect())
        using (var tx = c.BeginTransaction())
        {
            Exec(c, "DELETE FROM launchbox_games;", tx);
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO launchbox_games(id, title, lb_platform, app_path, rom, date_added, platform, game, matched_by, platform_how, genre, emulator)
                VALUES($id, $t, $lp, $ap, $rom, $d, $p, $g, $m, $h, $genre, $emu)
                """;
            var genre = cmd.Parameters.Add("$genre", SqliteType.Text);
            var emu = cmd.Parameters.Add("$emu", SqliteType.Text);
            var id = cmd.Parameters.Add("$id", SqliteType.Text);
            var t = cmd.Parameters.Add("$t", SqliteType.Text);
            var lp = cmd.Parameters.Add("$lp", SqliteType.Text);
            var ap = cmd.Parameters.Add("$ap", SqliteType.Text);
            var rom = cmd.Parameters.Add("$rom", SqliteType.Text);
            var d = cmd.Parameters.Add("$d", SqliteType.Text);
            var pl = cmd.Parameters.Add("$p", SqliteType.Text);
            var gm = cmd.Parameters.Add("$g", SqliteType.Text);
            var m = cmd.Parameters.Add("$m", SqliteType.Text);
            var h = cmd.Parameters.Add("$h", SqliteType.Text);
            foreach (var (match, how) in matches)
            {
                var g = match.Game;
                id.Value = g.Id; t.Value = g.Title; lp.Value = g.Platform; ap.Value = g.ApplicationPath; rom.Value = g.Rom;
                d.Value = g.DateAdded is { } date ? ToUtc(date) : DBNull.Value;
                pl.Value = match.LedPlatform; gm.Value = match.LedGame; m.Value = match.MatchedBy; h.Value = how; genre.Value = g.Genre; emu.Value = g.EmulatorPath;
                cmd.ExecuteNonQuery();
            }
            using (var meta = c.CreateCommand())
            {
                meta.Transaction = tx;
                meta.CommandText = "INSERT OR REPLACE INTO meta VALUES('launchbox_folder', $f), ('launchbox_read_at', $at)";
                meta.Parameters.AddWithValue("$f", folder);
                meta.Parameters.AddWithValue("$at", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                meta.ExecuteNonQuery();
            }
            tx.Commit();
        }
        MergeLaunchBoxGames();
        return LaunchBoxSummaries();
    }

    /// <summary>
    /// The LED-Next platform and game for a game a front end reports: by LaunchBox id (from the last
    /// Read LaunchBox), then by LaunchBox platform + title, else matched now like a LaunchBox read would
    /// (a game added to LaunchBox since then). Null when there is no platform to go on.
    /// </summary>
    /// <returns>Platform key, game name, and how it was found ("LaunchBox id", "title", "matched now").</returns>
    public (string Platform, string Game, string How)? FindFrontEndGame(string? id, string? launchBoxPlatform, string? title,
        string? applicationPath, IReadOnlyDictionary<string, string>? overrides)
    {
        using (var c = Connect())
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT platform, game FROM launchbox_games WHERE id = $id LIMIT 1";
                cmd.Parameters.AddWithValue("$id", id.Trim());
                using var r = cmd.ExecuteReader();
                if (r.Read()) return (r.GetString(0), r.GetString(1), "LaunchBox id");
            }
            if (!string.IsNullOrWhiteSpace(launchBoxPlatform) && !string.IsNullOrWhiteSpace(title))
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT platform, game FROM launchbox_games WHERE lb_platform = $p AND title = $t COLLATE NOCASE LIMIT 1";
                cmd.Parameters.AddWithValue("$p", launchBoxPlatform.Trim());
                cmd.Parameters.AddWithValue("$t", title.Trim());
                using var r = cmd.ExecuteReader();
                if (r.Read()) return (r.GetString(0), r.GetString(1), "title");
            }
        }
        if (string.IsNullOrWhiteSpace(launchBoxPlatform)) return null;

        var key = Core.Import.LaunchBox.PlatformMatcher.Match(launchBoxPlatform, GetPlatformSummaries().Select(p => p.Key), overrides).Key;
        var path = applicationPath ?? "";
        var exe = path.Replace('/', '\\').Split('\\').LastOrDefault() ?? "";
        var rom = Core.Import.LaunchBox.LaunchBoxLibrary.BuiltInEmulatorExes.Contains(exe, StringComparer.OrdinalIgnoreCase)
            ? "" : Core.Import.LaunchBox.LaunchBoxLibrary.RomName(path);
        var game = new Core.Import.LaunchBox.LaunchBoxGame(id ?? "", title ?? "", launchBoxPlatform, path, rom, null);
        var noTitles = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        var match = Core.Import.LaunchBox.GameMatcher.Match(game, key, this, noTitles);
        return (match.LedPlatform, match.LedGame, "matched now");
    }

    /// <summary>
    /// The LaunchBox games matched to this LED-Next game (platform and title as LaunchBox has them). With
    /// <paramref name="orMameDataPlatforms"/>, also the same rom on platforms that use MAME's data (a MAME game
    /// LaunchBox lists under Neo Geo or Cave).
    /// </summary>
    public List<(string LaunchBoxPlatform, string Title)> LaunchBoxEntries(string platform, string game, bool orMameDataPlatforms = false)
    {
        var list = new List<(string, string)>();
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT lb_platform, title FROM launchbox_games
            WHERE game = $g AND (platform = $p OR ($m = 1 AND platform IN (SELECT key FROM platforms WHERE uses_mame = 1)))
            GROUP BY lb_platform, title
            ORDER BY MIN(platform <> $p), lb_platform, title
            """;
        cmd.Parameters.AddWithValue("$m", orMameDataPlatforms ? 1 : 0);
        cmd.Parameters.AddWithValue("$p", platform);
        cmd.Parameters.AddWithValue("$g", game);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
        return list;
    }

    /// <summary>
    /// The LED-Next platform for a front end's platform name (BigBox's platform wheel): the platform its
    /// LaunchBox games were matched to, else the platform matcher. Null when LED-Next has no such platform.
    /// </summary>
    public (string Platform, string How)? FindFrontEndPlatform(string? launchBoxPlatform, IReadOnlyDictionary<string, string>? overrides)
    {
        if (string.IsNullOrWhiteSpace(launchBoxPlatform)) return null;
        var name = launchBoxPlatform.Trim();
        if (overrides is null || !overrides.ContainsKey(name))
        {
            using var c = Connect();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT platform, COUNT(*) n FROM launchbox_games WHERE lb_platform = $p GROUP BY platform ORDER BY n DESC LIMIT 1";
            cmd.Parameters.AddWithValue("$p", name);
            using var r = cmd.ExecuteReader();
            if (r.Read()) return (r.GetString(0), "LaunchBox games");
        }
        var keys = GetPlatformSummaries().Select(p => p.Key).ToList();
        var (key, how) = Core.Import.LaunchBox.PlatformMatcher.Match(name, keys, overrides);
        return keys.Contains(key, StringComparer.OrdinalIgnoreCase) ? (key, how) : null;
    }

    /// <summary>
    /// The emulator program LaunchBox runs a game with, and its rom name: the LaunchBox game with this id, else the
    /// LaunchBox games matched to this LED-Next game (the most common emulator among them). Null when LaunchBox has none.
    /// </summary>
    public (string Emulator, string Rom, string AppPath)? LaunchBoxEmulator(string? launchBoxId, string platform, string? game)
    {
        if (string.IsNullOrWhiteSpace(game) && string.IsNullOrWhiteSpace(launchBoxId)) return null;
        using var c = Connect();
        using var cmd = c.CreateCommand();
        if (!string.IsNullOrWhiteSpace(launchBoxId))
        {
            cmd.CommandText = "SELECT emulator, rom, app_path FROM launchbox_games WHERE id = $id LIMIT 1";
            cmd.Parameters.AddWithValue("$id", launchBoxId.Trim());
            using var r = cmd.ExecuteReader();
            if (r.Read()) return (r.GetString(0), r.GetString(1), r.GetString(2));
        }
        if (string.IsNullOrWhiteSpace(game)) return null;
        cmd.Parameters.Clear();
        cmd.CommandText = """
            SELECT emulator, MAX(rom), MAX(app_path) FROM launchbox_games WHERE platform = $p AND game = $g
            GROUP BY emulator ORDER BY COUNT(*) DESC, emulator LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$p", platform);
        cmd.Parameters.AddWithValue("$g", game);
        using (var r = cmd.ExecuteReader())
            if (r.Read()) return (r.GetString(0), r.GetString(1), r.GetString(2));
        return null;
    }

    /// <summary>Per platform: description (normalised) → game name, for matching LaunchBox titles.</summary>
    /// <remarks>
    /// Only imported (LEDBlinky) games: a game added from LaunchBox earlier is not a match target, or an old
    /// wrong name (a game named "mame" after its emulator, before that was fixed) would be found again by title.
    /// </remarks>
    private Dictionary<string, IReadOnlyDictionary<string, string>> TitleIndex()
    {
        var index = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT platform, name, description FROM games WHERE parent IS NULL AND sources <> 'LaunchBox'";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (!index.TryGetValue(r.GetString(0), out var d)) index[r.GetString(0)] = d = new(StringComparer.Ordinal);
            d.TryAdd(Core.Import.LaunchBox.PlatformMatcher.Normalize(r.GetString(2)), r.GetString(1));
        }
        return index.ToDictionary(k => k.Key, k => (IReadOnlyDictionary<string, string>)k.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds LaunchBox games (and platforms) the library doesn't have, so they can be found, filtered and set up.
    /// Runs after a LaunchBox read and after every LEDBlinky import (which replaces the games table).
    /// </summary>
    private void MergeLaunchBoxGames()
    {
        UseLightsFromAnotherEmulator();
        RemoveStaleLaunchBoxGames();
        UseMameDataForArcadePlatforms();
        AddMissingLaunchBoxGames();
        FillFromMameData();
    }

    /// <summary>
    /// A LaunchBox game whose own entry has no lights, while LEDBlinkyControls.xml has coloured lights for
    /// the same ROM name and title under another emulator (a Model 2 or MAME game run through TeknoParrot,
    /// an Atari vector game set up under AAE), is lit from that entry instead. Customized games, and
    /// MAME-data platforms (already lit from MAME), are left alone.
    /// </summary>
    private void UseLightsFromAnotherEmulator()
    {
        var candidates = new List<(long RowId, string Title, string Platform, string Game, string OtherPlatform, string OtherName, string OtherDescription)>();
        using (var c = Connect())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = """
                SELECT lb.rowid, lb.title, lb.platform, lb.game, o.platform, o.name, o.description
                FROM launchbox_games lb
                JOIN games o ON o.name = lb.rom AND o.platform <> lb.platform
                WHERE lb.rom <> ''
                  -- only a game set up in LEDBlinkyControls.xml (not MAME's general controls data)
                  AND o.sources LIKE '%LEDBlinkyControls.xml%'
                  AND EXISTS(SELECT 1 FROM lights l WHERE l.platform = o.platform AND l.game = o.name AND l.color IS NOT NULL)
                  -- a platform that uses MAME's data already lights its games from MAME
                  AND NOT (o.platform = 'MAME' AND EXISTS(SELECT 1 FROM platforms p WHERE p.key = lb.platform AND p.uses_mame = 1))
                  AND NOT EXISTS(SELECT 1 FROM lights l WHERE l.platform = lb.platform AND l.game = lb.game)
                  AND NOT EXISTS(SELECT 1 FROM user_profiles u WHERE u.platform = lb.platform AND u.game = lb.game)
                ORDER BY lb.rowid, o.platform
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                candidates.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6)));
        }
        // One entry per LaunchBox game, and only when the titles agree (the ROM name alone can collide).
        var moves = candidates
            .Where(m => Core.Import.LaunchBox.GameMatcher.SameTitle(m.Title, m.OtherDescription))
            .GroupBy(m => m.RowId)
            .Where(g => g.Count() == 1)
            .Select(g => g.First())
            .ToList();
        if (moves.Count == 0) return;

        using (var c = Connect())
        using (var tx = c.BeginTransaction())
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE launchbox_games SET platform = $p, game = $g, matched_by = $m WHERE rowid = $id";
            var p = cmd.Parameters.Add("$p", SqliteType.Text);
            var g = cmd.Parameters.Add("$g", SqliteType.Text);
            var m = cmd.Parameters.Add("$m", SqliteType.Text);
            var id = cmd.Parameters.Add("$id", SqliteType.Integer);
            foreach (var move in moves)
            {
                p.Value = move.OtherPlatform; g.Value = move.OtherName; m.Value = $"rom name on {move.OtherPlatform}"; id.Value = move.RowId;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        foreach (var move in moves.Take(10))
            AppLog.Info($"LaunchBox '{move.Title}': no lights on {move.Platform}/{move.Game}; lit from {move.OtherPlatform}/{move.OtherName}.");
        AppLog.Info($"{moves.Count:N0} LaunchBox games are lit from the same game under another emulator (LEDBlinky has their lights there).");
    }

    /// <summary>
    /// Removes games added from LaunchBox that no LaunchBox game points at any more (removed from LaunchBox,
    /// or matched differently now), unless you have customized them.
    /// </summary>
    private void RemoveStaleLaunchBoxGames()
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            DELETE FROM games
            WHERE sources = 'LaunchBox'
              AND NOT EXISTS(SELECT 1 FROM launchbox_games lb WHERE lb.platform = games.platform AND lb.game = games.name)
              AND NOT EXISTS(SELECT 1 FROM user_profiles u WHERE u.platform = games.platform AND u.game = games.name)
            """;
        int n = cmd.ExecuteNonQuery();
        if (n > 0) AppLog.Info($"Removed {n:N0} games added from LaunchBox earlier that no LaunchBox game uses now.");
    }

    /// <summary>
    /// LaunchBox-only games lit from MAME's data (Neo Geo MVS, Arcade Clones, Cave…) take MAME's button
    /// count, control types and players, so the button/control filters and smart lists find them.
    /// </summary>
    private void FillFromMameData()
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE games SET buttons = m.buttons, tags = m.tags,
                   players = CASE WHEN games.players = 0 THEN m.players ELSE games.players END
            FROM games m
            WHERE games.sources = 'LaunchBox' AND games.lighting > 0
              AND m.platform = 'MAME' AND m.name = games.name AND games.platform <> 'MAME'
              AND games.platform IN (SELECT key FROM platforms WHERE uses_mame = 1)
              AND (games.buttons <> m.buttons OR games.tags <> m.tags OR (games.players = 0 AND m.players > 0))
            """;
        int n = cmd.ExecuteNonQuery();
        if (n > 0) AppLog.Info($"{n:N0} LaunchBox games took their button count and control types from MAME's data.");
    }

    private void AddMissingLaunchBoxGames()
    {
        var missing = new List<(string LbPlatform, string Platform, string Game, string Title)>();
        using (var c = Connect())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = """
                SELECT lb.lb_platform, lb.platform, lb.game, MIN(lb.title) FROM launchbox_games lb
                WHERE NOT EXISTS(SELECT 1 FROM games g WHERE g.platform = lb.platform AND g.name = lb.game)
                GROUP BY lb.platform, lb.game
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read()) missing.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        }
        if (missing.Count == 0) return;

        using (var c = Connect())
        using (var tx = c.BeginTransaction())
        {
            bool hasMame = GetPlatform("MAME") is not null;
            using var plat = c.CreateCommand();
            plat.Transaction = tx;
            plat.CommandText = "INSERT OR IGNORE INTO platforms VALUES($k, $n, $p, $m, '#FFFFFF')";
            var k = plat.Parameters.Add("$k", SqliteType.Text);
            var n = plat.Parameters.Add("$n", SqliteType.Text);
            var pp = plat.Parameters.Add("$p", SqliteType.Text);
            var um = plat.Parameters.Add("$m", SqliteType.Integer);
            foreach (var p in missing.GroupBy(m => m.Platform, StringComparer.OrdinalIgnoreCase))
            {
                k.Value = p.Key; n.Value = p.First().LbPlatform; pp.Value = hasMame && !p.Key.Equals("MAME", StringComparison.OrdinalIgnoreCase) ? "MAME" : DBNull.Value;
                um.Value = hasMame && Core.Import.LaunchBox.PlatformMatcher.UsesMameDataByDefault(p.First().LbPlatform) ? 1 : 0;
                plat.ExecuteNonQuery();
            }
            tx.Commit();
        }
        lock (_cacheLock) _platforms = null;

        using (var c = Connect())
        using (var tx = c.BeginTransaction())
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR IGNORE INTO games(platform, name, description, parent, players, info, sources, lighting, buttons, tags)
                VALUES($p, $n, $d, NULL, 0, '', 'LaunchBox', $l, 0, '')
                """;
            var p = cmd.Parameters.Add("$p", SqliteType.Text);
            var nm = cmd.Parameters.Add("$n", SqliteType.Text);
            var d = cmd.Parameters.Add("$d", SqliteType.Text);
            var l = cmd.Parameters.Add("$l", SqliteType.Integer);
            var empty = new ControlMap();
            var panel = new PanelLayout();
            foreach (var m in missing)
            {
                // A MAME-based platform can still have game data (MAME's) for a game it doesn't list itself.
                bool lit = GameLightResolver.Resolve(this, m.Platform, m.Game, empty, panel).GameSpecific;
                p.Value = m.Platform; nm.Value = m.Game; d.Value = m.Title; l.Value = lit ? (int)GameLighting.Parent : 0;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        AppLog.Info($"Added {missing.Count:N0} LaunchBox games that are not in the LEDBlinky data.");
    }

    /// <summary>
    /// Platforms made from a LaunchBox arcade platform with MAME rom names (Cave, ZiNc…) use MAME's game
    /// data. Also upgrades such platforms created before this rule (they were made with uses_mame 0) and
    /// recomputes their LaunchBox-only games' lighting.
    /// </summary>
    private void UseMameDataForArcadePlatforms()
    {
        if (GetPlatform("MAME") is null) return;
        var keys = new List<string>();
        using (var c = Connect())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = """
                SELECT DISTINCT lb.lb_platform, lb.platform FROM launchbox_games lb
                JOIN platforms p ON p.key = lb.platform
                WHERE lb.platform_how <> 'your mapping' AND p.uses_mame = 0
                  -- made from LaunchBox: no LEDBlinky games or lights of its own
                  AND NOT EXISTS(SELECT 1 FROM games g WHERE g.platform = p.key AND g.sources <> 'LaunchBox')
                  AND NOT EXISTS(SELECT 1 FROM lights l WHERE l.platform = p.key)
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (Core.Import.LaunchBox.PlatformMatcher.UsesMameDataByDefault(r.GetString(0))) keys.Add(r.GetString(1));
        }
        if (keys.Count == 0) return;

        var games = new List<(string Platform, string Game)>();
        using (var c = Connect())
        using (var tx = c.BeginTransaction())
        {
            foreach (var key in keys)
            {
                using var up = c.CreateCommand();
                up.Transaction = tx;
                up.CommandText = "UPDATE platforms SET uses_mame = 1 WHERE key = $k";
                up.Parameters.AddWithValue("$k", key);
                up.ExecuteNonQuery();
                using var sel = c.CreateCommand();
                sel.Transaction = tx;
                sel.CommandText = "SELECT name FROM games WHERE platform = $k AND sources = 'LaunchBox'";
                sel.Parameters.AddWithValue("$k", key);
                using var r = sel.ExecuteReader();
                while (r.Read()) games.Add((key, r.GetString(0)));
            }
            tx.Commit();
        }
        lock (_cacheLock) { _platforms = null; _lightCache.Clear(); }

        int lit = 0;
        using (var c = Connect())
        using (var tx = c.BeginTransaction())
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE games SET lighting = $l WHERE platform = $p AND name = $n";
            var l = cmd.Parameters.Add("$l", SqliteType.Integer);
            var p = cmd.Parameters.Add("$p", SqliteType.Text);
            var n = cmd.Parameters.Add("$n", SqliteType.Text);
            var empty = new ControlMap();
            var panel = new PanelLayout();
            foreach (var g in games)
            {
                bool gameLit = GameLightResolver.Resolve(this, g.Platform, g.Game, empty, panel).GameSpecific;
                if (gameLit) lit++;
                l.Value = gameLit ? (int)GameLighting.Parent : 0; p.Value = g.Platform; n.Value = g.Game;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        AppLog.Info($"Platforms {string.Join(", ", keys)} now use MAME's game data: {lit:N0} of {games.Count:N0} games lit from it.");
    }

    public List<LaunchBoxPlatformSummary> LaunchBoxSummaries()
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT lb_platform, platform, MIN(platform_how), COUNT(*), SUM(matched_by = 'rom name'), SUM(matched_by = 'title'), SUM(matched_by = 'new')
            FROM launchbox_games GROUP BY lb_platform ORDER BY lb_platform COLLATE NOCASE
            """;
        using var r = cmd.ExecuteReader();
        var list = new List<LaunchBoxPlatformSummary>();
        while (r.Read())
            list.Add(new LaunchBoxPlatformSummary(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6)));
        return list;
    }

    // ---- Templates ------------------------------------------------------------------------------

    public List<LightTemplate> ListTemplates()
    {
        using var c = Connect();
        var heads = new List<(string Name, string Description)>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT name, description FROM templates ORDER BY name COLLATE NOCASE";
            using var r = cmd.ExecuteReader();
            while (r.Read()) heads.Add((r.GetString(0), r.GetString(1)));
        }
        return heads.Select(h => new LightTemplate(h.Name, ReadTemplateLights(c, h.Name), h.Description)).ToList();
    }

    public LightTemplate? GetTemplate(string name) => ListTemplates().FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    private static LightProfile ReadTemplateLights(SqliteConnection c, string name)
    {
        var profile = new LightProfile { Source = "LED-Next", Customized = true };
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT control, color, color_name, label, always_active FROM template_lights WHERE name = $n ORDER BY seq";
        cmd.Parameters.AddWithValue("$n", name);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            profile.Controls.Add(new ControlLight(r.GetString(0), r.IsDBNull(1) ? null : Rgb.ParseHex(r.GetString(1)),
                r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetInt32(4) != 0));
        return profile;
    }

    /// <summary>Saves (or replaces) a template by name.</summary>
    public void SaveTemplate(LightTemplate template)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        DeleteTemplate(c, tx, template.Name);
        using (var head = c.CreateCommand())
        {
            head.Transaction = tx;
            head.CommandText = "INSERT INTO templates VALUES($n, $d, $t)";
            head.Parameters.AddWithValue("$n", template.Name);
            head.Parameters.AddWithValue("$d", template.Description);
            head.Parameters.AddWithValue("$t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            head.ExecuteNonQuery();
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO template_lights VALUES($n, $seq, $c, $col, $cn, $lab, $a)";
            cmd.Parameters.AddWithValue("$n", template.Name);
            var seq = cmd.Parameters.Add("$seq", SqliteType.Integer);
            var ctl = cmd.Parameters.Add("$c", SqliteType.Text);
            var col = cmd.Parameters.Add("$col", SqliteType.Text);
            var cn = cmd.Parameters.Add("$cn", SqliteType.Text);
            var lab = cmd.Parameters.Add("$lab", SqliteType.Text);
            var a = cmd.Parameters.Add("$a", SqliteType.Integer);
            for (int i = 0; i < template.Profile.Controls.Count; i++)
            {
                var cl = template.Profile.Controls[i];
                seq.Value = i; ctl.Value = cl.Control;
                col.Value = cl.Color is { } rgb ? rgb.ToHex() : DBNull.Value;
                cn.Value = (object?)cl.ColorName ?? DBNull.Value;
                lab.Value = DBNull.Value; // labels are game-specific; templates carry colours only
                a.Value = cl.AlwaysActive ? 1 : 0;
                cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    public void DeleteTemplate(string name)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        DeleteTemplate(c, tx, name);
        tx.Commit();
    }

    private static void DeleteTemplate(SqliteConnection c, SqliteTransaction tx, string name)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM template_lights WHERE name = $n; DELETE FROM templates WHERE name = $n;";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.ExecuteNonQuery();
    }

    // ---- Smart lists ----------------------------------------------------------------------------

    public List<(string Name, GameFilter Filter)> ListSmartLists()
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT name, filter FROM smart_lists ORDER BY seq, name COLLATE NOCASE";
        using var r = cmd.ExecuteReader();
        var list = new List<(string, GameFilter)>();
        while (r.Read())
        {
            try { list.Add((r.GetString(0), System.Text.Json.JsonSerializer.Deserialize<GameFilter>(r.GetString(1), ConfigStore.JsonOptions)!)); }
            catch (System.Text.Json.JsonException ex) { AppLog.Warn($"Smart list '{r.GetString(0)}' could not be read: {ex.Message}"); }
        }
        return list;
    }

    /// <summary>Saves (or replaces) a smart list. New lists go to the end unless <paramref name="seq"/> is given.</summary>
    public void SaveSmartList(string name, GameFilter filter, int? seq = null)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO smart_lists(name, filter, seq)
            VALUES($n, $f, COALESCE($s, (SELECT COUNT(*) FROM smart_lists s WHERE s.name <> $n)))
            ON CONFLICT(name) DO UPDATE SET filter = excluded.filter
            """;
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$f", System.Text.Json.JsonSerializer.Serialize(filter, ConfigStore.JsonOptions));
        cmd.Parameters.AddWithValue("$s", (object?)seq ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void DeleteSmartList(string name)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM smart_lists WHERE name = $n";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.ExecuteNonQuery();
    }

    // ---- User edits -----------------------------------------------------------------------------

    /// <summary>
    /// Stores the user's lights for a game (or the platform default when <paramref name="game"/> is
    /// null). Replaces any earlier edit; imported data is kept and comes back on <see cref="RevertUserLights"/>.
    /// </summary>
    public void SaveUserLights(string platform, string? game, LightProfile profile) =>
        SaveUserLightsMany(new[] { (platform, game, profile) });

    /// <summary>Stores many edits in one transaction (bulk paste): all of them or none.</summary>
    public void SaveUserLightsMany(IEnumerable<(string Platform, string? Game, LightProfile Profile)> edits)
    {
        var list = edits.ToList();
        using var c = Connect();
        using var tx = c.BeginTransaction();
        foreach (var (platform, game, profile) in list) WriteUser(c, tx, platform, game, profile);
        tx.Commit();
        foreach (var (platform, game, _) in list) Invalidate(platform, game);
    }

    /// <summary>Reverts many games in one transaction.</summary>
    public void RevertUserLightsMany(IEnumerable<(string Platform, string? Game)> keys)
    {
        var list = keys.ToList();
        using var c = Connect();
        using var tx = c.BeginTransaction();
        foreach (var (platform, game) in list) DeleteUser(c, tx, platform, game);
        tx.Commit();
        foreach (var (platform, game) in list) Invalidate(platform, game);
    }

    private static void WriteUser(SqliteConnection c, SqliteTransaction tx, string platform, string? game, LightProfile profile)
    {
        DeleteUser(c, tx, platform, game);
        using (var head = c.CreateCommand())
        {
            head.Transaction = tx;
            head.CommandText = "INSERT INTO user_profiles VALUES($p, $g, $t)";
            head.Parameters.AddWithValue("$p", platform);
            head.Parameters.AddWithValue("$g", game ?? "");
            head.Parameters.AddWithValue("$t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            head.ExecuteNonQuery();
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO user_lights VALUES($p, $g, $seq, $c, $col, $cn, $lab, $a)";
            cmd.Parameters.AddWithValue("$p", platform);
            cmd.Parameters.AddWithValue("$g", game ?? "");
            var seq = cmd.Parameters.Add("$seq", SqliteType.Integer);
            var ctl = cmd.Parameters.Add("$c", SqliteType.Text);
            var col = cmd.Parameters.Add("$col", SqliteType.Text);
            var cn = cmd.Parameters.Add("$cn", SqliteType.Text);
            var lab = cmd.Parameters.Add("$lab", SqliteType.Text);
            var a = cmd.Parameters.Add("$a", SqliteType.Integer);
            for (int i = 0; i < profile.Controls.Count; i++)
            {
                var cl = profile.Controls[i];
                seq.Value = i; ctl.Value = cl.Control;
                col.Value = cl.Color is { } rgb ? rgb.ToHex() : DBNull.Value;
                cn.Value = (object?)cl.ColorName ?? DBNull.Value;
                lab.Value = (object?)cl.Label ?? DBNull.Value;
                a.Value = cl.AlwaysActive ? 1 : 0;
                cmd.ExecuteNonQuery();
            }
        }
    }

    /// <summary>Drops the user's edit, so the imported lights (or inheritance) apply again.</summary>
    public void RevertUserLights(string platform, string? game) => RevertUserLightsMany(new[] { (platform, game) });

    public bool IsCustomized(string platform, string? game) => GetLights(platform, game)?.Customized == true;

    public int CustomizedCount()
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM user_profiles";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void DeleteUser(SqliteConnection c, SqliteTransaction tx, string platform, string? game)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM user_lights WHERE platform = $p AND game = $g; DELETE FROM user_profiles WHERE platform = $p AND game = $g;";
        cmd.Parameters.AddWithValue("$p", platform);
        cmd.Parameters.AddWithValue("$g", game ?? "");
        cmd.ExecuteNonQuery();
    }

    private void Invalidate(string platform, string? game)
    {
        lock (_cacheLock) _lightCache.Remove(InMemoryGameLibrary.Key(platform, game));
    }
}
