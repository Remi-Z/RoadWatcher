using RoadWatcher.Core;
using SkiaSharp;

namespace RoadWatcher.Infrastructure;

public sealed class DominantVehicleColorEstimator : IVehicleColorEstimator
{
    public Task<Suggestion<string>?> EstimateAsync(string cropPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = SKBitmap.Decode(cropPath);
        if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0)
        {
            return Task.FromResult<Suggestion<string>?>(null);
        }

        long red = 0;
        long green = 0;
        long blue = 0;
        var count = 0;
        var xStart = bitmap.Width / 5;
        var xEnd = bitmap.Width * 4 / 5;
        var yStart = bitmap.Height / 5;
        var yEnd = bitmap.Height * 4 / 5;
        var step = Math.Max(1, Math.Min(bitmap.Width, bitmap.Height) / 120);

        for (var y = yStart; y < yEnd; y += step)
        {
            for (var x = xStart; x < xEnd; x += step)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Alpha < 220)
                {
                    continue;
                }

                red += pixel.Red;
                green += pixel.Green;
                blue += pixel.Blue;
                count++;
            }
        }

        if (count == 0)
        {
            return Task.FromResult<Suggestion<string>?>(null);
        }

        var colour = Classify((byte)(red / count), (byte)(green / count), (byte)(blue / count));
        return Task.FromResult<Suggestion<string>?>(new Suggestion<string>(colour, 0.55, "RoadWatcher dominant colour", "1"));
    }

    private static string Classify(byte red, byte green, byte blue)
    {
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var brightness = (red + green + blue) / 3d;
        var saturation = max == 0 ? 0 : (max - min) / (double)max;

        if (brightness < 48) return "Black";
        if (saturation < 0.12 && brightness > 205) return "White";
        if (saturation < 0.16) return brightness < 115 ? "Dark grey" : "Silver / grey";
        if (blue > red * 1.12 && blue > green * 1.08) return brightness < 115 ? "Dark blue" : "Blue";
        if (red > green * 1.18 && red > blue * 1.18) return brightness < 115 ? "Dark red" : "Red";
        if (green > red * 1.12 && green > blue * 1.08) return brightness < 115 ? "Dark green" : "Green";
        if (red > 1.15 * blue && green > 0.75 * red) return "Gold / beige";
        return brightness < 105 ? "Dark colour" : "Other";
    }
}

