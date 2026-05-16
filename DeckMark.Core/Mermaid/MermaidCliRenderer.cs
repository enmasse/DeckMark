using System.Text.Json;

namespace DeckMark.Core.Mermaid;

/// <summary>
/// Renders Mermaid diagrams by shelling out to the <c>mmdc</c> CLI tool.
/// Requires Node.js and <c>@mermaid-js/mermaid-cli</c> to be installed
/// (<c>npm install -g @mermaid-js/mermaid-cli</c>).
/// </summary>
public sealed class MermaidCliRenderer : IMermaidRenderer
{
    private readonly string _mmdc;
    private readonly MermaidRenderFormat _format;

    /// <param name="mmdcPath">Path to the mmdc executable. Defaults to "mmdc" on PATH.</param>
    /// <param name="format">Requested Mermaid output format.</param>
    public MermaidCliRenderer(string mmdcPath = "mmdc", MermaidRenderFormat format = MermaidRenderFormat.Png)
    {
        _mmdc = mmdcPath;
        _format = format;
    }

    public async Task<MermaidRenderAsset?> RenderAsync(string mermaidSource, CancellationToken cancellationToken = default)
    {
        string effectiveSource = EnsureRenderCompatibleSource(mermaidSource, _format);

        var inputFile = Path.GetTempFileName() + ".mmd";
        var outputFile = Path.GetTempFileName() + GetFileExtension(_format);
        try
        {
            await File.WriteAllTextAsync(inputFile, effectiveSource, cancellationToken).ConfigureAwait(false);

            var psi = new System.Diagnostics.ProcessStartInfo(_mmdc,
                $"-i \"{inputFile}\" -o \"{outputFile}\" -b transparent")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return null;
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (proc.ExitCode != 0 || !File.Exists(outputFile)) return null;
            var content = await File.ReadAllBytesAsync(outputFile, cancellationToken).ConfigureAwait(false);
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
        finally
        {
            if (File.Exists(inputFile)) File.Delete(inputFile);
            if (File.Exists(outputFile)) File.Delete(outputFile);
        }

    }

    private static string GetFileExtension(MermaidRenderFormat format)
    {
        return format switch
        {
            MermaidRenderFormat.Svg => ".svg",
            _ => ".png",
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
