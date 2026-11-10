using System.Text.Json;
using System.Text.Json.Serialization;

namespace LedNext.Core;

public static class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Loads the config, or creates the default 4-player config if the file is missing.</summary>
    public static CabinetConfig LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            AppLog.Info($"No config at {path}; creating the default 4-player panel.");
            var created = Defaults.CreateFourPlayerCabinet();
            Save(path, created);
            return created;
        }

        var config = JsonSerializer.Deserialize<CabinetConfig>(File.ReadAllText(path), JsonOptions)
                     ?? throw new InvalidDataException($"Config file is empty: {path}");
        if (config.Version > CabinetConfig.CurrentVersion)
            AppLog.Warn($"Config version {config.Version} is newer than this app ({CabinetConfig.CurrentVersion}). Some settings may be ignored.");
        else
            Migrate(config);
        return config;
    }

    /// <summary>Upgrades an older config in place. It is written back on the next save.</summary>
    public static void Migrate(CabinetConfig config)
    {
        if (config.Version < 2)
        {
            // v2 added ControlMap and Games. Missing in a v1 file, so they keep their empty defaults;
            // the null checks cover an explicit "null" in the JSON.
            config.ControlMap ??= new();
            config.Games ??= new();
            AppLog.Info($"Config upgraded from version {config.Version} to 2 (adds the game control map).");
            config.Version = 2;
        }
    }

    /// <summary>Writes to a temp file and swaps it in, so a crash never leaves a half-written config.</summary>
    public static void Save(string path, CabinetConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    public static CabinetConfig Clone(CabinetConfig config) =>
        JsonSerializer.Deserialize<CabinetConfig>(JsonSerializer.Serialize(config, JsonOptions), JsonOptions)!;
}
