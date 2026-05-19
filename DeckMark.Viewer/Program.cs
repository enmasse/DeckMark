using DeckMark.Core.Model;
using DeckMark.Core.Mermaid;
using DeckMark.Core.Parser;
using DeckMark.Viewer;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SkiaSharp;
using System.Collections.Concurrent;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: deckmark-viewer <input.deck.md>");
    return 1;
}

string path = args[0];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"File not found: {path}");
    return 2;
}

DeckDocument deck = DeckMarkParser.Parse(File.ReadAllText(path));

if (deck.Slides.Count == 0)
{
    Console.Error.WriteLine("No slides found.");
    return 3;
}

var state = new ViewerState { SlideCount = deck.Slides.Count };

// Pre-render mermaid diagrams before opening the window
var mermaidRenderer = new PersistentMermaidCacheRenderer(new MermaidInkRenderer(MermaidRenderFormat.Png));
var diagrams = new Dictionary<string, MermaidRenderAsset?>();
foreach (var slide in deck.Slides)
    CollectMermaid(slide.Body);
var renderer = new SlideRenderer(diagrams);
var mermaidLayoutsBySlide = deck.Slides
    .Select((slide, index) => renderer.GetMermaidLayouts(slide, deck.Header, index, deck.Slides.Count))
    .ToArray();

void CollectMermaid(IEnumerable<DeckMark.Core.Model.ContentBlock> blocks)
{
    foreach (var block in blocks)
    {
        if (block.Kind == DeckMark.Core.Model.BlockKind.MermaidBlock && !diagrams.ContainsKey(block.RawContent))
        {
            MermaidRenderAsset? asset = Task.Run(() => mermaidRenderer.RenderAsync(block.RawContent)).GetAwaiter().GetResult();
            diagrams[block.RawContent] = asset;
        }
        if (block.Kind == DeckMark.Core.Model.BlockKind.Columns)
        {
            CollectMermaid(block.Left);
            CollectMermaid(block.Center);
            CollectMermaid(block.Right);
        }
    }
}

PrintNotes(deck.Slides[0], 0, deck.Slides.Count);

// ── Window setup ─────────────────────────────────────────────────────────────

var presenterWindowOptions = WindowOptions.Default with
{
    Title                  = $"{deck.Header.Title} — DeckMark Viewer",
    Position               = new Vector2D<int>(100, 100),
    Size                   = new Vector2D<int>(1280, 720),
    WindowState            = WindowState.Normal,
    ShouldSwapAutomatically = false,
};

IWindow presenterWindow = Window.Create(presenterWindowOptions);
var windowPlatform = Window.GetWindowPlatform(false)
    ?? throw new InvalidOperationException("Unable to resolve the window platform.");
var displayTopology = DisplayTopologyController.Create();
var monitors = Array.Empty<IMonitor>();
IMonitor mainMonitor = null!;
IMonitor presenterMonitor = null!;
IMonitor? presentationMonitor;

RefreshMonitorSelection();

IWindow? audienceWindow = null;

var isPresentationMode = false;
var commandQueue = new ConcurrentQueue<Action>();
using var shutdownCts = new CancellationTokenSource();

var presenterView = new WindowRenderState(presenterWindow);
WindowRenderState? audienceView = null;

PrintCommandHelp();
var consoleTask = Task.Run(() => RunConsoleLoop(shutdownCts.Token));

ConfigureWindow(presenterView, enableInput: true);

presenterWindow.Update += _ =>
{
    while (commandQueue.TryDequeue(out var command))
        command();

    RefreshDirtyFrames();

    if (audienceWindow is not null && !audienceWindow.IsClosing)
    {
        audienceWindow.DoEvents();
        audienceWindow.DoUpdate();
        if (audienceWindow.IsVisible)
            audienceWindow.DoRender();
    }
};

presenterWindow.Run();

if (audienceWindow is not null && !audienceWindow.IsClosing)
    audienceWindow.Close();

shutdownCts.Cancel();
try
{
    consoleTask.GetAwaiter().GetResult();
}
catch (OperationCanceledException)
{
}
presenterView.Dispose();
audienceView?.Dispose();
return 0;

// ── Helpers ───────────────────────────────────────────────────────────────────

void ConfigureWindow(WindowRenderState target, bool enableInput)
{
    target.Window.Load += () =>
    {
        target.Gl = GL.GetApi(target.Window);
        target.TransitionRenderer = new SlideTransitionRenderer(target.Gl, RenderSlide);

        if (enableInput)
        {
            target.Input = target.Window.CreateInput();
            var handler = new InputHandler(state, key =>
            {
                if (TryCreateCommand(new ConsoleKeyInfo('\0', key, false, false, false), out var action))
                    commandQueue.Enqueue(action);
            });
            handler.Attach(target.Input);
        }

        RebuildSurface(target, target.Window.Size.X, target.Window.Size.Y);
    };

    target.Window.Resize += size => RebuildSurface(target, size.X, size.Y);
    target.Window.Render += _ => RenderWindow(target);
}

WindowRenderState EnsureAudienceView()
{
    if (audienceView is not null && audienceWindow is not null && !audienceWindow.IsClosing)
        return audienceView;

    audienceWindow = Window.Create(presenterWindowOptions with
    {
        Title = $"{deck.Header.Title} — DeckMark Presentation",
        IsVisible = false,
        TopMost = true,
        WindowBorder = WindowBorder.Hidden,
    });

    audienceView = new WindowRenderState(audienceWindow);
    ConfigureWindow(audienceView, enableInput: false);
    audienceWindow.Initialize();
    return audienceView;
}

void DisposeAudienceView()
{
    if (audienceWindow is not null && !audienceWindow.IsClosing)
        audienceWindow.Close();

    audienceView?.Dispose();
    audienceView = null;
    audienceWindow = null;
}

IMonitor[] GetOrderedMonitors()
{
    return windowPlatform.GetMonitors().OrderBy(m => m.Index).ToArray();
}

IMonitor GetPresenterMonitor(IReadOnlyList<IMonitor> availableMonitors)
{
    return availableMonitors.FirstOrDefault(m =>
    {
        var bounds = m.Bounds;
        return presenterWindowOptions.Position.X >= bounds.Origin.X &&
               presenterWindowOptions.Position.X < bounds.Origin.X + bounds.Size.X &&
               presenterWindowOptions.Position.Y >= bounds.Origin.Y &&
               presenterWindowOptions.Position.Y < bounds.Origin.Y + bounds.Size.Y;
    }) ?? mainMonitor;
}

void RefreshMonitorSelection()
{
    monitors = GetOrderedMonitors();
    if (monitors.Length == 0)
        throw new InvalidOperationException("No monitors detected for the current window platform.");

    mainMonitor = windowPlatform.GetMainMonitor() ?? monitors[0];
    presenterMonitor = GetPresenterMonitor(monitors);
    presentationMonitor = monitors.FirstOrDefault(IsUsablePresentationMonitor);
}

bool WaitForPresentationMonitor(TimeSpan timeout, TimeSpan pollInterval)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    int? stableMonitorIndex = null;
    Rectangle<int> stableBounds = default;
    int stablePollCount = 0;

    do
    {
        RefreshMonitorSelection();

        if (presentationMonitor is not null && HasUsableBounds(presentationMonitor.Bounds))
        {
            var bounds = presentationMonitor.Bounds;
            if (stableMonitorIndex == presentationMonitor.Index &&
                bounds.Origin == stableBounds.Origin &&
                bounds.Size == stableBounds.Size)
            {
                stablePollCount++;
            }
            else
            {
                stableMonitorIndex = presentationMonitor.Index;
                stableBounds = bounds;
                stablePollCount = 1;
            }

            if (stablePollCount >= 3)
                return true;
        }
        else
        {
            stableMonitorIndex = null;
            stableBounds = default;
            stablePollCount = 0;
        }

        Thread.Sleep(pollInterval);
    }
    while (DateTimeOffset.UtcNow < deadline);

    RefreshMonitorSelection();
    return presentationMonitor is not null && HasUsableBounds(presentationMonitor.Bounds);
}

void RefreshDirtyFrames()
{
    var now = DateTimeOffset.UtcNow;
    if (state.IsTransitionActive(now))
        state.Dirty = true;

    bool hadMermaidAnimation = state.MermaidAnimationStartedAt is not null;
    if (state.IsMermaidAnimationActive(now))
    {
        presenterView.TransitionRenderer?.InvalidateTextures();
        audienceView?.TransitionRenderer?.InvalidateTextures();
        state.Dirty = true;
    }
    else if (hadMermaidAnimation && state.MermaidAnimationStartedAt is null)
    {
        presenterView.TransitionRenderer?.InvalidateTextures();
        audienceView?.TransitionRenderer?.InvalidateTextures();
        state.Dirty = true;
    }

    if (!state.Dirty)
        return;

    state.Dirty = false;
    presenterView.DirtyFrames = 2;

    if (audienceView?.Window.IsVisible == true)
        audienceView.DirtyFrames = 2;
}

void RebuildSurface(WindowRenderState target, int width, int height)
{
    if (width <= 0 || height <= 0) return;

    target.TransitionRenderer?.InvalidateTextures();
    state.Dirty = true;
}

void RenderWindow(WindowRenderState target)
{
    if (target.DirtyFrames <= 0 || target.Gl is null || target.TransitionRenderer is null)
        return;

    target.DirtyFrames--;

    int w = target.Window.Size.X;
    int h = target.Window.Size.Y;

    var transitionNow = DateTimeOffset.UtcNow;
    if (state.IsTransitionActive(transitionNow) &&
        state.TransitionFromSlideIndex is int fromSlideIndex &&
        fromSlideIndex >= 0 &&
        fromSlideIndex < deck.Slides.Count)
    {
        var progress = state.GetTransitionProgress(transitionNow);
        target.TransitionRenderer.RenderTransition(w, h, state.TransitionKind, fromSlideIndex, state.SlideIndex, progress, state.Zoom, state.Pan, state.FillMode);
    }
    else
    {
        target.TransitionRenderer.RenderSlide(w, h, state.SlideIndex, state.Zoom, state.Pan, state.FillMode);
    }
    target.Window.SwapBuffers();
}

void RenderSlide(SKCanvas canvas, int slideIndex)
{
    var slide = deck.Slides[slideIndex];
    var mermaidFocus = CreateMermaidFocusRenderState(slideIndex);
    if (mermaidFocus is null)
        renderer.Draw(canvas, slide, deck.Header, slideIndex, deck.Slides.Count, includeMermaid: true, showLayoutDebugOverlay: state.ShowLayoutDebugOverlay);
    else
        renderer.Draw(canvas, slide, deck.Header, slideIndex, deck.Slides.Count, mermaidFocus, showLayoutDebugOverlay: state.ShowLayoutDebugOverlay);
}

SlideRenderer.MermaidFocusRenderState? CreateMermaidFocusRenderState(int slideIndex)
{
    var layouts = slideIndex >= 0 && slideIndex < mermaidLayoutsBySlide.Length
        ? mermaidLayoutsBySlide[slideIndex]
        : [];

    return MermaidFocusStateFactory.Create(slideIndex, state.SlideIndex, layouts, state, DateTimeOffset.UtcNow);
}

static void PrintNotes(Slide slide, int index, int total)
{
    Console.WriteLine();
    Console.WriteLine($"─── Slide {index + 1}/{total}: {slide.Title} ───");
    foreach (var note in slide.Notes)
    {
        if (!string.IsNullOrWhiteSpace(note.RawContent))
            Console.WriteLine(note.RawContent.Trim());
    }
    if (slide.Notes.Count == 0 || slide.Notes.All(n => string.IsNullOrWhiteSpace(n.RawContent)))
        Console.WriteLine("(no notes)");
}

void RunConsoleLoop(CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        if (!Console.KeyAvailable)
        {
            Thread.Sleep(25);
            continue;
        }

        ConsoleKeyInfo keyInfo;
        try
        {
            keyInfo = Console.ReadKey(intercept: true);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (TryCreateCommand(keyInfo, out var action))
        {
            commandQueue.Enqueue(action);
            continue;
        }

        if (keyInfo.KeyChar is not '\0' && !char.IsControl(keyInfo.KeyChar))
            Console.WriteLine($"Unknown key: {keyInfo.KeyChar}");
    }
}

bool TryCreateCommand(ConsoleKeyInfo keyInfo, out Action action)
{
    action = keyInfo.Key switch
    {
        ConsoleKey.Spacebar or ConsoleKey.RightArrow or ConsoleKey.DownArrow or ConsoleKey.N => () =>
        {
            if (state.AdvanceMermaidFocus(GetMermaidCount(state.SlideIndex)))
            {
                presenterView.TransitionRenderer?.InvalidateTextures();
                audienceView?.TransitionRenderer?.InvalidateTextures();
                return;
            }

            int previousSlideIndex = state.SlideIndex;
            if (state.Next())
            {
                state.ResetMermaidFocus();
                state.BeginTransition(previousSlideIndex, ResolveTransition(previousSlideIndex));
                OnSlideChanged();
            }
        },
        ConsoleKey.Backspace or ConsoleKey.LeftArrow or ConsoleKey.UpArrow or ConsoleKey.P => () =>
        {
            if (state.RetreatMermaidFocus())
            {
                presenterView.TransitionRenderer?.InvalidateTextures();
                audienceView?.TransitionRenderer?.InvalidateTextures();
                return;
            }

            int previousSlideIndex = state.SlideIndex;
            if (state.Previous())
            {
                state.FocusLastMermaid(GetMermaidCount(state.SlideIndex));
                presenterView.TransitionRenderer?.InvalidateTextures();
                audienceView?.TransitionRenderer?.InvalidateTextures();
                state.BeginTransition(previousSlideIndex, ResolveTransition(previousSlideIndex));
                OnSlideChanged();
            }
        },
        ConsoleKey.OemPlus or ConsoleKey.Add => () => state.ZoomIn(),
        ConsoleKey.OemMinus or ConsoleKey.Subtract => () => state.ZoomOut(),
        ConsoleKey.D0 or ConsoleKey.NumPad0 => () => state.ResetZoom(),
        ConsoleKey.D => () =>
        {
            state.ToggleLayoutDebugOverlay();
            presenterView.TransitionRenderer?.InvalidateTextures();
            audienceView?.TransitionRenderer?.InvalidateTextures();
        },
        ConsoleKey.F => () => state.ToggleFill(),
        ConsoleKey.F11 or ConsoleKey.M => () => TogglePresentationMode(),
        ConsoleKey.Escape or ConsoleKey.W => () => ExitPresentationMode(),
        ConsoleKey.H or ConsoleKey.Oem2 => () => PrintCommandHelp(),
        ConsoleKey.S => () => PrintNotes(deck.Slides[state.SlideIndex], state.SlideIndex, deck.Slides.Count),
        ConsoleKey.Q => () =>
        {
            DisposeAudienceView();
            presenterWindow.Close();
        },
        _ => null!,
    };

    return action is not null;
}

string? ResolveTransition(int slideIndex)
{
    for (int i = slideIndex; i >= 0; i--)
    {
        var transition = deck.Slides[i].Transition;
        if (!string.IsNullOrWhiteSpace(transition))
            return transition;
    }

    return null;
}

void OnSlideChanged()
{
    PrintNotes(deck.Slides[state.SlideIndex], state.SlideIndex, deck.Slides.Count);
}

int GetMermaidCount(int slideIndex)
{
    if (slideIndex < 0 || slideIndex >= mermaidLayoutsBySlide.Length)
        return 0;

    return mermaidLayoutsBySlide[slideIndex].Count;
}

static bool HasUsableBounds(Rectangle<int> bounds)
{
    return bounds.Size.X > 0 && bounds.Size.Y > 0;
}

bool IsUsablePresentationMonitor(IMonitor monitor)
{
    if (monitor.Index == mainMonitor.Index)
        return false;

    var mainBounds = mainMonitor.Bounds;
    var candidateBounds = monitor.Bounds;
    bool mainHasUsableBounds = HasUsableBounds(mainBounds);

    if (!HasUsableBounds(candidateBounds))
        return false;

    if (!mainHasUsableBounds)
        return true;

    return candidateBounds.Origin != mainBounds.Origin ||
           candidateBounds.Size != mainBounds.Size;
}

static Vector2D<int> GetPresentationWindowSize(IMonitor monitor)
{
    var resolution = monitor.VideoMode.Resolution;
    return resolution is { X: > 0, Y: > 0 } fullResolution
        ? fullResolution
        : monitor.Bounds.Size;
}

void TogglePresentationMode()
{
    Console.WriteLine("Presentation mode is temporarily disabled.");
}

void ExitPresentationMode()
{
    if (audienceWindow is null || audienceWindow.IsClosing)
        return;

    if (!isPresentationMode)
        return;

    DisposeAudienceView();

    if (!displayTopology.TryRestoreDisplayTopology(out var topologyMessage))
        Console.WriteLine(topologyMessage);
    else if (!string.IsNullOrWhiteSpace(topologyMessage))
        Console.WriteLine(topologyMessage);

    RefreshMonitorSelection();
    state.ResetZoom();
    presenterView.TransitionRenderer?.InvalidateTextures();
    isPresentationMode = false;

    Console.WriteLine("Presentation mode off.");
}

static void PrintCommandHelp()
{
    Console.WriteLine("Terminal keys: Space/Right/Down/N next, Backspace/Left/Up/P previous, +/- zoom, 0 reset, D debug overlay, F fill, F11/M presentation disabled, Esc/W windowed, S notes, H/? help, Q quit");
}

sealed class WindowRenderState : IDisposable
{
    private bool _graphicsDisposed;

    public WindowRenderState(IWindow window)
    {
        Window = window;
    }

    public IWindow Window { get; }
    public GL? Gl { get; set; }
    public SlideTransitionRenderer? TransitionRenderer { get; set; }
    public IInputContext? Input { get; set; }
    public int DirtyFrames { get; set; }

    public void DisposeGraphics()
    {
        if (_graphicsDisposed)
            return;

        Input?.Dispose();
        Input = null;
        TransitionRenderer?.Dispose();
        TransitionRenderer = null;
        Gl = null;
        _graphicsDisposed = true;
    }

    public void Dispose()
    {
        DisposeGraphics();
        Window.Dispose();
    }
}
