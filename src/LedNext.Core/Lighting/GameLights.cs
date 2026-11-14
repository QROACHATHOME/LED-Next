using LedNext.Core.Games;

namespace LedNext.Core.Lighting;

/// <summary>
/// Shows one game's resolved lights (static colours; animations come in phase 5). <paramref name="title"/> is the
/// game's own title ("Galaga (Namco rev. B)", "Galaga"), when known; spoken labels say it instead of the rom name.
/// </summary>
public sealed class GameLights(ResolvedLights lights, string displayName, string? title = null) : ILightSource
{
    private readonly KeyValuePair<string, Rgb>[] _colors = lights.ButtonColors.ToArray();

    public string Name { get; } = displayName;
    public ResolvedLights Lights { get; } = lights;
    public string? Title { get; } = title;

    public void Render(RenderContext ctx)
    {
        foreach (var (id, color) in _colors) ctx.Set(id, color);
        ctx.Status = $"lit from {Lights.From}";
    }
}
