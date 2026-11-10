namespace LedNext.Core;

/// <summary>Starting point for a new cabinet: a 4-player panel on two PacLED64 boards.</summary>
public static class Defaults
{
    public static CabinetConfig CreateFourPlayerCabinet()
    {
        var config = new CabinetConfig { Panel = CreateFourPlayerPanel() };
        config.Wiring.Boards.Add(new BoardDefinition { Key = "PacLED64#1", Model = "PacLED64", Name = "PacLED64 #1", GraphX = 1150, GraphY = 0 });
        config.Wiring.Boards.Add(new BoardDefinition { Key = "PacLED64#2", Model = "PacLED64", Name = "PacLED64 #2", GraphX = 1500, GraphY = 0 });
        config.Wiring.AutoWire(config.Panel);
        return config;
    }

    /// <summary>
    /// P1 and P2 in the middle with 8 buttons; P3 and P4 on the outside with 6 buttons. Every player
    /// also has Start, Coin and a joystick, and there is one shared trackball between P1 and P2.
    /// 41 RGB controls = 123 pins, which fits on two PacLED64s (128 pins). Controls are listed in
    /// player order so auto-wire fills board 1 with P1 and P2 first.
    /// </summary>
    public static PanelLayout CreateFourPlayerPanel()
    {
        var panel = new PanelLayout { Width = 1600, Height = 640 };
        AddPlayer(panel, player: 1, originX: 430, buttons: 8);
        AddPlayer(panel, player: 2, originX: 860, buttons: 8);
        AddPlayer(panel, player: 3, originX: 70, buttons: 6);
        AddPlayer(panel, player: 4, originX: 1270, buttons: 6);

        panel.Buttons.Add(new PanelButton
        {
            Id = "TRACKBALL",
            Label = "",
            Player = 0,
            Kind = ControlKind.Trackball,
            X = 850,
            Y = 520,
            Size = 100,
        });
        return panel;
    }

    private static void AddPlayer(PanelLayout panel, int player, double originX, int buttons)
    {
        const double spacing = 72;
        int perRow = buttons / 2;
        double buttonsX = originX + 130; // leave room for the joystick to the left

        for (int i = 0; i < buttons; i++)
        {
            int row = i / perRow, col = i % perRow;
            panel.Buttons.Add(new PanelButton
            {
                Id = $"P{player}_B{i + 1}",
                Label = $"{i + 1}",
                Player = player,
                Kind = ControlKind.Button,
                X = buttonsX + col * spacing + row * 18, // slight stagger, like a typical layout
                Y = 300 + row * spacing,
            });
        }

        panel.Buttons.Add(new PanelButton
        {
            Id = $"P{player}_START",
            Label = $"{player}P",
            Player = player,
            Kind = ControlKind.Start,
            X = originX + 60,
            Y = 120,
            Size = 44,
        });

        panel.Buttons.Add(new PanelButton
        {
            Id = $"P{player}_COIN",
            Label = "Coin",
            Player = player,
            Kind = ControlKind.Coin,
            X = originX + 120,
            Y = 120,
            Size = 44,
        });

        // Joystick to the left of the buttons, level with the gap between the two rows.
        panel.Buttons.Add(new PanelButton
        {
            Id = $"P{player}_STICK",
            Label = "",
            Player = player,
            Kind = ControlKind.Joystick,
            X = originX + 50,
            Y = 336,
            Size = 50,
        });
    }
}
