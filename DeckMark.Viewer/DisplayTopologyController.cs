using System.Runtime.InteropServices;
using Silk.NET.Windowing;

#pragma warning disable SYSLIB1054

namespace DeckMark.Viewer;

internal interface IDisplayTopologyController
{
    bool IsSupported { get; }

    bool CanBreakMirroring(IReadOnlyList<IMonitor> monitors);

    bool TryBeginExtendedPresentation(out string message);

    bool TryRestoreDisplayTopology(out string message);
}

internal static class DisplayTopologyController
{
    public static IDisplayTopologyController Create()
    {
        return OperatingSystem.IsWindows()
            ? new WindowsDisplayTopologyController()
            : new NoOpDisplayTopologyController();
    }
}

internal sealed class NoOpDisplayTopologyController : IDisplayTopologyController
{
    public bool IsSupported => false;

    public bool CanBreakMirroring(IReadOnlyList<IMonitor> monitors)
    {
        return false;
    }

    public bool TryBeginExtendedPresentation(out string message)
    {
        message = string.Empty;
        return false;
    }

    public bool TryRestoreDisplayTopology(out string message)
    {
        message = string.Empty;
        return true;
    }
}

internal sealed class WindowsDisplayTopologyController : IDisplayTopologyController
{
    private bool _changedTopology;

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool CanBreakMirroring(IReadOnlyList<IMonitor> monitors)
    {
        return IsSupported;
    }

    public bool TryBeginExtendedPresentation(out string message)
    {
        message = string.Empty;
        if (!IsSupported)
            return false;

        int error = NativeMethods.SetDisplayConfig(
            0,
            nint.Zero,
            0,
            nint.Zero,
            SetDisplayConfigFlags.Apply |
            SetDisplayConfigFlags.TopologyExtend |
            SetDisplayConfigFlags.PathPersistIfRequired);

        if (error != 0)
        {
            message = $"Failed to switch Windows display topology to Extend. Win32 result: {error}.";
            return false;
        }

        _changedTopology = true;
        message = "Attempting to switch Windows to Extend for presentation.";
        return true;
    }

    public bool TryRestoreDisplayTopology(out string message)
    {
        message = string.Empty;
        if (!_changedTopology)
            return true;

        int error = NativeMethods.SetDisplayConfig(
            0,
            nint.Zero,
            0,
            nint.Zero,
            SetDisplayConfigFlags.Apply |
            SetDisplayConfigFlags.TopologyClone |
            SetDisplayConfigFlags.PathPersistIfRequired);

        if (error != 0)
        {
            message = $"Failed to restore Windows display topology to Duplicate. Win32 result: {error}.";
            return false;
        }

        _changedTopology = false;
        message = "Restored duplicated display topology.";
        return true;
    }
    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern int SetDisplayConfig(
            uint numPathArrayElements,
            nint pathArray,
            uint numModeInfoArrayElements,
            nint modeInfoArray,
            SetDisplayConfigFlags flags);
    }

    [Flags]
    private enum SetDisplayConfigFlags : uint
    {
        TopologyClone = 0x00000002,
        TopologyExtend = 0x00000004,
        Apply = 0x00000080,
        PathPersistIfRequired = 0x00000800,
    }

}
