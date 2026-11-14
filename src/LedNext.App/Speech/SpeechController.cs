using System.Diagnostics;
using System.IO;
using System.Speech.Synthesis;
using System.Windows.Threading;
using LedNext.Core;
using LedNext.Core.Import;
using LedNext.Core.Lighting;
using LedNext.Core.Speech;

namespace LedNext.App.Speech;

/// <summary>
/// Reads a game's button labels aloud with a Windows voice (System.Speech, the same SAPI voices LEDBlinky uses) and
/// blinks each label's buttons while it is said (<see cref="SpeechHighlight"/>, the engine's top layer, so the lamp
/// lifecycle is untouched). Starts <see cref="SpeechSettings.DelaySeconds"/> after a front-end launch, again on
/// MAME's pause; stops on the front end's exit, the game's end, resume, or (with <see cref="SpeechSettings.StopOnInput"/>)
/// any control press. UI thread only. Never throws: failures are logged and speech stays off.
/// </summary>
public sealed class SpeechController : IDisposable
{
    /// <summary>Input this soon after speech starts is ignored (the button that launched the game is still being released).</summary>
    private static readonly TimeSpan InputGrace = TimeSpan.FromMilliseconds(600);

    private readonly AppSession _session;
    private readonly SpeechHighlight _highlight = new();
    private readonly RawInputWatcher _input = new();
    private readonly DispatcherTimer _delay;
    private readonly Stopwatch _since = new();
    private SpeechSynthesizer? _synth;
    private bool _synthFailed;
    private Prompt? _current;
    /// <summary>Set while one of Windows' newer voices speaks (<see cref="OneCoreSpeech"/>); cancelling stops it.</summary>
    private CancellationTokenSource? _oneCore;
    private List<SpeechItem> _items = new();
    private List<SpeechItem>? _pending;

    public SpeechController(AppSession session, Dispatcher dispatcher)
    {
        _session = session;
        _delay = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        _delay.Tick += (_, _) =>
        {
            _delay.Stop();
            if (_pending is { } items) { _pending = null; Speak(items, "launch"); }
        };
        _input.Input += what =>
        {
            if (!Talking || _since.Elapsed < InputGrace) return;
            Stop($"{what} was pressed");
        };
    }

    private SpeechSettings Settings => _session.Config.Speech;

    /// <summary>A voice is talking now (not just waiting for the delay).</summary>
    private bool Talking => _current is not null || _oneCore is not null;
    public bool Speaking => Talking || _pending is not null;

    /// <summary>
    /// The installed Windows voices, for the settings: Windows' newer voices first ("Microsoft Mark", clearer), then
    /// the old SAPI ones ("Microsoft David Desktop", the ones LEDBlinky uses). The names never clash.
    /// </summary>
    public static IReadOnlyList<string> Voices()
    {
        var voices = OneCoreSpeech.Voices().ToList();
        try
        {
            using var s = new SpeechSynthesizer();
            voices.AddRange(s.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Speech: could not list the Windows voices ({ex.Message}).");
        }
        return voices;
    }

    /// <summary>The front end launched a game: say its labels after the delay.</summary>
    public void GameLaunched(ILightSource lights, string? title)
    {
        Stop(null);
        if (!Settings.Enabled) return;
        var items = PlanFor(lights, title, withName: true);
        if (items is null) return;
        _pending = items;
        _delay.Interval = TimeSpan.FromSeconds(Math.Clamp(Settings.DelaySeconds, 0, 30));
        _delay.Start();
    }

    /// <summary>MAME paused or resumed the running game. <paramref name="lights"/> is the game's lights.</summary>
    public void Paused(ILightSource? lights, bool paused)
    {
        if (!paused) { Stop("the game resumed"); return; }
        if (!Settings.Enabled || !Settings.SpeakOnPause || lights is null) return;
        if (PlanFor(lights, null, withName: false) is { } items) Speak(items, "pause");
    }

    /// <summary>Says the labels of what the panel shows now (the settings' Test button).</summary>
    public string Test()
    {
        var source = _session.Engine.Source;
        var lights = source is Core.Outputs.LampOverlay o ? o.Base : source;
        if (lights is not GameLights g) return "Show a game first (select it in the Games tab).";
        var items = SpeechPlan.For(g.Lights, Settings, g.Lights.Game);
        if (items.Count == 0) return $"{g.Name}: no labelled buttons to say.";
        Stop(null);
        Speak(items, "test");
        return $"Saying {items.Count} item(s) for {g.Name}.";
    }

    private List<SpeechItem>? PlanFor(ILightSource lights, string? title, bool withName)
    {
        if (lights is Core.Outputs.LampOverlay o) lights = o.Base;
        if (lights is not GameLights g)
        {
            AppLog.Info($"Speech: '{lights.Name}' isn't a game's lights; nothing to say.");
            return null;
        }
        var items = SpeechPlan.For(g.Lights, Settings, withName ? title ?? g.Lights.Game : null);
        if (items.Count == 0)
        {
            AppLog.Info($"Speech: {g.Name} has no labelled buttons; nothing to say.");
            return null;
        }
        return items;
    }

    private void Speak(List<SpeechItem> items, string why)
    {
        if (OneCoreSpeech.Find(Settings.Voice) is { } newer) { SpeakOneCore(newer, items, why); return; }
        var synth = Synth();
        if (synth is null) return;
        Cancel();
        var pb = new PromptBuilder();
        for (int i = 0; i < items.Count; i++)
        {
            pb.AppendBookmark(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            pb.AppendText(items[i].Text);
            if (items[i].PauseAfter > TimeSpan.Zero) pb.AppendBreak(items[i].PauseAfter);
            else pb.AppendBreak(PromptBreak.Small);
        }
        try
        {
            synth.Rate = Settings.ClampedRate;
            synth.Volume = Settings.ClampedVolume;
            SelectVoice(synth);
            _items = items;
            _session.Engine.Highlight = Settings.BlinkButtons ? _highlight : null;
            if (Settings.StopOnInput) _input.Begin();
            _since.Restart();
            AppLog.Info($"Speech ({why}): {string.Join(" · ", items.Select(i => i.Buttons.Count == 0 ? i.Text : $"{i.Text} [{string.Join(",", i.Buttons)}]"))}");
            _current = synth.SpeakAsync(pb);
        }
        catch (Exception ex)
        {
            AppLog.Error("Speech: could not speak", ex);
            Finish();
        }
    }

    /// <summary>Says the items with one of Windows' newer voices. Never throws (async void on the UI thread).</summary>
    private async void SpeakOneCore(Windows.Media.SpeechSynthesis.VoiceInformation voice, List<SpeechItem> items, string why)
    {
        Cancel();
        var run = new CancellationTokenSource();
        _oneCore = run;
        try
        {
            _items = items;
            _session.Engine.Highlight = Settings.BlinkButtons ? _highlight : null;
            if (Settings.StopOnInput) _input.Begin();
            _since.Restart();
            AppLog.Info($"Speech ({why}, {voice.DisplayName}): {string.Join(" · ", items.Select(i => i.Buttons.Count == 0 ? i.Text : $"{i.Text} [{string.Join(",", i.Buttons)}]"))}");
            await OneCoreSpeech.SpeakAsync(voice, items, Settings.ClampedRate, Settings.ClampedVolume,
                i => { if (_oneCore == run) _highlight.Blink(items[i].Buttons); }, run.Token);
            if (_oneCore == run) AppLog.Info($"Speech: done ({_since.Elapsed.TotalSeconds:0.0} s).");
        }
        catch (OperationCanceledException) { } // stopped: Stop/Cancel already cleaned up
        catch (Exception ex) { AppLog.Error($"Speech: {voice.DisplayName} could not speak", ex); }
        finally
        {
            if (_oneCore == run) { _oneCore = null; Finish(); }
            run.Dispose();
        }
    }

    /// <summary>Stops speaking and blinking. <paramref name="why"/> is logged when something was being said.</summary>
    public void Stop(string? why)
    {
        bool was = Speaking;
        _delay.Stop();
        _pending = null;
        Cancel();
        if (was && why is not null) AppLog.Info($"Speech: stopped ({why}).");
    }

    private void Cancel()
    {
        if (_current is not null)
        {
            var c = _current;
            _current = null; // its SpeakCompleted is then ignored
            try { _synth?.SpeakAsyncCancel(c); }
            catch (Exception ex) { AppLog.Error("Speech: could not stop", ex); }
        }
        if (_oneCore is not null)
        {
            var run = _oneCore;
            _oneCore = null; // its SpeakOneCore then skips its own clean-up
            run.Cancel();
        }
        Finish();
    }

    private void Finish()
    {
        _highlight.Clear();
        _session.Engine.Highlight = null;
        _input.End();
    }

    private SpeechSynthesizer? Synth()
    {
        if (_synth is not null || _synthFailed) return _synth;
        try
        {
            var s = new SpeechSynthesizer();
            s.SetOutputToDefaultAudioDevice();
            s.BookmarkReached += (_, e) =>
            {
                if (_current is null || !ReferenceEquals(e.Prompt, _current)) return;
                if (int.TryParse(e.Bookmark, out var i) && i >= 0 && i < _items.Count) _highlight.Blink(_items[i].Buttons);
            };
            s.SpeakCompleted += (_, e) =>
            {
                if (_current is null || !ReferenceEquals(e.Prompt, _current)) return;
                _current = null;
                if (e.Error is { } err) AppLog.Warn($"Speech: {err.Message}");
                else AppLog.Info($"Speech: done ({_since.Elapsed.TotalSeconds:0.0} s).");
                Finish();
            };
            _synth = s;
        }
        catch (Exception ex)
        {
            _synthFailed = true;
            AppLog.Error("Speech: Windows speech isn't available", ex);
        }
        return _synth;
    }

    private string? _voiceSet;
    private void SelectVoice(SpeechSynthesizer synth)
    {
        var want = Settings.Voice;
        if (string.IsNullOrWhiteSpace(want) || want == _voiceSet) return;
        try { synth.SelectVoice(want); _voiceSet = want; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            AppLog.Warn($"Speech: voice '{want}' isn't installed; using Windows' default voice.");
            _voiceSet = want;
        }
    }

    /// <summary>LEDBlinky's speech settings (settings.ini in the LEDBlinky folder, read only). Returns what happened.</summary>
    public string CopyFromLedBlinky()
    {
        var folder = _session.Config.Games.LedBlinkyFolder;
        var path = string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, "settings.ini");
        if (path is null || !File.Exists(path)) return "No LEDBlinky settings.ini found (import from LEDBlinky first, in the Games tab).";
        try
        {
            _session.Config.Speech = SpeechSettings.FromLedBlinky(IniFile.Load(path), Settings);
            _session.MarkDirty();
            AppLog.Info($"Speech: settings copied from {path}.");
            return $"Copied from {path}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not read {path}: {ex.Message}";
        }
    }

    public void Dispose()
    {
        Stop(null);
        _input.Dispose();
        try { _synth?.Dispose(); } catch { }
        _synth = null;
    }
}
