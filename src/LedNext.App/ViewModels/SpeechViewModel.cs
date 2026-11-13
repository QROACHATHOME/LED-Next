using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedNext.App.Speech;
using LedNext.Core.Speech;

namespace LedNext.App.ViewModels;

/// <summary>Speech tab: button labels read aloud when a game is launched. Edits cabinet.json's <c>speech</c>.</summary>
public sealed partial class SpeechViewModel : ObservableObject
{
    private readonly AppSession _session;
    private readonly SpeechController _speech;

    public SpeechViewModel(AppSession session, SpeechController speech)
    {
        _session = session;
        _speech = speech;
        var voices = new[] { DefaultVoice }.Concat(SpeechController.Voices()).ToList();
        if (!string.IsNullOrWhiteSpace(S.Voice) && !voices.Contains(S.Voice)) voices.Add(S.Voice); // e.g. copied from LEDBlinky, not installed
        Voices = voices;
    }

    public const string DefaultVoice = "(Windows default)";
    private SpeechSettings S => _session.Config.Speech;

    public IReadOnlyList<string> Voices { get; }
    [ObservableProperty] private string? _status;

    private void Changed(string? property = null)
    {
        _session.MarkDirty();
        OnPropertyChanged(property);
    }

    public bool Enabled { get => S.Enabled; set { S.Enabled = value; Changed(); } }
    public string Voice
    {
        get => string.IsNullOrWhiteSpace(S.Voice) ? DefaultVoice : S.Voice;
        set { S.Voice = value == DefaultVoice ? null : value; Changed(); }
    }
    public int Rate { get => S.Rate; set { S.Rate = Math.Clamp(value, -10, 10); Changed(); } }
    public int Volume { get => S.Volume; set { S.Volume = Math.Clamp(value, 0, 100); Changed(); } }
    public double DelaySeconds { get => S.DelaySeconds; set { S.DelaySeconds = Math.Clamp(Math.Round(value, 1), 0, 30); Changed(); } }
    public bool SpeakGameName { get => S.SpeakGameName; set { S.SpeakGameName = value; Changed(); } }
    public double PauseAfterNameSeconds
    {
        get => S.PauseAfterNameSeconds;
        set { S.PauseAfterNameSeconds = Math.Clamp(Math.Round(value, 2), 0, 10); Changed(); }
    }
    public bool SpeakStartCoin { get => S.SpeakStartCoin; set { S.SpeakStartCoin = value; Changed(); } }
    public bool SpeakJoysticks { get => S.SpeakJoysticks; set { S.SpeakJoysticks = value; Changed(); } }
    public bool BlinkButtons { get => S.BlinkButtons; set { S.BlinkButtons = value; Changed(); } }
    public bool SpeakOnPause { get => S.SpeakOnPause; set { S.SpeakOnPause = value; Changed(); } }
    public bool StopOnInput { get => S.StopOnInput; set { S.StopOnInput = value; Changed(); } }
    public string Prefix { get => S.Prefix ?? ""; set { S.Prefix = string.IsNullOrWhiteSpace(value) ? null : value; Changed(); } }
    public string StartText { get => S.StartText; set { S.StartText = value; Changed(); } }
    public string CoinText { get => S.CoinText; set { S.CoinText = value; Changed(); } }

    [RelayCommand]
    private void Test() => Status = _speech.Test();

    [RelayCommand]
    private void StopSpeaking()
    {
        _speech.Stop("stopped in the Speech tab");
        Status = "Stopped.";
    }

    [RelayCommand]
    private void CopyFromLedBlinky()
    {
        Status = _speech.CopyFromLedBlinky();
        OnPropertyChanged(string.Empty); // every setting may have changed
    }
}
