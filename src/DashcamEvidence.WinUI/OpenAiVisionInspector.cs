using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DashcamEvidence.Core;

namespace DashcamEvidence_WinUI;

internal sealed class OpenAiVisionInspector
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private readonly string _apiKey;
    private readonly string _model;

    private OpenAiVisionInspector(string apiKey, string model)
    {
        _apiKey = apiKey;
        _model = model;
    }

    public static Func<IReadOnlyList<FrameSample>, CancellationToken, Task<SmartInspectResult?>>? CreateFromEnvironment()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        var inspector = new OpenAiVisionInspector(apiKey, string.IsNullOrWhiteSpace(model) ? "gpt-5.5" : model);
        return inspector.AnalyzeAsync;
    }

    private async Task<SmartInspectResult?> AnalyzeAsync(IReadOnlyList<FrameSample> frames, CancellationToken cancellationToken)
    {
        if (frames.Count == 0)
        {
            return null;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        using var request = new StringContent(await BuildRequestJson(frames, cancellationToken), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("https://api.openai.com/v1/responses", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(body);
        var text = ExtractOutputText(doc.RootElement);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var payload = JsonSerializer.Deserialize<VisionPayload>(text, Options);
        if (payload is null)
        {
            return null;
        }

        Enum.TryParse<IncidentCategory>(payload.Category, true, out var category);
        return new SmartInspectResult(
            category == default && !string.Equals(payload.Category, nameof(IncidentCategory.CyclistConflict), StringComparison.OrdinalIgnoreCase)
                ? null
                : category,
            payload.Plate ?? "",
            payload.VehicleNotes ?? "",
            null,
            payload.Notes ?? "",
            payload.Confidence,
            "openai");
    }

    private async Task<string> BuildRequestJson(IReadOnlyList<FrameSample> frames, CancellationToken cancellationToken)
    {
        var content = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["type"] = "input_text",
                ["text"] = """
                Inspect these dashcam frames near one incident point. Return only the requested JSON. Use best effort:
                - plate: visible plate text, blank if not readable
                - vehicleNotes: color, vehicle type, direction, or other visible notes
                - category: one of CyclistConflict, BusYieldFailure, BikeLaneObstruction, StopSignViolation, FailToYield, UnsafePass, Other
                - notes: short neutral evidence note; do not claim certainty about legal violations
                - confidence: 0 to 1
                """
            }
        };

        foreach (var frame in frames)
        {
            var bytes = await File.ReadAllBytesAsync(frame.ImagePath, cancellationToken);
            content.Add(new Dictionary<string, object?>
            {
                ["type"] = "input_image",
                ["image_url"] = $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}"
            });
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["input"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = content
                }
            },
            ["text"] = new Dictionary<string, object?>
            {
                ["format"] = new Dictionary<string, object?>
                {
                    ["type"] = "json_schema",
                    ["name"] = "dashcam_smart_inspect",
                    ["strict"] = true,
                    ["schema"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["plate"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["vehicleNotes"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["category"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string",
                                ["enum"] = Enum.GetNames<IncidentCategory>()
                            },
                            ["notes"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["confidence"] = new Dictionary<string, object?>
                            {
                                ["type"] = "number",
                                ["minimum"] = 0,
                                ["maximum"] = 1
                            }
                        },
                        ["required"] = new[] { "plate", "vehicleNotes", "category", "notes", "confidence" }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(body, Options);
    }

    private static string? ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText))
        {
            return outputText.GetString();
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text))
                {
                    return text.GetString();
                }
            }
        }

        return null;
    }

    private sealed record VisionPayload(string? Category, string? Plate, string? VehicleNotes, string? Notes, double? Confidence);
}
