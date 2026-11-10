using LedNext.Core.Games;

namespace LedNext.Core.Lighting;

/// <summary>Shows one game's resolved lights (static colours; animations come in phase 5).</summary>
public sealed class GameLights(ResolvedLights lights, string displayName) : ILightSource
{
    private readonly KeyValuePair<string, Rgb>[] _colors = lights.ButtonColors.ToArray();

    public string Name { get; } = displayName;
    public ResolvedLights Lights { get; } = lights;

    public void Render(RenderContext ctx)
    {
        foreach (var (id, color) in _colors) ctx.Set(id, color);
        ctx.Status = $"lit from {Lights.From}";
    }
}
