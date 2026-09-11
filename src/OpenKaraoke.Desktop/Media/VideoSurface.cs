using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace OpenKaraoke.Desktop.Media;

/// <summary>
/// Shows the raw BGRA frames produced by <see cref="VideoPlayback"/>. Avalonia has no built-in
/// video control, so the frames are uploaded into a <see cref="WriteableBitmap"/> and drawn scaled
/// (letterboxed) to the control bounds.
/// </summary>
public sealed class VideoSurface : Control
{
    private WriteableBitmap? _bitmap;
    private bool _hasFrame;

    public VideoSurface()
    {
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    /// <summary>Copies one BGRA frame into the off-screen bitmap. Must run on the UI thread.</summary>
    public void Present(byte[] frame, int width, int height)
    {
        int required = width * height * 4;
        if (width <= 0 || height <= 0 || frame.Length < required)
        {
            return;
        }

        WriteableBitmap? bitmap = EnsureBitmap(width, height);
        if (bitmap is null)
        {
            return;
        }

        using (ILockedFramebuffer locked = bitmap.Lock())
        {
            int rowBytes = Math.Min(locked.RowBytes, width * 4);
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(frame, y * width * 4, locked.Address + (y * locked.RowBytes), rowBytes);
            }
        }

        _hasFrame = true;
        InvalidateVisual();
    }

    /// <summary>Drops the current frame so the next song does not show a stale image.</summary>
    public void Clear()
    {
        _hasFrame = false;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        WriteableBitmap? bitmap = _bitmap;
        if (!_hasFrame || bitmap is null)
        {
            context.FillRectangle(Brushes.Black, bounds);
            return;
        }

        Size source = bitmap.Size;
        double scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
        double width = Math.Max(1, source.Width * scale);
        double height = Math.Max(1, source.Height * scale);
        var destination = new Rect((bounds.Width - width) / 2, (bounds.Height - height) / 2, width, height);
        context.DrawImage(bitmap, new Rect(source), destination);
    }

    private WriteableBitmap? EnsureBitmap(int width, int height)
    {
        if (_bitmap is not null && _bitmap.PixelSize.Width == width && _bitmap.PixelSize.Height == height)
        {
            return _bitmap;
        }

        WriteableBitmap created;
        try
        {
            created = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
        }
        catch (Exception)
        {
            // Out of memory or an unsupported size: keep showing the previous bitmap.
            return _bitmap;
        }

        _bitmap?.Dispose();
        _bitmap = created;
        return _bitmap;
    }
}
