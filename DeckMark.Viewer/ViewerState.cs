using SkiaSharp;

namespace DeckMark.Viewer;

internal enum SlideTransitionKind
{
    None,
    Fade,
    Dissolve,
    Newsflash,
    Cover,
    Pull,
    Push,
    Wipe,
    Blinds,
    Checker,
    Comb,
    RandomBar,
    Split,
    Strips,
    Circle,
    Cut,
    Diamond,
    Plus,
    Wedge,
    Wheel,
    Zoom,
}

internal sealed class ViewerState
{
    private static readonly SlideTransitionKind[] RandomTransitionKinds =
    [
        SlideTransitionKind.Fade,
        SlideTransitionKind.Dissolve,
        SlideTransitionKind.Newsflash,
        SlideTransitionKind.Cover,
        SlideTransitionKind.Pull,
        SlideTransitionKind.Push,
        SlideTransitionKind.Wipe,
        SlideTransitionKind.Blinds,
        SlideTransitionKind.Checker,
        SlideTransitionKind.Comb,
        SlideTransitionKind.RandomBar,
        SlideTransitionKind.Split,
        SlideTransitionKind.Strips,
        SlideTransitionKind.Circle,
        SlideTransitionKind.Cut,
        SlideTransitionKind.Diamond,
        SlideTransitionKind.Plus,
        SlideTransitionKind.Wedge,
        SlideTransitionKind.Wheel,
        SlideTransitionKind.Zoom,
    ];

    public int SlideIndex { get; private set; }
    public int SlideCount { get; init; }
    public int? TransitionFromSlideIndex { get; private set; }
    public SlideTransitionKind TransitionKind { get; private set; }
    public DateTimeOffset? TransitionStartedAt { get; private set; }

    public float Zoom { get; private set; } = 1.0f;
    public SKPoint Pan { get; private set; } = SKPoint.Empty;
    public bool FillMode { get; private set; }

    public bool Dirty { get; set; } = true;

    private const float ZoomStep = 0.1f;
    private const float ZoomMin = 0.25f;
    private const float ZoomMax = 4.0f;
    private static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(350);

    public bool Next()
    {
        if (SlideIndex >= SlideCount - 1) return false;
        SlideIndex++;
        Dirty = true;
        return true;
    }

    public bool Previous()
    {
        if (SlideIndex <= 0) return false;
        SlideIndex--;
        Dirty = true;
        return true;
    }

    public void ZoomIn()
    {
        Zoom = Math.Clamp(Zoom + ZoomStep, ZoomMin, ZoomMax);
        Dirty = true;
    }

    public void ZoomOut()
    {
        Zoom = Math.Clamp(Zoom - ZoomStep, ZoomMin, ZoomMax);
        Dirty = true;
    }

    public void ZoomDelta(float delta)
    {
        Zoom = Math.Clamp(Zoom + delta * 0.05f, ZoomMin, ZoomMax);
        Dirty = true;
    }

    public void ResetZoom()
    {
        Zoom = 1.0f;
        Pan = SKPoint.Empty;
        FillMode = false;
        Dirty = true;
    }

    public void ToggleFill()
    {
        FillMode = !FillMode;
        Pan = SKPoint.Empty;
        Dirty = true;
    }

    public void ApplyPan(float dx, float dy)
    {
        Pan = new SKPoint(Pan.X + dx, Pan.Y + dy);
        Dirty = true;
    }

    public void BeginTransition(int fromSlideIndex, string? transition)
    {
        var kind = ParseTransition(transition);
        if (kind == SlideTransitionKind.None)
        {
            EndTransition();
            Dirty = true;
            return;
        }

        TransitionFromSlideIndex = fromSlideIndex;
        TransitionKind = kind;
        TransitionStartedAt = DateTimeOffset.UtcNow;
        Dirty = true;
    }

    public bool IsTransitionActive(DateTimeOffset now)
    {
        if (TransitionKind == SlideTransitionKind.None || TransitionStartedAt is null || TransitionFromSlideIndex is null)
            return false;

        if (now - TransitionStartedAt.Value < TransitionDuration)
            return true;

        EndTransition();
        return false;
    }

    public float GetTransitionProgress(DateTimeOffset now)
    {
        if (!IsTransitionActive(now) || TransitionStartedAt is null)
            return 1.0f;

        var elapsed = now - TransitionStartedAt.Value;
        return (float)Math.Clamp(elapsed.TotalMilliseconds / TransitionDuration.TotalMilliseconds, 0.0, 1.0);
    }

    public void EndTransition()
    {
        TransitionFromSlideIndex = null;
        TransitionKind = SlideTransitionKind.None;
        TransitionStartedAt = null;
    }

    public float BaseScale(int windowWidth, int windowHeight, float slideWidth, float slideHeight)
    {
        if (FillMode)
            return Math.Max(windowWidth / slideWidth, windowHeight / slideHeight);
        return Math.Min(windowWidth / slideWidth, windowHeight / slideHeight);
    }

    private static SlideTransitionKind ParseTransition(string? transition)
    {
        return transition?.Trim().ToLowerInvariant() switch
        {
            "fade" => SlideTransitionKind.Fade,
            "dissolve" => SlideTransitionKind.Dissolve,
            "newsflash" => SlideTransitionKind.Newsflash,
            "cover" => SlideTransitionKind.Cover,
            "pull" => SlideTransitionKind.Pull,
            "push" => SlideTransitionKind.Push,
            "wipe" => SlideTransitionKind.Wipe,
            "blinds" => SlideTransitionKind.Blinds,
            "checker" => SlideTransitionKind.Checker,
            "comb" => SlideTransitionKind.Comb,
            "randombar" => SlideTransitionKind.RandomBar,
            "split" => SlideTransitionKind.Split,
            "strips" => SlideTransitionKind.Strips,
            "circle" => SlideTransitionKind.Circle,
            "cut" => SlideTransitionKind.Cut,
            "diamond" => SlideTransitionKind.Diamond,
            "plus" => SlideTransitionKind.Plus,
            "random" => RandomTransitionKinds[Random.Shared.Next(RandomTransitionKinds.Length)],
            "wedge" => SlideTransitionKind.Wedge,
            "wheel" => SlideTransitionKind.Wheel,
            "zoom" => SlideTransitionKind.Zoom,
            _ => SlideTransitionKind.None,
        };
    }
}
