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
var mermaidRenderer = new MermaidInkRenderer(MermaidRenderFormat.Png);
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
var monitors = windowPlatform.GetMonitors().OrderBy(m => m.Index).ToArray();
if (monitors.Length == 0)
    throw new InvalidOperationException("No monitors detected for the current window platform.");
var primaryMonitor = windowPlatform.GetMainMonitor() ?? monitors[0];
var presentationMonitor = monitors.FirstOrDefault(m => m.Index != primaryMonitor.Index) ?? primaryMonitor;
bool hasSecondaryMonitor = presentationMonitor.Index != primaryMonitor.Index;
var presenterParent = presenterWindow.Parent;
IWindow? audienceWindow = hasSecondaryMonitor
    ? presenterParent?.CreateWindow(presenterWindowOptions with
    {
        Title = $"{deck.Header.Title} — DeckMark Presentation",
        IsVisible = false,
        TopMost = true,
    })
    : null;

var isPresentationMode = false;
var commandQueue = new ConcurrentQueue<Action>();
using var shutdownCts = new CancellationTokenSource();

var presenterView = new WindowRenderState(presenterWindow);
WindowRenderState? audienceView = audienceWindow is not null
    ? new WindowRenderState(audienceWindow)
    : null;

PrintCommandHelp();
var consoleTask = Task.Run(() => RunConsoleLoop(shutdownCts.Token));

ConfigureWindow(presenterView, enableInput: true);
if (audienceView is WindowRenderState audienceRenderState)
    ConfigureWindow(audienceRenderState, enableInput: false);

if (audienceWindow is not null)
    audienceWindow.Initialize();

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
            var handler = new InputHandler(state);
            handler.Attach(target.Input);
        }

        RebuildSurface(target, target.Window.Size.X, target.Window.Size.Y);
    };

    target.Window.Resize += size => RebuildSurface(target, size.X, size.Y);
    target.Window.Render += _ => RenderWindow(target);
    target.Window.Closing += target.DisposeGraphics;
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

    if (audienceView is not null && audienceView.Window.IsVisible)
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
            audienceWindow?.Close();
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

void TogglePresentationMode()
{
    if (audienceWindow is null || audienceWindow.IsClosing)
    {
        presenterWindow.WindowState = presenterWindow.WindowState == WindowState.Fullscreen
            ? WindowState.Normal
            : WindowState.Fullscreen;
        state.ResetZoom();
        Console.WriteLine($"Presentation mode {(presenterWindow.WindowState == WindowState.Fullscreen ? "on" : "off")} on primary monitor.");
        return;
    }

    if (isPresentationMode)
    {
        ExitPresentationMode();
        return;
    }

    audienceWindow.WindowState = WindowState.Normal;
    audienceWindow.Monitor = presentationMonitor;
    audienceWindow.IsVisible = true;
    audienceWindow.WindowState = WindowState.Fullscreen;
    state.ResetZoom();
    isPresentationMode = true;

    Console.WriteLine($"Presentation mode on monitor {presentationMonitor.Index}: {presentationMonitor.Name}. Presenter view remains on the primary screen.");
}

void ExitPresentationMode()
{
    if (audienceWindow is null || audienceWindow.IsClosing)
    {
        if (presenterWindow.WindowState != WindowState.Fullscreen)
            return;

        presenterWindow.WindowState = WindowState.Normal;
        state.ResetZoom();
        Console.WriteLine("Presentation mode off.");
        return;
    }

    if (!isPresentationMode)
        return;

    audienceWindow.WindowState = WindowState.Normal;
    audienceWindow.IsVisible = false;
    state.ResetZoom();
    isPresentationMode = false;

    Console.WriteLine("Presentation mode off.");
}

static void PrintCommandHelp()
{
    Console.WriteLine("Terminal keys: Space/Right/Down/N next, Backspace/Left/Up/P previous, +/- zoom, 0 reset, D debug overlay, F fill, F11/M present, Esc/W windowed, S notes, H/? help, Q quit");
}

sealed class WindowRenderState : IDisposable
{
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
        Input?.Dispose();
        Input = null;
        TransitionRenderer?.Dispose();
        TransitionRenderer = null;
        Gl = null;
    }

    public void Dispose()
    {
        DisposeGraphics();
        Window.Dispose();
    }
}
