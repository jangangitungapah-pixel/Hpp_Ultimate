using System.Text;
using System.Text.Json;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class GeminiMaterialAuditService(
    HttpClient httpClient,
    IConfiguration configuration,
    SeededBusinessDataStore store,
    ILogger<GeminiMaterialAuditService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GeminiAiOptions _options = GeminiAiOptions.FromConfiguration(configuration, store.GetBusinessSettings());

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<MaterialAiAuditResult> AuditMaterialsAsync(MaterialAiAuditRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new MaterialAiAuditResult(false, "Tambahkan GEMINI_API_KEY untuk menjalankan audit AI katalog material.", []);
        }

        if (request.Materials.Count < 2)
        {
            return new MaterialAiAuditResult(false, "Minimal butuh dua material untuk analisis kemiripan.", []);
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
                        new { text = BuildPrompt(request.Materials) }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseJsonSchema = BuildResponseSchema(),
                temperature = 0.1
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
                logger.LogWarning("Gemini material audit failed with status {StatusCode}: {Body}", response.StatusCode, body);
                return new MaterialAiAuditResult(false, "Gemini belum berhasil menganalisis katalog material.", []);
            }

            var rawJson = ExtractResponseText(body);
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new MaterialAiAuditResult(false, "Gemini tidak mengembalikan hasil audit material.", []);
            }

            var envelope = JsonSerializer.Deserialize<GeminiMaterialAuditEnvelope>(rawJson, JsonOptions);
            if (envelope is null)
            {
                return new MaterialAiAuditResult(false, "Hasil audit AI belum bisa dipahami aplikasi.", []);
            }

            var materialsById = request.Materials.ToDictionary(item => item.Id);
            var suggestions = new List<MaterialAiNormalizationSuggestion>();

            foreach (var item in envelope.Suggestions)
            {
                if (!Guid.TryParse(item.TargetMaterialId, out var targetId) || !materialsById.TryGetValue(targetId, out var target))
                {
                    continue;
                }

                var related = new List<MaterialAiDuplicateReference>();
                foreach (var duplicateId in item.RelatedMaterialIds)
                {
                    if (!Guid.TryParse(duplicateId, out var relatedId) || !materialsById.TryGetValue(relatedId, out var relatedMaterial))
                    {
                        continue;
                    }

                    related.Add(new MaterialAiDuplicateReference(
                        relatedMaterial.Id,
                        relatedMaterial.Code,
                        relatedMaterial.Name,
                        relatedMaterial.Brand));
                }

                if (related.Count == 0)
                {
                    continue;
                }

                suggestions.Add(new MaterialAiNormalizationSuggestion(
                    target.Id,
                    target.Code,
                    string.IsNullOrWhiteSpace(item.CanonicalName) ? target.Name : item.CanonicalName.Trim(),
                    string.IsNullOrWhiteSpace(item.CanonicalBrand) ? target.Brand : item.CanonicalBrand.Trim(),
                    NormalizeConfidence(item.Confidence),
                    string.IsNullOrWhiteSpace(item.Reason) ? "Material terlihat punya nama/merk mirip." : item.Reason.Trim(),
                    related));
            }

            var message = suggestions.Count == 0
                ? "Gemini tidak menemukan pasangan material yang cukup mirip untuk direview."
                : $"Gemini menemukan {suggestions.Count} saran normalisasi material.";

            return new MaterialAiAuditResult(true, message, suggestions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Gemini material audit error.");
            return new MaterialAiAuditResult(false, "Terjadi kendala saat menghubungi Gemini untuk audit katalog.", []);
        }
    }

    private static string BuildPrompt(IReadOnlyList<RawMaterialListItem> materials)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Audit katalog material berikut untuk mencari material dengan nama atau merk yang sangat mirip dan sebaiknya dinormalisasi.");
        builder.AppendLine("Kembalikan hanya saran review, jangan usulkan merge otomatis.");
        builder.AppendLine("Aturan:");
        builder.AppendLine("1. Fokus pada material yang kemungkinan duplikat, beda penulisan, typo, atau variasi nama merek.");
        builder.AppendLine("2. relatedMaterialIds harus selalu mencantumkan targetMaterialId juga.");
        builder.AppendLine("3. confidence hanya boleh high, medium, atau low.");
        builder.AppendLine("4. Jangan beri saran kalau material jelas beda produk.");
        builder.AppendLine("5. canonicalName dan canonicalBrand harus ringkas dan rapi untuk dipakai sebagai nama/merk baku.");
        builder.AppendLine();
        builder.AppendLine("Daftar material:");

        foreach (var material in materials.OrderBy(item => item.Name))
        {
            builder.Append("- id: ");
            builder.Append(material.Id);
            builder.Append(" | code: ");
            builder.Append(material.Code);
            builder.Append(" | name: ");
            builder.Append(material.Name);
            builder.Append(" | brand: ");
            builder.Append(string.IsNullOrWhiteSpace(material.Brand) ? "-" : material.Brand);
            builder.Append(" | net: ");
            builder.Append(material.NetQuantity.ToString("0.####"));
            builder.Append(' ');
            builder.Append(material.NetUnit);
            builder.Append(" | baseUnit: ");
            builder.Append(material.BaseUnit);
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
                            ["targetMaterialId"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string"
                            },
                            ["canonicalName"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string"
                            },
                            ["canonicalBrand"] = new Dictionary<string, object?>
                            {
                                ["type"] = new object[] { "string", "null" }
                            },
                            ["confidence"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string",
                                ["enum"] = new[] { "high", "medium", "low" }
                            },
                            ["reason"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string"
                            },
                            ["relatedMaterialIds"] = new Dictionary<string, object?>
                            {
                                ["type"] = "array",
                                ["items"] = new Dictionary<string, object?>
                                {
                                    ["type"] = "string"
                                },
                                ["minItems"] = 2
                            }
                        },
                        ["required"] = new[] { "targetMaterialId", "canonicalName", "canonicalBrand", "confidence", "reason", "relatedMaterialIds" }
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

    private sealed class GeminiMaterialAuditEnvelope
    {
        public List<GeminiMaterialAuditSuggestionEnvelope> Suggestions { get; set; } = [];
    }

    private sealed class GeminiMaterialAuditSuggestionEnvelope
    {
        public string? TargetMaterialId { get; set; }

        public string? CanonicalName { get; set; }

        public string? CanonicalBrand { get; set; }

        public string? Confidence { get; set; }

        public string? Reason { get; set; }

        public List<string> RelatedMaterialIds { get; set; } = [];
    }
}
