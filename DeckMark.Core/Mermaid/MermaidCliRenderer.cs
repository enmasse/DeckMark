namespace DeckMark.Core.Mermaid;

/// <summary>
/// Renders Mermaid diagrams by shelling out to the <c>mmdc</c> CLI tool.
/// Requires Node.js and <c>@mermaid-js/mermaid-cli</c> to be installed
/// (<c>npm install -g @mermaid-js/mermaid-cli</c>).
/// </summary>
public sealed class MermaidCliRenderer : IMermaidRenderer
{
    private readonly string _mmdc;

    /// <param name="mmdcPath">Path to the mmdc executable. Defaults to "mmdc" on PATH.</param>
    public MermaidCliRenderer(string mmdcPath = "mmdc")
    {
        _mmdc = mmdcPath;
    }

    public async Task<byte[]?> RenderAsync(string mermaidSource, CancellationToken cancellationToken = default)
    {
        var inputFile = Path.GetTempFileName() + ".mmd";
        var outputFile = Path.GetTempFileName() + ".png";
        try
        {
            await File.WriteAllTextAsync(inputFile, mermaidSource, cancellationToken).ConfigureAwait(false);

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
            return await File.ReadAllBytesAsync(outputFile, cancellationToken).ConfigureAwait(false);
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
}
