using System.Text.Json;
using System.Text.Json.Serialization;

namespace LedNext.FrontEnd;

/// <summary>What a front end reports. One JSON object per line on the pipe.</summary>
public static class FrontEndEvents
{
    /// <summary>A game is highlighted in the front end (browsing).</summary>
    public const string Select = "select";
    /// <summary>A game is starting.</summary>
    public const string Launch = "launch";
    /// <summary>The game that was running has exited.</summary>
    public const string Exit = "exit";
    /// <summary>The front end finished starting.</summary>
    public const string Started = "started";
    /// <summary>The front end is shutting down.</summary>
    public const string Closing = "closing";
    /// <summary>Bring the LED-Next window to the front (sent by a second LED-Next start).</summary>
    public const string Show = "show";
}

/// <param name="Event">One of <see cref="FrontEndEvents"/>.</param>
/// <param name="Source">"LaunchBox", "BigBox", "LED-Next"…</param>
/// <param name="GameId">The front end's own game id (LaunchBox's GUID), the most reliable match.</param>
/// <param name="Platform">The front end's platform name ("Arcade", "Sega Genesis").</param>
/// <param name="ApplicationPath">The game's rom / launch path.</param>
public sealed record FrontEndMessage(
    string Event,
    string? Source = null,
    string? GameId = null,
    string? Title = null,
    string? Platform = null,
    string? ApplicationPath = null)
{
    public bool HasGame => !string.IsNullOrWhiteSpace(GameId) || !string.IsNullOrWhiteSpace(Title);

    public override string ToString() =>
        HasGame ? $"{Source ?? "?"} {Event}: {Title} ({Platform})" : $"{Source ?? "?"} {Event}";
}

public static class FrontEndProtocol
{
    /// <summary>Per user session (named pipes are local to the machine).</summary>
    public const string PipeName = "LedNext.FrontEnd";

    /// <summary>Longest line accepted; anything longer is not one of our messages.</summary>
    public const int MaxLineLength = 16 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>A single line (no newline inside: JSON escapes them).</summary>
    public static string Serialize(FrontEndMessage message) => JsonSerializer.Serialize(message, Json);

    /// <summary>The message, or null for an empty, too long or malformed line.</summary>
    public static FrontEndMessage? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaxLineLength) return null;
        try
        {
            var m = JsonSerializer.Deserialize<FrontEndMessage>(line, Json);
            return m is { Event.Length: > 0 } ? m : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
