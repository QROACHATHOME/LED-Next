using System.Text.RegularExpressions;
using System.Xml.Linq;
using LedNext.Core.Games;

namespace LedNext.Core.Import.LedBlinky;

/// <summary>One port (pin) from LEDBlinkyInputMap.xml. Port numbers are 1-based, like LED-Next pins.</summary>
public sealed record InputMapPort(int ControllerId, string ControllerName, int Port, string Label, string Channel, IReadOnlyList<string> InputCodes);

public static partial class LedBlinkyInputMap
{
    public static List<InputMapPort> Load(string path)
    {
        var ports = new List<InputMapPort>();
        foreach (var controller in XDocument.Load(path).Descendants("ledController"))
        {
            int id = (int?)controller.Attribute("id") ?? 1;
            string name = (string?)controller.Attribute("name") ?? "";
            foreach (var port in controller.Elements("port"))
            {
                var codes = SplitCodes((string?)port.Attribute("inputCodes"));
                ports.Add(new InputMapPort(id, name, (int?)port.Attribute("number") ?? 0,
                    (string?)port.Attribute("label") ?? "", (string?)port.Attribute("type") ?? "", codes));
            }
        }
        return ports;
    }

    /// <summary>"|KEYCODE_A|JOYCODE_1_BUTTON1" → [KEYCODE_A, JOYCODE_1_BUTTON1].</summary>
    public static IReadOnlyList<string> SplitCodes(string? codes) =>
        string.IsNullOrWhiteSpace(codes)
            ? Array.Empty<string>()
            : codes.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Select(c => c.ToUpperInvariant()).ToArray();

    /// <summary>
    /// Reads MAME's cfg/default.cfg (the user's own MAME key assignments), keyed by MAME port type
    /// (P1_BUTTON1, START1, UI_SELECT…). Unverified against a real file; returns empty on any problem.
    /// </summary>
    public static Dictionary<string, List<string>> LoadMameDefaultCfg(string path, List<string> notes)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var port in XDocument.Load(path).Descendants("port"))
            {
                var type = (string?)port.Attribute("type");
                var seq = port.Elements("newseq").FirstOrDefault(e => (string?)e.Attribute("type") == "standard");
                if (type is null || seq is null) continue;
                result[type] = CodeToken().Matches(seq.Value).Select(m => m.Value.ToUpperInvariant()).ToList();
            }
        }
        catch (Exception ex)
        {
            notes.Add($"Could not read MAME's {path}: {ex.Message}. Using MAME's built-in key defaults.");
        }
        return result;
    }

    [GeneratedRegex(@"\b(KEYCODE|JOYCODE|MOUSECODE|GUNCODE)_[A-Z0-9_]+\b", RegexOptions.IgnoreCase)]
    private static partial Regex CodeToken();
}

/// <summary>
/// Builds the <see cref="ControlMap"/> the same way LEDBlinky lights a game: a game control's input
/// codes (MAME's key assignments) are matched against the input codes recorded for each LED port in
/// LEDBlinkyInputMap.xml, and the port is followed through LED-Next's wiring to a panel button.
/// </summary>
public static class ControlMapBuilder
{
    /// <param name="ports">LEDBlinkyInputMap.xml ports.</param>
    /// <param name="digitalCodes">Input codes per normalised digital control (P1_BUTTON1, P1_START, UI_SELECT…).</param>
    /// <param name="usedControls">Controls that appear in the imported game data; only these are mapped.</param>
    /// <param name="notes">Receives human-readable findings for the import report.</param>
    public static ControlMap Build(
        IReadOnlyList<InputMapPort> ports,
        IReadOnlyDictionary<string, HashSet<string>> digitalCodes,
        IEnumerable<string> usedControls,
        WiringMap wiring,
        PanelLayout panel,
        List<string> notes)
    {
        var buttonCodes = CollectButtonCodes(ports, wiring, notes);
        var map = new ControlMap();

        foreach (var control in usedControls.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            var (_, isDigital) = ControlNames.Classify(control);
            IEnumerable<string> codes;
            if (isDigital)
                codes = digitalCodes.TryGetValue(control, out var c) ? c : Enumerable.Empty<string>();
            else if (control.EndsWith("_TRACKBALL", StringComparison.OrdinalIgnoreCase))
                codes = new[] { "TRACKBALL" };
            else if (control.EndsWith("_DIAL", StringComparison.OrdinalIgnoreCase))
                codes = new[] { "DIAL" };
            else
                continue; // joysticks and other analog controls: by name only

            var codeSet = codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (codeSet.Count == 0) continue;

            var matches = new List<string>();
            string? matchedCode = null;
            foreach (var b in panel.Buttons)
            {
                if (!buttonCodes.TryGetValue(b.Id, out var bc)) continue;
                var hit = bc.FirstOrDefault(codeSet.Contains);
                if (hit is null) continue;
                matches.Add(b.Id);
                matchedCode ??= hit;
            }
            if (matches.Count == 0) continue;

            map.Mappings.Add(new ControlMapping { Control = control, Buttons = matches, Source = $"input code {matchedCode}" });
            var byName = ControlMap.ByConvention(control, panel);
            if (byName.Count > 0 && !byName.SequenceEqual(matches, StringComparer.OrdinalIgnoreCase))
                notes.Add($"{control} lights {string.Join(", ", matches)} (input code {matchedCode}), not {string.Join(", ", byName)} as its name suggests.");
        }
        return map;
    }

    /// <summary>
    /// The LED-Next board a LEDBlinky controller is: "PACLED64" (seen on the cabinet, type 3), or any name with
    /// "ULTIMATE" in it for the I-PAC Ultimate I/O (LEDBlinky's exact name for it is not known yet; a tester's
    /// LEDBlinkyInputMap.xml will tell).
    /// </summary>
    public static string? BoardKeyFor(InputMapPort port)
    {
        if (port.ControllerName.Contains("PACLED64", StringComparison.OrdinalIgnoreCase))
            return BoardModels.PacLed64.KeyFor(port.ControllerId);
        if (port.ControllerName.Contains("ULTIMATE", StringComparison.OrdinalIgnoreCase))
            return BoardModels.UltimateIO.KeyFor(Math.Max(1, port.ControllerId));
        return null;
    }

    /// <summary>Input codes reaching each LED-Next button, through LEDBlinky port → LED-Next pin binding.</summary>
    public static Dictionary<string, HashSet<string>> CollectButtonCodes(IReadOnlyList<InputMapPort> ports, WiringMap wiring, List<string> notes)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var unsupported = new HashSet<string>();
        foreach (var port in ports)
        {
            if (port.InputCodes.Count == 0) continue;
            if (BoardKeyFor(port) is not { } boardKey)
            {
                if (unsupported.Add($"{port.ControllerName}#{port.ControllerId}"))
                    notes.Add($"LEDBlinky controller {port.ControllerName} #{port.ControllerId} is not supported yet; its ports were skipped.");
                continue;
            }
            var binding = wiring.FindByPin(boardKey, port.Port);
            if (binding is null)
            {
                notes.Add($"LEDBlinky {boardKey} pin {port.Port} ('{port.Label}' {port.Channel}) is not wired in LED-Next.");
                continue;
            }
            if (port.Channel.Length == 1 && !string.Equals(port.Channel, binding.Channel.ToString(), StringComparison.OrdinalIgnoreCase))
                notes.Add($"{boardKey} pin {port.Port}: LEDBlinky says '{port.Label}' {port.Channel}, LED-Next has {binding.ButtonId} {binding.Channel}.");
            if (!result.TryGetValue(binding.ButtonId, out var set))
                result[binding.ButtonId] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.UnionWith(port.InputCodes);
        }
        return result;
    }
}
