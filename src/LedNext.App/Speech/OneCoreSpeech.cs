using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using LedNext.Core;
using LedNext.Core.Speech;

namespace LedNext.App.Speech;

/// <summary>
/// Windows' newer voices (the "OneCore" voices Narrator uses: Microsoft David, Zira, Mark…), which sound clearer than
/// the old SAPI "Desktop" voices that System.Speech reaches. Each item is synthesized to its own audio, then the items
/// are played in turn, so the caller knows exactly when each label starts (to blink its buttons).
/// </summary>
internal static class OneCoreSpeech
{
    /// <summary>The installed newer voices' names ("Microsoft Mark"), or none when the API isn't available.</summary>
    public static IReadOnlyList<string> Voices()
    {
        try { return SpeechSynthesizer.AllVoices.Select(v => v.DisplayName).ToList(); }
        catch (Exception ex)
        {
            AppLog.Warn($"Speech: could not list Windows' newer voices ({ex.Message}).");
            return Array.Empty<string>();
        }
    }

    /// <summary>The newer voice with this name, or null (then it is an old SAPI voice, or not installed).</summary>
    public static VoiceInformation? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try { return SpeechSynthesizer.AllVoices.FirstOrDefault(v => string.Equals(v.DisplayName, name, StringComparison.OrdinalIgnoreCase)); }
        catch { return null; }
    }

    /// <summary>
    /// Says the items in order. <paramref name="itemStarted"/> is called as each one starts playing. Call from the UI
    /// thread: the awaits come back to it. Cancelling stops at once.
    /// </summary>
    public static async Task SpeakAsync(VoiceInformation voice, IReadOnlyList<SpeechItem> items, int rate, int volume,
        Action<int> itemStarted, CancellationToken ct)
    {
        using var synth = new SpeechSynthesizer { Voice = voice };
        // SAPI's -10…10 is roughly a third to three times normal speed; the newer API takes the factor (0.5…6).
        synth.Options.SpeakingRate = Math.Clamp(Math.Pow(3, rate / 10.0), 0.5, 6.0);
        synth.Options.AudioVolume = Math.Clamp(volume, 0, 100) / 100.0;

        // Synthesize everything first (a few hundred ms in all), so the labels play without gaps.
        var audio = new List<SpeechSynthesisStream>();
        try
        {
            foreach (var item in items)
            {
                audio.Add(await synth.SynthesizeTextToStreamAsync(item.Text));
                ct.ThrowIfCancellationRequested();
            }

            using var player = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Speech };
            for (int i = 0; i < items.Count; i++)
            {
                itemStarted(i);
                await PlayAsync(player, audio[i], ct);
                // The audio already ends with a short silence; add the item's own pause (after the game's name).
                if (items[i].PauseAfter > TimeSpan.Zero) await Task.Delay(items[i].PauseAfter, ct);
            }
        }
        finally
        {
            foreach (var a in audio) a.Dispose();
        }
    }

    private static async Task PlayAsync(MediaPlayer player, SpeechSynthesisStream audio, CancellationToken ct)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Ended(MediaPlayer p, object e) => done.TrySetResult();
        void Failed(MediaPlayer p, MediaPlayerFailedEventArgs e) => done.TrySetException(new InvalidOperationException(e.ErrorMessage));
        player.MediaEnded += Ended;
        player.MediaFailed += Failed;
        try
        {
            using var source = MediaSource.CreateFromStream(audio, audio.ContentType);
            player.Source = source;
            using (ct.Register(() => { try { player.Pause(); } catch { } done.TrySetCanceled(ct); }))
            {
                player.Play();
                await done.Task;
            }
        }
        finally
        {
            player.MediaEnded -= Ended;
            player.MediaFailed -= Failed;
            player.Source = null;
        }
    }
}
