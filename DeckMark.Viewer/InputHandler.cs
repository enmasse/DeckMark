using Silk.NET.Input;

namespace DeckMark.Viewer;

/// <summary>
/// Wires mouse input to <see cref="ViewerState"/>.
/// </summary>
internal sealed class InputHandler
{
    private readonly ViewerState _state;
    private readonly Action<ConsoleKey> _commandSink;

    private bool _dragging;
    private System.Numerics.Vector2 _lastMouse;

    public InputHandler(ViewerState state, Action<ConsoleKey> commandSink)
    {
        _state = state;
        _commandSink = commandSink;
    }

    public void Attach(IInputContext input)
    {
        foreach (var keyboard in input.Keyboards)
            keyboard.KeyDown += OnKeyDown;

        foreach (var mouse in input.Mice)
        {
            mouse.Scroll      += OnScroll;
            mouse.MouseDown   += OnMouseDown;
            mouse.MouseUp     += OnMouseUp;
            mouse.MouseMove   += OnMouseMove;
        }
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scanCode)
    {
        if (TryMapConsoleKey(key, out var consoleKey))
            _commandSink(consoleKey);
    }

    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        _state.ZoomDelta(wheel.Y);
    }

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            _dragging  = true;
            _lastMouse = mouse.Position;
        }
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left)
            _dragging = false;
    }

    private void OnMouseMove(IMouse mouse, System.Numerics.Vector2 pos)
    {
        if (!_dragging) return;
        float dx = pos.X - _lastMouse.X;
        float dy = pos.Y - _lastMouse.Y;
        _state.ApplyPan(dx, dy);
        _lastMouse = pos;
    }

    private static bool TryMapConsoleKey(Key key, out ConsoleKey consoleKey)
    {
        consoleKey = key switch
        {
            Key.Space => ConsoleKey.Spacebar,
            Key.Right => ConsoleKey.RightArrow,
            Key.Down => ConsoleKey.DownArrow,
            Key.N => ConsoleKey.N,
            Key.Backspace => ConsoleKey.Backspace,
            Key.Left => ConsoleKey.LeftArrow,
            Key.Up => ConsoleKey.UpArrow,
            Key.P => ConsoleKey.P,
            Key.Equal or Key.KeypadAdd => ConsoleKey.OemPlus,
            Key.Minus or Key.KeypadSubtract => ConsoleKey.OemMinus,
            Key.Number0 or Key.Keypad0 => ConsoleKey.D0,
            Key.D => ConsoleKey.D,
            Key.F => ConsoleKey.F,
            Key.F11 => ConsoleKey.F11,
            Key.M => ConsoleKey.M,
            Key.Escape => ConsoleKey.Escape,
            Key.W => ConsoleKey.W,
            Key.H or Key.Slash => ConsoleKey.H,
            Key.S => ConsoleKey.S,
            Key.Q => ConsoleKey.Q,
            _ => default,
        };

        return consoleKey != default;
    }
}
