using System.Globalization;
using System.Text.Json;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace DashcamEvidence.Core;

public readonly record struct CvPoint(int X, int Y);

public readonly record struct CvRect(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public CvPoint Center => new(X + (Width / 2), Y + (Height / 2));
}

public enum VehicleRoadPosition
{
    Unknown,
    InBikeLane,
    NearBikeLane,
    OtherLane
}

public sealed record CvAnalysisOptions(
    double ScanFps,
    float MinConfidence,
    string? VehicleModelPath,
    string? VehicleLabelsPath,
    string OutputRoot)
{
    public static CvAnalysisOptions Default() => new(2, 0.35f, null, null, DefaultOutputRoot());

    public static CvAnalysisOptions FromEnvironment()
    {
        var defaults = Default();

        return new CvAnalysisOptions(
            ReadDouble("DASHCAM_CV_SCAN_FPS", defaults.ScanFps),
            ReadFloat("DASHCAM_CV_MIN_CONFIDENCE", defaults.MinConfidence),
            BlankToNull(Environment.GetEnvironmentVariable("DASHCAM_CV_VEHICLE_MODEL")),
            BlankToNull(Environment.GetEnvironmentVariable("DASHCAM_CV_VEHICLE_LABELS")),
            defaults.OutputRoot);
    }

    public string OutputFolder(string recordingId) => Path.Combine(OutputRoot, recordingId);

    private static double ReadDouble(string name, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallback;

    private static float ReadFloat(string name, float fallback) =>
        float.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallback;

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string DefaultOutputRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DashcamEvidence",
        "scans");
}

public sealed record BikeLaneDetection(IReadOnlyList<CvPoint> Polygon, float Confidence);

public sealed record VehicleDetection(CvRect Bounds, string Label, float Confidence, VehicleRoadPosition RoadPosition);

public sealed record FrameDetection(
    TimeSpan Offset,
    string AnnotatedFramePath,
    BikeLaneDetection? BikeLane,
    IReadOnlyList<VehicleDetection> Vehicles,
    string Source);

public sealed record VehicleTrack(
    string Id,
    TimeSpan FirstOffset,
    TimeSpan LastOffset,
    string Label,
    float Confidence,
    VehicleRoadPosition RoadPosition,
    string BestCropPath,
    string Plate,
    string VehicleNotes,
    string Source);

public sealed record RecordingScanResult(
    string RecordingId,
    string VideoPath,
    string OutputFolder,
    IReadOnlyList<FrameDetection> Frames,
    IReadOnlyList<VehicleTrack> VehicleTracks,
    string Source);

public static class OpenCvRecordingScanner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<RecordingScanResult> ScanAsync(
        Recording recording,
        CvAnalysisOptions? options = null,
        Func<IReadOnlyList<FrameSample>, CancellationToken, Task<SmartInspectResult?>>? analyzeVehicleDetails = null,
        CancellationToken cancellationToken = default)
    {
        options ??= CvAnalysisOptions.Default();
        var outputFolder = options.OutputFolder(recording.Id);
        Directory.CreateDirectory(outputFolder);

        using var capture = new VideoCapture(recording.SourcePath);
        if (!capture.IsOpened())
        {
            return await SaveAsync(new RecordingScanResult(
                recording.Id,
                recording.SourcePath,
                outputFolder,
                [],
                [],
                "opencv video open failed"),
                cancellationToken);
        }

        var vehicleDetector = VehicleDetector.TryCreate(options, out var modelSource);
        var videoFps = capture.Get(VideoCaptureProperties.Fps);
        if (videoFps <= 0)
        {
            videoFps = Math.Max(options.ScanFps, 1);
        }

        var frameCount = (long)Math.Max(capture.Get(VideoCaptureProperties.FrameCount), 0);
        var step = Math.Max(1, (int)Math.Round(videoFps / Math.Max(options.ScanFps, 0.1)));
        var frames = new List<FrameDetection>();
        var trackFrames = new List<(TimeSpan Offset, VehicleDetection Vehicle, string CropPath)>();

        using var frame = new Mat();
        for (long frameIndex = 0; frameCount == 0 || frameIndex < frameCount; frameIndex += step)
        {
            cancellationToken.ThrowIfCancellationRequested();
            capture.Set(VideoCaptureProperties.PosFrames, frameIndex);
            if (!capture.Read(frame) || frame.Empty())
            {
                break;
            }

            var offset = TimeSpan.FromSeconds(frameIndex / videoFps);
            var lane = DetectBikeLane(frame);
            var vehicles = vehicleDetector.Detect(frame)
                .Select(vehicle => vehicle with { RoadPosition = ClassifyVehiclePosition(vehicle.Bounds, lane) })
                .ToArray();

            foreach (var vehicle in vehicles)
            {
                var cropPath = SaveVehicleCrop(frame, vehicle, outputFolder, frames.Count + 1, trackFrames.Count + 1);
                trackFrames.Add((offset, vehicle, cropPath));
            }

            var annotatedPath = Path.Combine(outputFolder, $"frame-{frames.Count + 1:0000}-{Math.Round(offset.TotalMilliseconds)}ms.jpg");
            SaveAnnotatedFrame(frame, lane, vehicles, annotatedPath);
            frames.Add(new FrameDetection(offset, annotatedPath, lane, vehicles, modelSource));
        }

        var tracks = await BuildTracksAsync(trackFrames, analyzeVehicleDetails, cancellationToken);
        vehicleDetector.Dispose();

        return await SaveAsync(new RecordingScanResult(
            recording.Id,
            recording.SourcePath,
            outputFolder,
            frames,
            tracks,
            $"{frames.Count} sampled frames; {modelSource}"),
            cancellationToken);

        async Task<RecordingScanResult> SaveAsync(RecordingScanResult result, CancellationToken token)
        {
            var scanPath = Path.Combine(outputFolder, "scan.json");
            await File.WriteAllTextAsync(scanPath, JsonSerializer.Serialize(result, JsonOptions), token);
            return result;
        }
    }

    public static string VehicleModelStatus(CvAnalysisOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.VehicleModelPath) || !File.Exists(options.VehicleModelPath))
        {
            return "vehicle model missing; set DASHCAM_CV_VEHICLE_MODEL to an ONNX model path";
        }

        if (string.IsNullOrWhiteSpace(options.VehicleLabelsPath) || !File.Exists(options.VehicleLabelsPath))
        {
            return "vehicle labels missing; set DASHCAM_CV_VEHICLE_LABELS to a label file path";
        }

        return "vehicle model configured";
    }

    public static VehicleRoadPosition ClassifyVehiclePosition(CvRect vehicle, BikeLaneDetection? bikeLane)
    {
        if (bikeLane is null || bikeLane.Polygon.Count < 3)
        {
            return VehicleRoadPosition.Unknown;
        }

        var center = vehicle.Center;
        if (PointInPolygon(center, bikeLane.Polygon))
        {
            return VehicleRoadPosition.InBikeLane;
        }

        var minDistance = bikeLane.Polygon
            .Select((point, index) => DistanceToSegment(center, point, bikeLane.Polygon[(index + 1) % bikeLane.Polygon.Count]))
            .Min();

        return minDistance <= Math.Max(vehicle.Width, vehicle.Height) * 1.5
            ? VehicleRoadPosition.NearBikeLane
            : VehicleRoadPosition.OtherLane;
    }

    private static BikeLaneDetection? DetectBikeLane(Mat frame)
    {
        using var hsv = new Mat();
        using var greenMask = new Mat();
        Cv2.CvtColor(frame, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, new Scalar(35, 35, 35), new Scalar(95, 255, 255), greenMask);
        Cv2.FindContours(greenMask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var contour = contours
            .Where(candidate => Cv2.ContourArea(candidate) > frame.Width * frame.Height * 0.01)
            .OrderByDescending(candidate => Cv2.ContourArea(candidate))
            .FirstOrDefault();
        if (contour is null)
        {
            return null;
        }

        var epsilon = 0.02 * Cv2.ArcLength(contour, true);
        var approx = Cv2.ApproxPolyDP(contour, epsilon, true);
        var points = approx.Length >= 3
            ? approx.Select(point => new CvPoint(point.X, point.Y)).ToArray()
            : Cv2.BoundingRect(contour).ToPolygon();

        return new BikeLaneDetection(points, 0.75f);
    }

    private static void SaveAnnotatedFrame(Mat frame, BikeLaneDetection? lane, IReadOnlyList<VehicleDetection> vehicles, string path)
    {
        using var annotated = frame.Clone();
        if (lane is not null)
        {
            var polygon = lane.Polygon.Select(point => new Point(point.X, point.Y)).ToArray();
            Cv2.Polylines(annotated, [polygon], true, Scalar.LimeGreen, 3);
        }

        foreach (var vehicle in vehicles)
        {
            var color = vehicle.RoadPosition == VehicleRoadPosition.InBikeLane ? Scalar.Red : Scalar.DeepSkyBlue;
            Cv2.Rectangle(annotated, vehicle.Bounds.ToRect(), color, 2);
            Cv2.PutText(
                annotated,
                $"{vehicle.Label} {vehicle.Confidence:0.00} {vehicle.RoadPosition}",
                new Point(vehicle.Bounds.Left, Math.Max(18, vehicle.Bounds.Top - 6)),
                HersheyFonts.HersheySimplex,
                0.5,
                color,
                1);
        }

        Cv2.ImWrite(path, annotated);
    }

    private static string SaveVehicleCrop(Mat frame, VehicleDetection vehicle, string outputFolder, int frameNumber, int vehicleNumber)
    {
        var cropsFolder = Path.Combine(outputFolder, "crops");
        Directory.CreateDirectory(cropsFolder);
        var cropPath = Path.Combine(cropsFolder, $"vehicle-{frameNumber:0000}-{vehicleNumber:0000}.jpg");
        var rect = vehicle.Bounds.ToRect().Intersect(new Rect(0, 0, frame.Width, frame.Height));
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return "";
        }

        using var crop = new Mat(frame, rect);
        Cv2.ImWrite(cropPath, crop);
        return cropPath;
    }

    private static async Task<IReadOnlyList<VehicleTrack>> BuildTracksAsync(
        IReadOnlyList<(TimeSpan Offset, VehicleDetection Vehicle, string CropPath)> detections,
        Func<IReadOnlyList<FrameSample>, CancellationToken, Task<SmartInspectResult?>>? analyzeVehicleDetails,
        CancellationToken cancellationToken)
    {
        var tracks = new List<VehicleTrack>();
        for (var index = 0; index < detections.Count; index++)
        {
            var detection = detections[index];
            SmartInspectResult? details = null;
            if (analyzeVehicleDetails is not null && !string.IsNullOrWhiteSpace(detection.CropPath))
            {
                details = await analyzeVehicleDetails([new FrameSample(detection.Offset, detection.CropPath)], cancellationToken);
            }

            tracks.Add(new VehicleTrack(
                $"vehicle-{index + 1:0000}",
                detection.Offset,
                detection.Offset,
                detection.Vehicle.Label,
                detection.Vehicle.Confidence,
                detection.Vehicle.RoadPosition,
                detection.CropPath,
                details?.Plate ?? "",
                details?.VehicleNotes ?? "",
                details?.Source ?? "local crop"));
        }

        return tracks;
    }

    private static bool PointInPolygon(CvPoint point, IReadOnlyList<CvPoint> polygon)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];
            if ((pi.Y > point.Y) != (pj.Y > point.Y) &&
                point.X < (double)(pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y) + pi.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static double DistanceToSegment(CvPoint point, CvPoint start, CvPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (dx == 0 && dy == 0)
        {
            return Distance(point, start);
        }

        var t = Math.Max(0, Math.Min(1, ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / (double)(dx * dx + dy * dy)));
        var projection = new CvPoint((int)Math.Round(start.X + t * dx), (int)Math.Round(start.Y + t * dy));
        return Distance(point, projection);
    }

    private static double Distance(CvPoint left, CvPoint right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private sealed class VehicleDetector : IDisposable
    {
        private static readonly HashSet<string> VehicleLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            "car",
            "truck",
            "bus",
            "motorcycle",
            "motorbike",
            "van"
        };

        private readonly Net? _net;
        private readonly string[] _labels;
        private readonly float _minConfidence;

        private VehicleDetector(Net? net, string[] labels, float minConfidence)
        {
            _net = net;
            _labels = labels;
            _minConfidence = minConfidence;
        }

        public static VehicleDetector TryCreate(CvAnalysisOptions options, out string source)
        {
            var status = VehicleModelStatus(options);
            if (!string.Equals(status, "vehicle model configured", StringComparison.OrdinalIgnoreCase))
            {
                source = status;
                return new VehicleDetector(null, [], options.MinConfidence);
            }

            try
            {
                source = "opencv dnn";
                return new VehicleDetector(
                    CvDnn.ReadNetFromOnnx(options.VehicleModelPath!),
                    File.ReadAllLines(options.VehicleLabelsPath!).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray(),
                    options.MinConfidence);
            }
            catch (Exception ex) when (ex is OpenCVException or IOException or ArgumentException)
            {
                source = $"vehicle model failed: {ex.Message}";
                return new VehicleDetector(null, [], options.MinConfidence);
            }
        }

        public IReadOnlyList<VehicleDetection> Detect(Mat frame)
        {
            if (_net is null || _labels.Length == 0)
            {
                return [];
            }

            using var blob = CvDnn.BlobFromImage(frame, 1 / 255.0, new Size(640, 640), new Scalar(), true, false);
            _net.SetInput(blob);
            using var output = _net.Forward();
            return ParseYoloOutput(output, frame.Width, frame.Height);
        }

        public void Dispose() => _net?.Dispose();

        private IReadOnlyList<VehicleDetection> ParseYoloOutput(Mat output, int frameWidth, int frameHeight)
        {
            if (output.Dims < 3)
            {
                return [];
            }

            var rows = output.Size(1);
            var dimensions = output.Size(2);
            if (rows < dimensions)
            {
                (rows, dimensions) = (dimensions, rows);
            }

            var detections = new List<VehicleDetection>();
            for (var row = 0; row < rows; row++)
            {
                var bestClass = -1;
                var bestScore = 0f;
                for (var classIndex = 4; classIndex < dimensions; classIndex++)
                {
                    var score = ReadOutput(output, row, classIndex, rows, dimensions);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestClass = classIndex - 4;
                    }
                }

                if (bestScore < _minConfidence || bestClass < 0 || bestClass >= _labels.Length || !VehicleLabels.Contains(_labels[bestClass]))
                {
                    continue;
                }

                var cx = ReadOutput(output, row, 0, rows, dimensions) * frameWidth / 640f;
                var cy = ReadOutput(output, row, 1, rows, dimensions) * frameHeight / 640f;
                var width = ReadOutput(output, row, 2, rows, dimensions) * frameWidth / 640f;
                var height = ReadOutput(output, row, 3, rows, dimensions) * frameHeight / 640f;
                var rect = new CvRect(
                    Math.Max(0, (int)Math.Round(cx - width / 2)),
                    Math.Max(0, (int)Math.Round(cy - height / 2)),
                    Math.Max(1, (int)Math.Round(width)),
                    Math.Max(1, (int)Math.Round(height)));
                detections.Add(new VehicleDetection(rect, _labels[bestClass], bestScore, VehicleRoadPosition.Unknown));
            }

            return detections;
        }

        private static float ReadOutput(Mat output, int row, int column, int rows, int dimensions) =>
            output.Size(1) == rows
                ? output.At<float>(0, row, column)
                : output.At<float>(0, column, row);
    }
}

public static class OpenCvFrameExtractor
{
    public static Task<IReadOnlyList<FrameSample>> ExtractAsync(
        string videoPath,
        IReadOnlyList<TimeSpan> offsets,
        string outputFolder,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputFolder);
        using var capture = new VideoCapture(videoPath);
        if (!capture.IsOpened())
        {
            return Task.FromResult<IReadOnlyList<FrameSample>>([]);
        }

        using var frame = new Mat();
        var samples = new List<FrameSample>();
        for (var index = 0; index < offsets.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = offsets[index];
            capture.Set(VideoCaptureProperties.PosMsec, offset.TotalMilliseconds);
            if (!capture.Read(frame) || frame.Empty())
            {
                continue;
            }

            var imagePath = Path.Combine(outputFolder, $"frame-{index + 1}-{Math.Round(offset.TotalMilliseconds)}ms.jpg");
            Cv2.ImWrite(imagePath, frame);
            samples.Add(new FrameSample(offset, imagePath));
        }

        return Task.FromResult<IReadOnlyList<FrameSample>>(samples);
    }
}

internal static class OpenCvMapper
{
    public static Rect ToRect(this CvRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    public static CvPoint[] ToPolygon(this Rect rect) =>
    [
        new(rect.Left, rect.Top),
        new(rect.Right, rect.Top),
        new(rect.Right, rect.Bottom),
        new(rect.Left, rect.Bottom)
    ];
}
