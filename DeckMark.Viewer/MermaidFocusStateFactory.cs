using System.Collections.Generic;

namespace DeckMark.Viewer;

internal static class MermaidFocusStateFactory
{
    public static SlideRenderer.MermaidFocusRenderState? Create(
        int slideIndex,
        int currentSlideIndex,
        IReadOnlyList<SlideRenderer.MermaidOverlayLayout> layouts,
        ViewerState state,
        DateTimeOffset now)
    {
        if (slideIndex != currentSlideIndex || layouts.Count == 0)
            return null;

        bool isAnimating = state.IsMermaidAnimationActive(now);
        if (!isAnimating)
        {
            if (state.FocusedMermaidIndex is not int focusedIndex)
                return null;

            var focusedFrame = CreateFrame(layouts, focusedIndex, 1f);
            return focusedFrame is null
                ? null
                : new SlideRenderer.MermaidFocusRenderState(null, focusedFrame, 1f);
        }

        float progress = state.GetMermaidAnimationProgress(now);
        var fromFrame = CreateFrame(layouts, state.MermaidAnimationFromIndex, 1f - progress);
        var toFrame = CreateFrame(layouts, state.MermaidAnimationToIndex, progress);

        if (fromFrame is null && toFrame is null)
            return null;

        return new SlideRenderer.MermaidFocusRenderState(fromFrame, toFrame, progress);
    }

    private static SlideRenderer.MermaidFocusFrame? CreateFrame(
        IReadOnlyList<SlideRenderer.MermaidOverlayLayout> layouts,
        int? index,
        float progress)
    {
        if (index is null || index < 0 || index >= layouts.Count)
            return null;

        var layout = layouts[index.Value];
        return new SlideRenderer.MermaidFocusFrame(layout.Source, layout.Bounds, progress);
    }
}
