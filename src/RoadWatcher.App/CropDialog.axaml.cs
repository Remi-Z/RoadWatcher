using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using RoadWatcher.Core;
using SkiaSharp;

namespace RoadWatcher.App;

public sealed partial class CropDialog : Window
{
    private readonly string _sourcePath;
    private readonly string _destinationPath;
    private Bitmap? _bitmap;
    private Point? _dragStart;
    private Rect _selection;

    public CropDialog() : this(string.Empty, string.Empty)
    {
    }

    public CropDialog(string sourcePath, string destinationPath)
    {
        _sourcePath = sourcePath;
        _destinationPath = destinationPath;
        InitializeComponent();
        Opened += (_, _) => LoadSource();
        Closed += (_, _) => _bitmap?.Dispose();
    }

    private void LoadSource()
    {
        if (string.IsNullOrWhiteSpace(_sourcePath))
        {
            return;
        }

        _bitmap = new Bitmap(_sourcePath);
        SourceImage.Source = _bitmap;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (!eventArgs.GetCurrentPoint(Surface).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragStart = Clamp(eventArgs.GetPosition(Surface));
        _selection = new Rect(_dragStart.Value, new Size(0, 0));
        eventArgs.Pointer.Capture(Surface);
        UpdateSelectionVisual();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (_dragStart is null || !eventArgs.GetCurrentPoint(Surface).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var current = Clamp(eventArgs.GetPosition(Surface));
        var left = Math.Min(_dragStart.Value.X, current.X);
        var top = Math.Min(_dragStart.Value.Y, current.Y);
        _selection = new Rect(left, top, Math.Abs(current.X - _dragStart.Value.X), Math.Abs(current.Y - _dragStart.Value.Y));
        UpdateSelectionVisual();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        _dragStart = null;
        eventArgs.Pointer.Capture(null);
        UpdateSelectionVisual();
    }

    private void UpdateSelectionVisual()
    {
        SelectionBorder.IsVisible = _selection.Width > 1 && _selection.Height > 1;
        Canvas.SetLeft(SelectionBorder, _selection.X);
        Canvas.SetTop(SelectionBorder, _selection.Y);
        SelectionBorder.Width = _selection.Width;
        SelectionBorder.Height = _selection.Height;
        StatusText.Text = SelectionBorder.IsVisible
            ? $"Selection  {_selection.Width:0} × {_selection.Height:0} display pixels"
            : "Drag to create a selection";
    }

    private void OnSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (_bitmap is null || _selection.Width < 4 || _selection.Height < 4)
        {
            StatusText.Text = "Select a vehicle or plate before saving.";
            return;
        }

        if (!FrameCropMapper.TryMapSelection(
                new FrameDisplayRect(_selection.X, _selection.Y, _selection.Width, _selection.Height),
                Surface.Bounds.Width,
                Surface.Bounds.Height,
                _bitmap.PixelSize.Width,
                _bitmap.PixelSize.Height,
                out var cropBounds))
        {
            StatusText.Text = "The selection must overlap the evidence frame.";
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_destinationPath)
            ?? throw new InvalidOperationException("Crop destination directory is missing."));
        using var source = SKBitmap.Decode(_sourcePath)
            ?? throw new InvalidDataException("The captured frame could not be decoded.");
        using var crop = new SKBitmap(cropBounds.Width, cropBounds.Height);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.DrawBitmap(
                source,
                new SKRectI(
                    cropBounds.X,
                    cropBounds.Y,
                    cropBounds.X + cropBounds.Width,
                    cropBounds.Y + cropBounds.Height),
                new SKRect(0, 0, cropBounds.Width, cropBounds.Height));
        }

        using var image = SKImage.FromBitmap(crop);
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        using var stream = File.Create(_destinationPath);
        data.SaveTo(stream);
        Close(true);
    }

    private void OnCancelClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs) => Close(false);

    private Point Clamp(Point point) => new(
        Math.Clamp(point.X, 0, Surface.Bounds.Width),
        Math.Clamp(point.Y, 0, Surface.Bounds.Height));
}
