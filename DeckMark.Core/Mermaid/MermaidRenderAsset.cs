namespace DeckMark.Core.Mermaid;

/// <summary>
/// Represents rendered Mermaid content and its output format.
/// </summary>
/// <param name="Format">The encoded content format.</param>
/// <param name="Content">The encoded Mermaid render output.</param>
/// <param name="Size">The intrinsic rendered Mermaid size.</param>
public sealed record MermaidRenderAsset(MermaidRenderFormat Format, byte[] Content, MermaidRenderSize Size);

/// <summary>
/// Represents the intrinsic rendered Mermaid size.
/// </summary>
/// <param name="Width">The intrinsic width in pixels or SVG user units.</param>
/// <param name="Height">The intrinsic height in pixels or SVG user units.</param>
public readonly record struct MermaidRenderSize(float Width, float Height)
{
    public static MermaidRenderSize Empty => new(0f, 0f);
}

/// <summary>
/// Supported encoded Mermaid output formats.
/// </summary>
public enum MermaidRenderFormat
{
    /// <summary>
    /// Scalable Vector Graphics output.
    /// </summary>
    Svg,

    /// <summary>
    /// Portable Network Graphics output.
    /// </summary>
    Png,
}
