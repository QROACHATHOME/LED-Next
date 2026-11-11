using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LedNext.Core.Import.Mame;

/// <summary>
/// Reads the keys bound in a TeknoParrot game profile (<c>TeknoParrot\UserProfiles\&lt;game&gt;.xml</c>, read only).
/// </summary>
/// <remarks>
/// Each <c>JoystickButtons</c> entry names a game input (<c>P1Button1</c>, <c>P1ButtonStart</c>, <c>Coin1</c>) and its
/// binding for the profile's "Input API": DirectInput keeps the key in <c>BindName</c> ("Keyboard Button LeftControl",
/// SharpDX key names); RawInput keeps it in <c>RawInputButton/KeyboardKey</c> ("ControlKey", Windows Forms key names)
/// with <c>DeviceType</c> Keyboard. Both are turned into MAME key codes (KEYCODE_LCONTROL), so the panel's input map
/// can follow them. Gamepad, wheel, gun and XInput bindings have no key and are left out.
/// </remarks>
public static partial class TeknoParrotProfile
{
    /// <summary>True for a LaunchBox application path that is a TeknoParrot game profile.</summary>
    public static bool IsProfilePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "UserProfiles", StringComparison.OrdinalIgnoreCase);

    /// <summary>MAME port type (P1_BUTTON1, START1, COIN1) → the MAME key codes bound to it. Only keyboard bindings.</summary>
    public static Dictionary<string, List<string>> ReadKeys(string profilePath)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var root = XDocument.Load(profilePath).Root;
        if (root is null) return result;
        var api = root.Element("ConfigValues")?.Elements("FieldInformation")
                      .FirstOrDefault(f => (string?)f.Element("FieldName") == "Input API")?.Element("FieldValue")?.Value ?? "";

        foreach (var b in root.Element("JoystickButtons")?.Elements("JoystickButtons") ?? Enumerable.Empty<XElement>())
        {
            var type = PortType((string?)b.Element("InputMapping"));
            if (type is null) continue;
            string? key = null;
            if (api.Equals("DirectInput", StringComparison.OrdinalIgnoreCase))
            {
                var bind = (string?)b.Element("BindName") ?? "";
                if (bind.StartsWith(KeyboardPrefix, StringComparison.Ordinal) && b.Element("DirectInputButton") is not null)
                    key = bind[KeyboardPrefix.Length..].Trim();
            }
            else if (api.StartsWith("RawInput", StringComparison.OrdinalIgnoreCase))
            {
                var ri = b.Element("RawInputButton");
                if ((string?)ri?.Element("DeviceType") == "Keyboard") key = (string?)ri.Element("KeyboardKey");
            }
            if (KeyCode(key) is not { } code) continue;
            if (!result.TryGetValue(type, out var codes)) result[type] = codes = new List<string>();
            if (!codes.Contains(code)) codes.Add(code);
        }
        return result;
    }

    private const string KeyboardPrefix = "Keyboard Button ";

    /// <summary>"P1Button3" → P1_BUTTON3, "P2ButtonStart" → START2, "Coin1" → COIN1; anything else (directions, test, analog) → null.</summary>
    public static string? PortType(string? inputMapping)
    {
        if (inputMapping is null) return null;
        var m = Mapping().Match(inputMapping.Trim());
        if (!m.Success) return null;
        if (m.Groups["coin"].Success) return $"COIN{m.Groups["coin"].Value}";
        var player = m.Groups["p"].Value;
        return m.Groups["b"].Value.Equals("Start", StringComparison.OrdinalIgnoreCase) ? $"START{player}" : $"P{player}_BUTTON{m.Groups["b"].Value}";
    }

    private static readonly Dictionary<string, string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        // DirectInput (SharpDX Key) names
        ["LeftControl"] = "LCONTROL", ["RightControl"] = "RCONTROL", ["LeftAlt"] = "LALT", ["RightAlt"] = "RALT",
        ["LeftShift"] = "LSHIFT", ["RightShift"] = "RSHIFT", ["Return"] = "ENTER", ["NumberPadEnter"] = "ENTER_PAD",
        ["Comma"] = "COMMA", ["Period"] = "STOP", ["Slash"] = "SLASH", ["Semicolon"] = "COLON", ["Apostrophe"] = "QUOTE",
        ["LeftBracket"] = "OPENBRACE", ["RightBracket"] = "CLOSEBRACE", ["Minus"] = "MINUS", ["Equals"] = "EQUALS",
        ["Backslash"] = "BACKSLASH", ["Back"] = "BACKSPACE", ["Escape"] = "ESC", ["Tab"] = "TAB",
        ["UpArrow"] = "UP", ["DownArrow"] = "DOWN", ["LeftArrow"] = "LEFT", ["RightArrow"] = "RIGHT",
        // RawInput (Windows Forms Keys) names; a bare Control/Shift/Menu is the left one on a keyboard encoder
        ["ControlKey"] = "LCONTROL", ["LControlKey"] = "LCONTROL", ["RControlKey"] = "RCONTROL",
        ["ShiftKey"] = "LSHIFT", ["LShiftKey"] = "LSHIFT", ["RShiftKey"] = "RSHIFT",
        ["Menu"] = "LALT", ["LMenu"] = "LALT", ["RMenu"] = "RALT", ["Enter"] = "ENTER",
        ["Oemcomma"] = "COMMA", ["OemPeriod"] = "STOP", ["OemOpenBrackets"] = "OPENBRACE", ["OemCloseBrackets"] = "CLOSEBRACE",
        ["Oem4"] = "OPENBRACE", ["Oem6"] = "CLOSEBRACE", ["OemMinus"] = "MINUS", ["Oemplus"] = "EQUALS",
        ["OemQuestion"] = "SLASH", ["OemSemicolon"] = "COLON", ["OemQuotes"] = "QUOTE", ["Escape"] = "ESC",
        // both
        ["Space"] = "SPACE", ["Up"] = "UP", ["Down"] = "DOWN", ["Left"] = "LEFT", ["Right"] = "RIGHT",
    };

    /// <summary>A TeknoParrot key name as a MAME key code ("LeftControl" → KEYCODE_LCONTROL, "D1" → KEYCODE_1); null if unknown.</summary>
    public static string? KeyCode(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        key = key.Trim();
        if (Named.TryGetValue(key, out var named)) return "KEYCODE_" + named;
        if (key.Length == 2 && key[0] == 'D' && char.IsAsciiDigit(key[1])) return "KEYCODE_" + key[1];
        if (key.Length == 1 && char.IsAsciiLetter(key[0])) return "KEYCODE_" + char.ToUpperInvariant(key[0]);
        if (FunctionKey().IsMatch(key)) return "KEYCODE_" + key.ToUpperInvariant();
        return null; // numbers ("105") and anything else LED-Next can't name
    }

    [GeneratedRegex(@"^(?:P(?<p>\d)Button(?<b>\d+|Start)|Coin(?<coin>\d))$", RegexOptions.IgnoreCase)]
    private static partial Regex Mapping();

    [GeneratedRegex(@"^F([1-9]|1[0-2])$", RegexOptions.IgnoreCase)]
    private static partial Regex FunctionKey();
}
