using System.Text;
using System.Text.Json;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class GeminiProductionRecommendationService(
    HttpClient httpClient,
    IConfiguration configuration,
    SeededBusinessDataStore store,
    ILogger<GeminiProductionRecommendationService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GeminiAiOptions _options = GeminiAiOptions.FromConfiguration(configuration, store.GetBusinessSettings());

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<ProductionAiRecommendationResult> RecommendAsync(ProductionAiRecommendationRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new ProductionAiRecommendationResult(false, "Tambahkan GEMINI_API_KEY untuk memakai rekomendasi produksi AI.", []);
        }

        if (request.Recipes.Count == 0)
        {
            return new ProductionAiRecommendationResult(false, "Belum ada resep yang bisa dianalisis.", []);
        }

        var endpoint = $"{_options.EndpointBaseUrl.TrimEnd('/')}/models/{_options.Model}:generateContent";
        var payload = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = BuildPrompt(request) }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseJsonSchema = BuildResponseSchema(),
                temperature = 0.15
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        httpRequest.Headers.Add("x-goog-api-key", _options.ApiKey);

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Gemini production recommendation failed with status {StatusCode}: {Body}", response.StatusCode, body);
                return new ProductionAiRecommendationResult(false, "Gemini belum berhasil membuat rekomendasi produksi.", []);
            }

            var rawJson = ExtractResponseText(body);
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new ProductionAiRecommendationResult(false, "Gemini tidak mengembalikan rekomendasi produksi.", []);
            }

            var envelope = JsonSerializer.Deserialize<GeminiProductionRecommendationEnvelope>(rawJson, JsonOptions);
            if (envelope is null)
            {
                return new ProductionAiRecommendationResult(false, "Hasil rekomendasi produksi AI belum bisa dipahami aplikasi.", []);
            }

            var recipesById = request.Recipes.ToDictionary(item => item.RecipeId);
            var suggestions = new List<ProductionAiSuggestion>();

            foreach (var item in envelope.Suggestions)
            {
                if (!Guid.TryParse(item.RecipeId, out var recipeId) || !recipesById.TryGetValue(recipeId, out var recipe))
                {
                    continue;
                }

                suggestions.Add(new ProductionAiSuggestion(
                    recipeId,
                    recipe.Code,
                    recipe.Name,
                    Math.Max(1, item.SuggestedBatchCount),
                    Math.Clamp(item.SuggestedTargetDurationMinutes, 1, 1440),
                    NormalizeConfidence(item.Confidence),
                    string.IsNullOrWhiteSpace(item.Reason) ? "Rekomendasi dibuat dari kesiapan stok dan ritme produksi terakhir." : item.Reason.Trim()));
            }

            var message = suggestions.Count == 0
                ? "Gemini belum menemukan rekomendasi produksi yang cukup kuat."
                : $"Gemini menyiapkan {suggestions.Count} rekomendasi produksi.";

            return new ProductionAiRecommendationResult(true, message, suggestions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Gemini production recommendation error.");
            return new ProductionAiRecommendationResult(false, "Terjadi kendala saat menghubungi Gemini untuk rekomendasi produksi.", []);
        }
    }

    private static string BuildPrompt(ProductionAiRecommendationRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Buat rekomendasi prioritas produksi harian untuk aplikasi operasional.");
        builder.AppendLine("Kembalikan maksimal 3 saran produksi yang paling masuk akal.");
        builder.AppendLine("Aturan:");
        builder.AppendLine("1. Prioritaskan resep yang CanStart = true.");
        builder.AppendLine("2. Hindari resep yang sedang punya batch running kecuali ada alasan kuat.");
        builder.AppendLine("3. suggestedBatchCount harus integer minimal 1.");
        builder.AppendLine("4. suggestedTargetDurationMinutes harus realistis dan minimal 1.");
        builder.AppendLine("5. confidence hanya boleh high, medium, atau low.");
        builder.AppendLine("6. reason harus singkat, konkret, dan bisa dimengerti operator.");
        builder.AppendLine();
        builder.AppendLine("Resep yang tersedia:");

        foreach (var recipe in request.Recipes.OrderBy(item => item.Name))
        {
            builder.Append("- id: ");
            builder.Append(recipe.RecipeId);
            builder.Append(" | code: ");
            builder.Append(recipe.Code);
            builder.Append(" | name: ");
            builder.Append(recipe.Name);
            builder.Append(" | porsi/batch: ");
            builder.Append(recipe.PortionYieldPerBatch);
            builder.Append(' ');
            builder.Append(recipe.PortionUnit);
            builder.Append(" | bahan: ");
            builder.Append(recipe.MaterialCount);
            builder.Append(" | bisa mulai: ");
            builder.Append(recipe.CanStart ? "ya" : "tidak");
            builder.Append(" | running: ");
            builder.Append(recipe.HasRunningBatch ? "ya" : "tidak");
            builder.Append(" | catatan: ");
            builder.Append(recipe.ReadinessMessage);
            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("Antrian aktif:");
        foreach (var item in request.Queue.OrderBy(item => item.QueuedAt).Take(20))
        {
            builder.Append("- ");
            builder.Append(item.RecipeCode);
            builder.Append(" | ");
            builder.Append(item.RecipeName);
            builder.Append(" | batch: ");
            builder.Append(item.BatchCount);
            builder.Append(" | porsi: ");
            builder.Append(item.TotalPortions);
            builder.Append(" | status: ");
            builder.Append(item.Status);
            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("Riwayat produksi terbaru:");
        foreach (var item in request.History.OrderByDescending(item => item.QueuedAt).Take(30))
        {
            builder.Append("- ");
            builder.Append(item.RecipeCode);
            builder.Append(" | ");
            builder.Append(item.RecipeName);
            builder.Append(" | batch: ");
            builder.Append(item.BatchCount);
            builder.Append(" | porsi: ");
            builder.Append(item.TotalPortions);
            builder.Append(" | waktu: ");
            builder.Append(item.QueuedAt.ToString("yyyy-MM-dd HH:mm"));
            builder.Append(" | status: ");
            builder.Append(item.Status);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static object BuildResponseSchema()
        => new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new Dictionary<string, object?>
            {
                ["suggestions"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["recipeId"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["suggestedBatchCount"] = new Dictionary<string, object?> { ["type"] = "integer", ["minimum"] = 1 },
                            ["suggestedTargetDurationMinutes"] = new Dictionary<string, object?> { ["type"] = "integer", ["minimum"] = 1 },
                            ["confidence"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { "high", "medium", "low" } },
                            ["reason"] = new Dictionary<string, object?> { ["type"] = "string" }
                        },
                        ["required"] = new[] { "recipeId", "suggestedBatchCount", "suggestedTargetDurationMinutes", "confidence", "reason" }
                    }
                }
            },
            ["required"] = new[] { "suggestions" }
        };

    private static string? ExtractResponseText(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return null;
        }

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text))
            {
                builder.Append(text.GetString());
            }
        }

        return builder.ToString();
    }

    private static string NormalizeConfidence(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "medium"
            : value.Trim().ToLowerInvariant() switch
            {
                "high" => "high",
                "low" => "low",
                _ => "medium"
            };

    private sealed class GeminiProductionRecommendationEnvelope
    {
        public List<GeminiProductionRecommendationSuggestionEnvelope> Suggestions { get; set; } = [];
    }

    private sealed class GeminiProductionRecommendationSuggestionEnvelope
    {
        public string? RecipeId { get; set; }
        public int SuggestedBatchCount { get; set; }
        public int SuggestedTargetDurationMinutes { get; set; }
        public string? Confidence { get; set; }
        public string? Reason { get; set; }
    }
}
