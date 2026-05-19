using DeckMark.Core.Mermaid;

namespace DeckMark.Tests;

public sealed class PersistentMermaidCacheRendererTests : IDisposable
{
    private readonly string _cacheDirectory = Path.Combine(Path.GetTempPath(), "DeckMark.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RenderAsync_ReusesPersistedAssetAcrossRendererInstances()
    {
        var expected = new MermaidRenderAsset(MermaidRenderFormat.Png, [1, 2, 3, 4], new MermaidRenderSize(320f, 180f));
        var firstInner = new FakeMermaidRenderer(expected);
        var firstRenderer = new PersistentMermaidCacheRenderer(firstInner, _cacheDirectory);

        var first = await firstRenderer.RenderAsync("graph TD\nA-->B");

        Assert.NotNull(first);
        Assert.Equal(1, firstInner.CallCount);

        var secondInner = new FakeMermaidRenderer(null);
        var secondRenderer = new PersistentMermaidCacheRenderer(secondInner, _cacheDirectory);

        var second = await secondRenderer.RenderAsync("graph TD\nA-->B");

        Assert.NotNull(second);
        Assert.Equal(expected.Format, second!.Format);
        Assert.Equal(expected.Content, second.Content);
        Assert.Equal(expected.Size, second.Size);
        Assert.Equal(0, secondInner.CallCount);
    }

    [Fact]
    public async Task RenderAsync_FallsBackToPersistedAssetWhenInnerRendererFails()
    {
        var expected = new MermaidRenderAsset(MermaidRenderFormat.Svg, "<svg />"u8.ToArray(), new MermaidRenderSize(640f, 480f));
        var warmRenderer = new PersistentMermaidCacheRenderer(new FakeMermaidRenderer(expected), _cacheDirectory);
        var warmed = await warmRenderer.RenderAsync("flowchart LR\nA-->B");
        Assert.NotNull(warmed);

        var offlineRenderer = new PersistentMermaidCacheRenderer(new ThrowingMermaidRenderer(), _cacheDirectory);
        var cached = await offlineRenderer.RenderAsync("flowchart LR\nA-->B");

        Assert.NotNull(cached);
        Assert.Equal(expected.Format, cached!.Format);
        Assert.Equal(expected.Content, cached.Content);
        Assert.Equal(expected.Size, cached.Size);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory))
            Directory.Delete(_cacheDirectory, recursive: true);
    }

    private sealed class FakeMermaidRenderer : IMermaidRenderer
    {
        private readonly MermaidRenderAsset? _result;

        public FakeMermaidRenderer(MermaidRenderAsset? result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<MermaidRenderAsset?> RenderAsync(string mermaidSource, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class ThrowingMermaidRenderer : IMermaidRenderer
    {
        public Task<MermaidRenderAsset?> RenderAsync(string mermaidSource, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Network unavailable");
        }
    }
}
