using System.Text.Json;

namespace DeckMark.Core.Mermaid;

/// <summary>
/// Renders Mermaid diagrams by calling the mermaid.ink public API.
/// Requires internet access. No Node.js installation needed.
/// </summary>
public sealed class MermaidInkRenderer : IMermaidRenderer
{
    private static readonly HttpClient Http = new();
    private readonly MermaidRenderFormat _format;

    public MermaidInkRenderer(MermaidRenderFormat format = MermaidRenderFormat.Png)
    {
        _format = format;
    }

    public async Task<MermaidRenderAsset?> RenderAsync(string mermaidSource, CancellationToken cancellationToken = default)
    {
        try
        {
            string effectiveSource = EnsureRenderCompatibleSource(mermaidSource, _format);

            // mermaid.ink requires URL-safe Base64 (no +, /, or = padding)
            var bytes = System.Text.Encoding.UTF8.GetBytes(effectiveSource);
            var encoded = Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
            var url = GetBaseUrl(_format) + encoded;
            var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            DumpDebugSvg(content);
            var size = _format == MermaidRenderFormat.Svg
                ? MermaidSvgSizeParser.Parse(content)
                : MermaidRenderSize.Empty;
            return new MermaidRenderAsset(_format, content, size);
        }
        catch
        {
            return null;
        }

    }

    private static string GetBaseUrl(MermaidRenderFormat format)
    {
        return format switch
        {
            MermaidRenderFormat.Svg => "https://mermaid.ink/svg/",
            _ => "https://mermaid.ink/img/",
        };
    }

    private static string EnsureRenderCompatibleSource(string mermaidSource, MermaidRenderFormat format)
    {
        if (mermaidSource.Contains("%%{init:", StringComparison.Ordinal) ||
            mermaidSource.Contains("%%{initialize:", StringComparison.Ordinal))
            return mermaidSource;

        const string themeCss = ".node rect, .node circle, .node ellipse, .node polygon, .node path, .cluster rect, .cluster polygon, .label-container { filter: none !important; }";
        object config = format == MermaidRenderFormat.Svg
            ? new { flowchart = new { htmlLabels = false }, themeCSS = themeCss }
            : new { themeCSS = themeCss };
        string configJson = JsonSerializer.Serialize(config);
        string init = $"%%{{init: {configJson} }}%%";
        return $"{init}{Environment.NewLine}{mermaidSource}";
    }

    private static void DumpDebugSvg(byte[] content)
    {
        if (content.Length == 0)
            return;

        string? path = Environment.GetEnvironmentVariable("DECKMARK_DEBUG_MERMAID_SVG");
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            File.WriteAllBytes(path, content);
        }
        catch
        {
        }
    }
}
