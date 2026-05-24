using SkiaSharp;

namespace DeckMark.Viewer;

internal sealed class AmbientBackgroundAnimator
{
    private const int BlobCount = 3;
    private const float MinRadius = 260f;
    private const float MaxRadius = 620f;
    private const float MinAlpha = 0.22f;
    private const float MaxAlpha = 0.40f;
    private const float MinBlur = 18f;
    private const float MaxBlur = 42f;
    private const float MinCycleSeconds = 14f;
    private const float MaxCycleSeconds = 28f;
    private const float MinHoldSeconds = 3f;
    private const float MaxHoldSeconds = 8f;
    private const float MinDriftSeconds = 18f;
    private const float MaxDriftSeconds = 34f;

    private static readonly SKColor[] Palette =
    [
        new(0x3A, 0x6E, 0xA5),
        new(0x4F, 0xA3, 0xA5),
        new(0xD9, 0xC6, 0xA5),
        new(0x6B, 0x8F, 0xC8),
        new(0x7C, 0xB7, 0xA3),
    ];

    private readonly AmbientBlob[] _blobs;

    public AmbientBackgroundAnimator()
    {
        _blobs = new AmbientBlob[BlobCount];
        for (int i = 0; i < _blobs.Length; i++)
            _blobs[i] = AmbientBlob.CreateInitial();
    }

    public void Update(DateTimeOffset now, float width, float height)
    {
        if (width <= 0f || height <= 0f)
            return;

        for (int i = 0; i < _blobs.Length; i++)
            _blobs[i] = _blobs[i].Update(now);
    }

    public void Draw(SKCanvas canvas, float width, float height, DateTimeOffset now)
    {
        Update(now, width, height);

        foreach (var blob in _blobs)
            DrawBlob(canvas, blob, width, height, now);
    }

    private static void DrawBlob(SKCanvas canvas, AmbientBlob blob, float width, float height, DateTimeOffset now)
    {
        var center = blob.GetCenter(now, width, height);
        float opacity = blob.GetOpacity(now);
        if (opacity <= 0.001f)
            return;

        float radius = blob.GetRadius(now);
        var color = blob.Color.WithAlpha((byte)Math.Clamp((int)MathF.Round(opacity * 255f), 0, 255));
        var transparent = blob.Color.WithAlpha(0);

        using var shader = SKShader.CreateRadialGradient(
            center,
            radius,
            [color, transparent],
            [0f, 1f],
            SKShaderTileMode.Clamp);
        using var blur = SKImageFilter.CreateBlur(blob.BlurSigma, blob.BlurSigma);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Shader = shader,
            ImageFilter = blur,
            BlendMode = SKBlendMode.SrcOver,
        };

        canvas.DrawCircle(center, radius, paint);
    }

    private readonly record struct AmbientBlob(
        SKColor Color,
        DateTimeOffset CycleStartedAt,
        TimeSpan FadeInDuration,
        TimeSpan HoldDuration,
        TimeSpan FadeOutDuration,
        TimeSpan DriftDuration,
        SKPoint StartPosition,
        SKPoint EndPosition,
        float StartRadius,
        float EndRadius,
        float PeakAlpha,
        float BlurSigma)
    {
        public static AmbientBlob CreateInitial()
        {
            var now = DateTimeOffset.UtcNow;
            return Create(now, advanceIntoCycle: true);
        }

        public AmbientBlob Update(DateTimeOffset now)
        {
            if (now - CycleStartedAt < TotalDuration)
                return this;

            return Create(now, advanceIntoCycle: false);
        }

        public SKPoint GetCenter(DateTimeOffset now, float width, float height)
        {
            _ = now;
            return ResolvePoint(StartPosition, width, height);
        }

        public float GetOpacity(DateTimeOffset now)
        {
            var elapsed = now - CycleStartedAt;
            if (elapsed <= TimeSpan.Zero)
                return 0f;

            if (elapsed < FadeInDuration)
                return PeakAlpha * SmoothStep(GetNormalizedProgress(now, FadeInDuration));

            elapsed -= FadeInDuration;
            if (elapsed < HoldDuration)
                return PeakAlpha;

            elapsed -= HoldDuration;
            if (elapsed < FadeOutDuration)
                return PeakAlpha * (1f - SmoothStep((float)(elapsed.TotalMilliseconds / FadeOutDuration.TotalMilliseconds)));

            return 0f;
        }

        public float GetRadius(DateTimeOffset now)
        {
            float driftProgress = SmoothStep(GetNormalizedProgress(now, DriftDuration));
            return Lerp(StartRadius, EndRadius, driftProgress);
        }

        private TimeSpan TotalDuration => FadeInDuration + HoldDuration + FadeOutDuration;

        private static AmbientBlob Create(DateTimeOffset now, bool advanceIntoCycle)
        {
            var fadeIn = TimeSpan.FromSeconds(RandomRange(MinCycleSeconds * 0.28f, MaxCycleSeconds * 0.4f));
            var hold = TimeSpan.FromSeconds(RandomRange(MinHoldSeconds, MaxHoldSeconds));
            var fadeOut = TimeSpan.FromSeconds(RandomRange(MinCycleSeconds * 0.45f, MaxCycleSeconds * 0.62f));
            var totalDuration = fadeIn + hold + fadeOut;
            var cycleStart = advanceIntoCycle
                ? now - TimeSpan.FromSeconds(RandomRange(0f, (float)totalDuration.TotalSeconds))
                : now;
            var position = RandomPosition();

            return new AmbientBlob(
                Palette[Random.Shared.Next(Palette.Length)],
                cycleStart,
                fadeIn,
                hold,
                fadeOut,
                TimeSpan.FromSeconds(RandomRange(MinDriftSeconds, MaxDriftSeconds)),
                position,
                position,
                RandomRange(MinRadius, MaxRadius),
                RandomRange(MinRadius, MaxRadius),
                RandomRange(MinAlpha, MaxAlpha),
                RandomRange(MinBlur, MaxBlur));
        }

        private static SKPoint ResolvePoint(SKPoint normalized, float width, float height)
        {
            return new SKPoint(normalized.X * width, normalized.Y * height);
        }

        private float GetNormalizedProgress(DateTimeOffset now, TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
                return 1f;

            var elapsed = now - CycleStartedAt;
            return Math.Clamp((float)(elapsed.TotalMilliseconds / duration.TotalMilliseconds), 0f, 1f);
        }

        private static SKPoint RandomPosition()
        {
            return new SKPoint(RandomRange(0.1f, 0.9f), RandomRange(0.08f, 0.92f));
        }

        private static float RandomRange(float min, float max)
        {
            return min + ((float)Random.Shared.NextDouble() * (max - min));
        }

        private static float SmoothStep(float value)
        {
            var t = Math.Clamp(value, 0f, 1f);
            return t * t * (3f - (2f * t));
        }

        private static float Lerp(float from, float to, float progress)
        {
            return from + ((to - from) * progress);
        }
    }
}
