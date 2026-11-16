using System;
using System.Linq;
using LedNext.FrontEnd.Lite;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace LedNext.Playnite
{
    /// <summary>
    /// Sends Playnite's selection, game start / stop and start-up / shut-down to LED-Next. Never makes Playnite wait
    /// (a background thread writes to the pipe) and never throws into Playnite.
    /// </summary>
    public class LedNextPlugin : GenericPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly LiteClient _client = new LiteClient();
        private readonly string _source;

        public override Guid Id { get; } = Guid.Parse("8f3c2a71-5d1e-4b7a-9c61-2e4f0b9d7a13");

        public LedNextPlugin(IPlayniteAPI api) : base(api)
        {
            _source = api.ApplicationInfo.Mode == ApplicationMode.Fullscreen ? "Playnite Fullscreen" : "Playnite";
            _client.Log += text => Logger.Info("LED-Next: " + text);
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args) =>
            Safe(() => _client.Send(new LiteMessage(LiteMessage.Started) { Source = _source }));

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args) => Safe(() =>
        {
            _client.Send(new LiteMessage(LiteMessage.Closing) { Source = _source });
            _client.Dispose();
        });

        public override void OnGameSelected(OnGameSelectedEventArgs args) => Safe(() =>
        {
            var game = args?.NewValue?.FirstOrDefault();
            if (game != null) _client.Send(ToMessage(LiteMessage.Select, game));
        });

        public override void OnGameStarting(OnGameStartingEventArgs args) => Safe(() =>
        {
            if (args?.Game != null) _client.Send(ToMessage(LiteMessage.Launch, args.Game));
        });

        public override void OnGameStopped(OnGameStoppedEventArgs args) => Safe(() =>
            _client.Send(new LiteMessage(LiteMessage.Exit) { Source = _source, Title = args?.Game?.Name }));

        /// <summary>The game's Playnite id, name, first platform and first rom (its variables filled in).</summary>
        private LiteMessage ToMessage(string ev, Game game)
        {
            var rom = game.Roms?.FirstOrDefault()?.Path;
            if (!string.IsNullOrEmpty(rom))
            {
                try { rom = PlayniteApi.ExpandGameVariables(game, rom); } catch { }
            }
            return new LiteMessage(ev)
            {
                Source = _source,
                GameId = "playnite:" + game.Id,
                Title = game.Name,
                Platform = game.Platforms?.FirstOrDefault()?.Name,
                ApplicationPath = string.IsNullOrEmpty(rom) ? game.Name : rom,
            };
        }

        private static void Safe(Action action)
        {
            try { action(); }
            catch (Exception ex) { Logger.Error(ex, "LED-Next plugin"); }
        }
    }
}
