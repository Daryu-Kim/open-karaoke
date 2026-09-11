using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using OpenKaraoke.Desktop.Media;

namespace OpenKaraoke.Desktop.Views;

/// <summary>
/// Borderless window that fills a customer-facing monitor. MainWindow keeps it on the secondary
/// screen, feeds frames into <see cref="Surface"/> and uses <see cref="SetStatus"/> to explain the
/// screen while nothing is playing (no song yet, probing, or a song without video).
/// The window has no title bar, so it can be dragged with the mouse to work around desktops
/// (e.g. Wayland) that ignore programmatic window placement.
/// </summary>
public partial class VideoWindow : Window
{
    private const int MoveThresholdPixels = 6;

    private bool _dragging;
    private PixelPoint _dragOrigin;
    private Point _dragStart;

    public VideoWindow()
    {
        InitializeComponent();
    }

    /// <summary>Surface the frame decoder writes into.</summary>
    public VideoSurface Surface => VideoCanvas;

    /// <summary>
    /// True once the operator dragged the window, so MainWindow keeps re-applying the size but
    /// leaves the position to the operator.
    /// </summary>
    public bool MovedByUser { get; private set; }

    /// <summary>Shows a Korean status line; an empty text leaves the screen black.</summary>
    public void SetStatus(string text)
    {
        StatusLabel.Text = text;
        StatusLabel.IsVisible = !string.IsNullOrWhiteSpace(text);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragging = true;
        _dragOrigin = Position;
        _dragStart = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_dragging)
        {
            return;
        }

        Point current = e.GetPosition(this);
        int dx = (int)Math.Round(current.X - _dragStart.X);
        int dy = (int)Math.Round(current.Y - _dragStart.Y);

        Position = new PixelPoint(_dragOrigin.X + dx, _dragOrigin.Y + dy);

        if (!MovedByUser && (Math.Abs(dx) >= MoveThresholdPixels || Math.Abs(dy) >= MoveThresholdPixels))
        {
            MovedByUser = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        _dragging = false;
        e.Pointer.Capture(null);
    }
}
