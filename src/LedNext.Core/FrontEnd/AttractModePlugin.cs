namespace LedNext.Core.FrontEnd;

/// <summary>
/// LED-Next's Attract-Mode (and Attract-Mode Plus) plugin: a Squirrel script that runs LedNextCmd.exe with
/// LEDBlinky-style commands on Attract-Mode's transitions (start, selection, list, game start / end, screensaver,
/// exit). Written by <see cref="FrontEndSetup.InstallAttractMode"/> to &lt;Attract-Mode&gt;\plugins\LED-Next\plugin.nut.
/// </summary>
public static class AttractModePlugin
{
    /// <summary>The plugin, with the tool's path as its default "Command" setting.</summary>
    public static string Script(string toolPath)
    {
        var path = toolPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $$"""
            // {{FrontEndSetup.Marker}}: lights the panel from Attract-Mode. Safe to delete.
            //
            // Enable it in Attract-Mode: Configure > Plug-ins > LED-Next > Enabled: Yes.
            // It runs LED-Next's command tool (LedNextCmd.exe) with LEDBlinky-style commands, in the background:
            //   1 start · 2 exit · 9 "<rom>" "<system>" selection · 8 "<system>" list · "<rom>" "<system>" game start
            //   4 game end · 5 / 6 screensaver start / end
            // The system is the game's System from its romlist, else its emulator's name.

            class UserConfig </ help="Lights the arcade panel with LED-Next" /> {
                </ label="Command", help="Path to LedNextCmd.exe (in LED-Next's folder)", order=1 />
                command = "{{path}}";
            }

            class LedNext {
                config = null;
                in_screensaver = false;

                constructor() {
                    config = fe.get_config();
                    fe.add_transition_callback(this, "on_transition");
                }

                function q(s) {
                    // Command-line quoting; a rom or system name with a double quote in it loses the quote.
                    local out = "";
                    foreach (c in s) { if (c != '"') out += c.tochar(); }
                    return "\"" + out + "\"";
                }

                function system_of(offset) {
                    local s = fe.game_info(Info.System, offset);
                    if (s == "") s = fe.game_info(Info.Emulator, offset);
                    return s;
                }

                function send(args) {
                    fe.plugin_command_bg(config["command"], "--source Attract-Mode " + args);
                }

                function on_transition(ttype, var, ttime) {
                    switch (ttype) {
                        case Transition.StartLayout:
                            if (var == FromTo.Frontend) send("1");
                            else if (var == FromTo.ScreenSaver) { in_screensaver = true; send("5"); }
                            break;
                        case Transition.EndLayout:
                            if (var == FromTo.Frontend) send("2");
                            else if (var == FromTo.ScreenSaver) { in_screensaver = false; send("6"); }
                            break;
                        case Transition.ToNewList:
                            if (!in_screensaver) send("8 " + q(system_of(0)));
                            break;
                        case Transition.FromOldSelection:
                            if (!in_screensaver) send("9 " + q(fe.game_info(Info.Name)) + " " + q(system_of(0)));
                            break;
                        case Transition.ToGame:
                            send(q(fe.game_info(Info.Name)) + " " + q(system_of(0)));
                            break;
                        case Transition.FromGame:
                            send("4");
                            break;
                    }
                    return false;
                }
            }

            fe.plugin["LED-Next"] <- LedNext();

            """;
    }
}
