namespace LedNext.Core.Lighting;

public sealed class AllOff : ILightSource
{
    public string Name => "Off";
    public void Render(RenderContext ctx) { }
}

public sealed class SolidColor(Rgb color, string? name = null) : ILightSource
{
    public string Name { get; } = name ?? $"Solid {color.ToHex()}";
    public void Render(RenderContext ctx) => ctx.SetAll(color);
}

/// <summary>Lights one button at a time in white, in panel order. Others glow dim.</summary>
public sealed class ButtonChase(double stepSeconds = 0.35) : ILightSource
{
    public string Name => "Button chase";

    public void Render(RenderContext ctx)
    {
        var buttons = ctx.Panel.Buttons;
        if (buttons.Count == 0) return;
        ctx.SetAll(new Rgb(12, 12, 12));
        var current = buttons[(int)(ctx.Time.TotalSeconds / stepSeconds) % buttons.Count];
        ctx.Set(current.Id, Rgb.White);
        ctx.Status = $"{current.Id} ({current.Label})";
    }
}

/// <summary>All buttons red → green → blue → white. A button showing the wrong colour has swapped channels.</summary>
public sealed class RgbCycle(double stepSeconds = 1.0) : ILightSource
{
    private static readonly (Rgb Color, string Name)[] Steps =
        { (Rgb.Red, "Red"), (Rgb.Green, "Green"), (Rgb.Blue, "Blue"), (Rgb.White, "White") };

    public string Name => "RGB cycle";

    public void Render(RenderContext ctx)
    {
        var step = Steps[(int)(ctx.Time.TotalSeconds / stepSeconds) % Steps.Length];
        ctx.SetAll(step.Color);
        ctx.Status = $"All buttons should be {step.Name}";
    }
}

/// <summary>A hue wave sweeping across the panel from left to right.</summary>
public sealed class Rainbow(double degreesPerSecond = 90, double spread = 360) : ILightSource
{
    public string Name => "Rainbow";

    public void Render(RenderContext ctx)
    {
        double offset = ctx.Time.TotalSeconds * degreesPerSecond;
        foreach (var b in ctx.Panel.Buttons)
            ctx.Set(b.Id, Rgb.FromHsv(b.NormX * spread - offset, 1, 1));
    }
}

/// <summary>Lights each pin of each board in turn, ignoring wiring. Used to discover physical wiring.</summary>
public sealed class PinWalk(double stepSeconds = 0.6) : ILightSource
{
    public string Name => "Pin walk";

    public void Render(RenderContext ctx)
    {
        int total = ctx.Panel.Boards.Sum(b => b.PinCount);
        if (total == 0) return;
        int index = (int)(ctx.Time.TotalSeconds / stepSeconds) % total;
        foreach (var board in ctx.Panel.Boards)
        {
            if (index < board.PinCount)
            {
                ctx.SetPin(board.Key, index + 1, 255);
                ctx.Status = $"{board.Name} pin {index + 1}";
                return;
            }
            index -= board.PinCount;
        }
    }
}

/// <summary>One pin on at full brightness, steady (used by the wiring wizard).</summary>
public sealed class SinglePin(string boardKey, string boardName, int pin) : ILightSource
{
    public string Name => "Wiring wizard";

    public void Render(RenderContext ctx)
    {
        ctx.SetPin(boardKey, pin, 255);
        ctx.Status = $"{boardName} pin {pin} is lit";
    }
}

/// <summary>Flashes one button white so you can find it on the panel.</summary>
public sealed class IdentifyButton(string buttonId) : ILightSource
{
    public string ButtonId { get; } = buttonId;
    public string Name => $"Identify {ButtonId}";

    public void Render(RenderContext ctx)
    {
        bool on = (int)(ctx.Time.TotalSeconds * 4) % 2 == 0;
        ctx.Set(ButtonId, on ? Rgb.White : Rgb.Black);
        ctx.Status = $"Flashing {ButtonId}";
    }
}

/// <summary>Pins toggled by hand from the Boards view. Thread-safe.</summary>
public sealed class ManualPins : ILightSource
{
    private readonly object _gate = new();
    private readonly HashSet<(string BoardKey, int Pin)> _on = new();

    public string Name => "Manual pins";

    public bool Toggle(string boardKey, int pin)
    {
        lock (_gate)
        {
            if (_on.Remove((boardKey, pin))) return false;
            _on.Add((boardKey, pin));
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate) _on.Clear();
    }

    public void Render(RenderContext ctx)
    {
        lock (_gate)
        {
            foreach (var (key, pin) in _on) ctx.SetPin(key, pin, 255);
            ctx.Status = _on.Count == 0 ? "Click pins to light them" : $"{_on.Count} pin(s) on";
        }
    }
}
