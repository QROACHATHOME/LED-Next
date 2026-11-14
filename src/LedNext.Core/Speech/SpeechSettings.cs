using System.Globalization;
using System.Text.Json.Serialization;
using LedNext.Core.Import;

namespace LedNext.Core.Speech;

/// <summary>
/// Speaking a game's button labels when the front end launches it, blinking each button while its label is
/// spoken (LEDBlinky's "speak game controls"). Added in config version 8; off by default.
/// </summary>
public sealed class SpeechSettings
{
    public bool Enabled { get; set; }
    /// <summary>A Windows voice name ("Microsoft David Desktop - English (United States)"), or null for the default.</summary>
    public string? Voice { get; set; }
    /// <summary>-10 (slow) … 10 (fast), as in Windows and LEDBlinky.</summary>
    public int Rate { get; set; }
    /// <summary>0 … 100.</summary>
    public int Volume { get; set; } = 100;
    /// <summary>Seconds to wait after the launch before speaking (the emulator is loading).</summary>
    public double DelaySeconds { get; set; } = 1.0;
    public bool SpeakGameName { get; set; } = true;
    /// <summary>
    /// Seconds of silence after the game's name, before the labels (config version 9). 0 = the voice's own short
    /// pause, as before.
    /// </summary>
    public double PauseAfterNameSeconds { get; set; }
    /// <summary>
    /// Say the game's name even when it has no labelled buttons (config version 10; on). Off: such games say nothing.
    /// Only with <see cref="SpeakGameName"/>.
    /// </summary>
    public bool SpeakNameWithoutLabels { get; set; } = true;
    /// <summary>Also speak Start and Coin ("Start Game", "Insert Coin").</summary>
    public bool SpeakStartCoin { get; set; }
    /// <summary>Also speak joystick and other non-button labels ("Move", "Aim").</summary>
    public bool SpeakJoysticks { get; set; }
    /// <summary>Blink the buttons while their label is spoken.</summary>
    public bool BlinkButtons { get; set; } = true;
    /// <summary>Speak the labels again when the game is paused (MAME reports pause through its outputs).</summary>
    public bool SpeakOnPause { get; set; } = true;
    /// <summary>A key press, a joystick move or a button press stops the speech (the user's idea; LEDBlinky's AbortSpeechAnyKey).</summary>
    public bool StopOnInput { get; set; } = true;
    /// <summary>Said before the controls ("Controls are"), or empty.</summary>
    public string? Prefix { get; set; }
    public string StartText { get; set; } = "Start Game";
    public string CoinText { get; set; } = "Insert Coin";

    [JsonIgnore] public int ClampedRate => Math.Clamp(Rate, -10, 10);
    [JsonIgnore] public int ClampedVolume => Math.Clamp(Volume, 0, 100);
    [JsonIgnore] public TimeSpan PauseAfterName => TimeSpan.FromSeconds(Math.Clamp(PauseAfterNameSeconds, 0, 10));

    /// <summary>
    /// LEDBlinky's speech settings from its settings.ini (read only). <see cref="StopOnInput"/> is left as it is:
    /// LEDBlinky's AbortSpeechAnyKey defaults to off, LED-Next's to on.
    /// </summary>
    public static SpeechSettings FromLedBlinky(IniFile ini, SpeechSettings? keep = null)
    {
        string? Get(string key)
        {
            foreach (var (_, entries) in ini.Sections)
                foreach (var (k, v) in entries)
                    if (string.Equals(k.Trim(), key, StringComparison.OrdinalIgnoreCase)) return v.Trim();
            return null;
        }
        bool Flag(string key, bool fallback) => Get(key) is { } v ? v == "1" : fallback;
        int Int(string key, int fallback) =>
            int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;

        var s = new SpeechSettings
        {
            StopOnInput = keep?.StopOnInput ?? true,
            DelaySeconds = keep?.DelaySeconds ?? 1.0,
            PauseAfterNameSeconds = keep?.PauseAfterNameSeconds ?? 0, // LEDBlinky has no such setting
            SpeakNameWithoutLabels = keep?.SpeakNameWithoutLabels ?? true, // nor this one
        };
        s.Enabled = Flag("GameStartSpeakControls", false);
        s.SpeakGameName = Flag("GameStartSpeakName", true);
        s.SpeakStartCoin = Flag("GameStartSpeakStartCoin", false);
        s.BlinkButtons = Flag("GameStartStrobeSpeech", true);
        s.SpeakOnPause = Flag("GamePauseSpeakControls", true);
        s.SpeakJoysticks = !Flag("DoNotSpeakJoystickActions", true);
        s.Rate = Int("SpeechRate", 0);
        s.Volume = Int("SpeechVolume", 100);
        s.Voice = string.IsNullOrWhiteSpace(Get("Voice")) ? null : Get("Voice");
        s.Prefix = string.IsNullOrWhiteSpace(Get("SpeakControlsPrefix")) ? null : Get("SpeakControlsPrefix");
        if (Get("SpeakStartButton") is { Length: > 0 } start) s.StartText = start;
        if (Get("SpeakCoinButton") is { Length: > 0 } coin) s.CoinText = coin;
        return s;
    }
}
