using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DeckMark.Core.Mermaid;

/// <summary>
/// Wraps another <see cref="IMermaidRenderer"/> and persists rendered Mermaid assets for reuse across sessions.
/// </summary>
public sealed class PersistentMermaidCacheRenderer : IMermaidRenderer
{
    private readonly IMermaidRenderer _inner;
    private readonly string _cacheDirectory;

    public PersistentMermaidCacheRenderer(IMermaidRenderer inner, string? cacheDirectory = null)
    {
        _inner = inner;
        _cacheDirectory = string.IsNullOrWhiteSpace(cacheDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeckMark", "MermaidCache")
            : cacheDirectory;
    }

    public async Task<MermaidRenderAsset?> RenderAsync(string mermaidSource, CancellationToken cancellationToken = default)
    {
        string cachePath = GetCachePath(mermaidSource);

        MermaidRenderAsset? cached = await TryReadCachedAsync(cachePath, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
            return cached;

        MermaidRenderAsset? rendered = await _inner.RenderAsync(mermaidSource, cancellationToken).ConfigureAwait(false);
        if (rendered is not null)
        {
            await TryWriteCachedAsync(cachePath, rendered, cancellationToken).ConfigureAwait(false);
            return rendered;
        }

        return await TryReadCachedAsync(cachePath, cancellationToken).ConfigureAwait(false);
    }

    private string GetCachePath(string mermaidSource)
    {
        var input = Encoding.UTF8.GetBytes($"v1\n{mermaidSource}");
        var hash = Convert.ToHexStringLower(SHA256.HashData(input));
        return Path.Combine(_cacheDirectory, hash + ".json");
    }

    private async Task<MermaidRenderAsset?> TryReadCachedAsync(string cachePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(cachePath))
            return null;

        try
        {
            await using var stream = File.OpenRead(cachePath);
            var payload = await JsonSerializer.DeserializeAsync<CachedMermaidAsset>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (payload is null || payload.Content is null || !Enum.IsDefined(payload.Format))
                return null;

            return new MermaidRenderAsset(
                payload.Format,
                payload.Content,
                new MermaidRenderSize(payload.Width, payload.Height));
        }
        catch
        {
            return null;
        }
    }

    private async Task TryWriteCachedAsync(string cachePath, MermaidRenderAsset asset, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            string tempPath = cachePath + ".tmp";
            var payload = new CachedMermaidAsset(asset.Format, asset.Content, asset.Size.Width, asset.Size.Height);

            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, payload, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, cachePath, overwrite: true);
        }
        catch
        {
        }
    }

    private sealed record CachedMermaidAsset(MermaidRenderFormat Format, byte[] Content, float Width, float Height);
}
