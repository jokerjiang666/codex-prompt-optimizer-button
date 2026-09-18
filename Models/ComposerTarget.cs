using System.Windows;
using System.Windows.Automation;

namespace CodexInputEnhancer.Models;

public sealed record ComposerTarget(
    IntPtr WindowHandle,
    AutomationElement Element,
    Rect Bounds,
    Rect? PermissionAnchorBounds = null);
