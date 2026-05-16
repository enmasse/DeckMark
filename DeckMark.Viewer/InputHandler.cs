using Silk.NET.Input;

namespace DeckMark.Viewer;

/// <summary>
/// Wires mouse input to <see cref="ViewerState"/>.
/// </summary>
internal sealed class InputHandler
{
    private readonly ViewerState _state;

    private bool _dragging;
    private System.Numerics.Vector2 _lastMouse;

    public InputHandler(ViewerState state)
    {
        _state = state;
    }

    public void Attach(IInputContext input)
    {
        foreach (var mouse in input.Mice)
        {
            mouse.Scroll      += OnScroll;
            mouse.MouseDown   += OnMouseDown;
            mouse.MouseUp     += OnMouseUp;
            mouse.MouseMove   += OnMouseMove;
        }
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
}
