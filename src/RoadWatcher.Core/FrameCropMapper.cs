namespace RoadWatcher.Core;

/// <summary>
/// A rectangle in display coordinates. It deliberately has no UI-framework
/// dependency so every crop surface uses identical letterbox calculations.
/// </summary>
public readonly record struct FrameDisplayRect(double X, double Y, double Width, double Height)
{
    public static FrameDisplayRect FromPoints(double startX, double startY, double endX, double endY) => new(
        Math.Min(startX, endX),
        Math.Min(startY, endY),
        Math.Abs(endX - startX),
        Math.Abs(endY - startY));
}

/// <summary>
/// An integer rectangle in the original captured-frame pixel coordinates.
/// </summary>
public readonly record struct FramePixelRect(int X, int Y, int Width, int Height);

/// <summary>
/// Maps a user drag over a uniformly displayed frame back to its original
/// pixels. Selection is clipped to the visible image, never the letterbox.
/// </summary>
public static class FrameCropMapper
{
    public const double MinimumDisplaySelectionPixels = 4;

    public static FrameDisplayRect GetDisplayedImageRect(
        double viewportWidth,
        double viewportHeight,
        int sourcePixelWidth,
        int sourcePixelHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0 ||
            sourcePixelWidth <= 0 || sourcePixelHeight <= 0)
        {
            return default;
        }

        var scale = Math.Min(
            viewportWidth / sourcePixelWidth,
            viewportHeight / sourcePixelHeight);
        var width = sourcePixelWidth * scale;
        var height = sourcePixelHeight * scale;
        return new FrameDisplayRect(
            (viewportWidth - width) / 2,
            (viewportHeight - height) / 2,
            width,
            height);
    }

    public static bool TryMapSelection(
        FrameDisplayRect selection,
        double viewportWidth,
        double viewportHeight,
        int sourcePixelWidth,
        int sourcePixelHeight,
        out FramePixelRect crop,
        double minimumDisplaySelectionPixels = MinimumDisplaySelectionPixels)
    {
        crop = default;
        if (minimumDisplaySelectionPixels <= 0 ||
            !double.IsFinite(selection.X) || !double.IsFinite(selection.Y) ||
            !double.IsFinite(selection.Width) || !double.IsFinite(selection.Height) ||
            selection.Width < minimumDisplaySelectionPixels ||
            selection.Height < minimumDisplaySelectionPixels)
        {
            return false;
        }

        var image = GetDisplayedImageRect(
            viewportWidth,
            viewportHeight,
            sourcePixelWidth,
            sourcePixelHeight);
        if (image.Width <= 0 || image.Height <= 0)
        {
            return false;
        }

        var left = Math.Max(selection.X, image.X);
        var top = Math.Max(selection.Y, image.Y);
        var right = Math.Min(selection.X + selection.Width, image.X + image.Width);
        var bottom = Math.Min(selection.Y + selection.Height, image.Y + image.Height);
        if (right - left < minimumDisplaySelectionPixels ||
            bottom - top < minimumDisplaySelectionPixels)
        {
            return false;
        }

        var scaleX = sourcePixelWidth / image.Width;
        var scaleY = sourcePixelHeight / image.Height;
        var sourceLeft = Math.Clamp(
            (int)Math.Floor((left - image.X) * scaleX),
            0,
            sourcePixelWidth - 1);
        var sourceTop = Math.Clamp(
            (int)Math.Floor((top - image.Y) * scaleY),
            0,
            sourcePixelHeight - 1);
        var sourceRight = Math.Clamp(
            (int)Math.Ceiling((right - image.X) * scaleX),
            sourceLeft + 1,
            sourcePixelWidth);
        var sourceBottom = Math.Clamp(
            (int)Math.Ceiling((bottom - image.Y) * scaleY),
            sourceTop + 1,
            sourcePixelHeight);
        crop = new FramePixelRect(
            sourceLeft,
            sourceTop,
            sourceRight - sourceLeft,
            sourceBottom - sourceTop);
        return true;
    }
}
