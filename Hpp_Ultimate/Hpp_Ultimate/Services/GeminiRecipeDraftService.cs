using System.Text;
using System.Text.Json;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class GeminiRecipeDraftService(
    HttpClient httpClient,
    IConfiguration configuration,
    SeededBusinessDataStore store,
    ILogger<GeminiRecipeDraftService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GeminiAiOptions _options = GeminiAiOptions.FromConfiguration(configuration, store.GetBusinessSettings());

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<RecipeAiDraftResult> GenerateDraftAsync(RecipeAiDraftRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new RecipeAiDraftResult(false, "Tambahkan GEMINI_API_KEY untuk memakai generator resep AI.");
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return new RecipeAiDraftResult(false, "Prompt resep AI masih kosong.");
        }

        if (request.Materials.Count == 0)
        {
            return new RecipeAiDraftResult(false, "Katalog material kosong. AI belum bisa menyusun resep.");
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
                temperature = 0.2
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
                logger.LogWarning("Gemini recipe draft failed with status {StatusCode}: {Body}", response.StatusCode, body);
                return new RecipeAiDraftResult(false, "Gemini belum berhasil menyusun draft resep.");
            }

            var rawJson = ExtractResponseText(body);
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new RecipeAiDraftResult(false, "Gemini tidak mengembalikan draft resep.");
            }

            var envelope = JsonSerializer.Deserialize<GeminiRecipeDraftEnvelope>(rawJson, JsonOptions);
            if (envelope is null)
            {
                return new RecipeAiDraftResult(false, "Hasil AI belum bisa dipahami aplikasi.");
            }

            var materialsByCode = request.Materials.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);
            var groups = new List<RecipeAiGroupSuggestion>();
            var warnings = new List<string>();

            foreach (var group in envelope.Groups)
            {
                var materialSuggestions = new List<RecipeAiMaterialSuggestion>();
                foreach (var material in group.Materials)
                {
                    if (material.Quantity <= 0 || string.IsNullOrWhiteSpace(material.Unit))
                    {
                        continue;
                    }

                    var code = Normalize(material.MaterialCode);
                    if (code is null || !materialsByCode.TryGetValue(code, out var materialContext))
                    {
                        warnings.Add($"Bahan \"{Normalize(material.RawMaterialName) ?? "Tanpa nama"}\" belum bisa dicocokkan ke katalog material.");
                        continue;
                    }

                    if (!materialContext.AvailableUnits.Any(unit => unit.Equals(material.Unit, StringComparison.OrdinalIgnoreCase)))
                    {
                        warnings.Add($"Unit \"{material.Unit}\" untuk bahan {materialContext.Name} tidak cocok dengan katalog.");
                        continue;
                    }

                    if (!materialContext.ExistsInWarehouse)
                    {
                        warnings.Add($"Bahan {materialContext.Name} belum ada di list gudang.");
                    }

                    materialSuggestions.Add(new RecipeAiMaterialSuggestion(
                        materialContext.Code,
                        material.Quantity,
                        material.Unit.Trim(),
                        Math.Max(0m, material.WastePercent),
                        Normalize(material.Notes)));
                }

                if (materialSuggestions.Count == 0)
                {
                    continue;
                }

                groups.Add(new RecipeAiGroupSuggestion(
                    string.IsNullOrWhiteSpace(group.Name) ? "Bahan utama" : group.Name.Trim(),
                    Normalize(group.Notes),
                    materialSuggestions));
            }

            foreach (var warning in envelope.Warnings)
            {
                var normalized = Normalize(warning);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    warnings.Add(normalized);
                }
            }

            if (groups.Count == 0)
            {
                return new RecipeAiDraftResult(false, "Gemini belum menghasilkan kelompok bahan yang valid untuk katalog ini.");
            }

            var costs = envelope.Costs
                .Where(item => !string.IsNullOrWhiteSpace(item.Name) && item.Amount > 0)
                .Select(item =>
                {
                    var name = item.Name!.Trim();
                    return new RecipeAiCostSuggestion(
                        NormalizeCostType(item.Type),
                        name,
                        item.Amount,
                        Normalize(item.Notes));
                })
                .ToArray();

            var draft = new RecipeAiDraft(
                string.IsNullOrWhiteSpace(envelope.Name) ? "Draft resep AI" : envelope.Name.Trim(),
                Normalize(envelope.Description),
                envelope.PortionYield <= 0 ? 1m : envelope.PortionYield,
                RecipePortionUnitCatalog.Normalize(envelope.PortionUnit),
                Math.Clamp(envelope.TargetMarginPercent, 0m, 500m),
                groups,
                costs,
                warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

            return new RecipeAiDraftResult(true, $"Draft resep AI siap direview. {groups.Count} kelompok bahan berhasil disusun.", draft);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Gemini recipe draft error.");
            return new RecipeAiDraftResult(false, "Terjadi kendala saat menghubungi Gemini untuk menyusun resep.");
        }
    }

    private static string BuildPrompt(RecipeAiDraftRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Susun draft resep operasional dari prompt berikut.");
        builder.AppendLine("Gunakan hanya material yang tersedia di katalog.");
        builder.AppendLine("Kembalikan JSON yang mengikuti schema.");
        builder.AppendLine("Aturan:");
        builder.AppendLine("1. Gunakan materialCode hanya dari katalog yang diberikan.");
        builder.AppendLine("2. Jika bahan tidak yakin, jangan paksa match. Tambahkan warning.");
        builder.AppendLine("3. Buat struktur kelompok bahan yang rapi dan praktis.");
        builder.AppendLine("4. portionUnit hanya boleh dari pilihan umum seperti pcs, pouch, box, pack, cup, bottle, tray.");
        builder.AppendLine("5. costs hanya untuk overhead atau production yang memang masuk akal.");
        builder.AppendLine("6. Prioritaskan bahan yang ada di gudang jika memungkinkan.");
        builder.AppendLine();
        builder.AppendLine("Prompt user:");
        builder.AppendLine(request.Prompt.Trim());
        builder.AppendLine();
        builder.AppendLine("Katalog material:");

        foreach (var material in request.Materials.OrderBy(item => item.Name))
        {
            builder.Append("- code: ");
            builder.Append(material.Code);
            builder.Append(" | name: ");
            builder.Append(material.Name);
            builder.Append(" | brand: ");
            builder.Append(string.IsNullOrWhiteSpace(material.Brand) ? "-" : material.Brand);
            builder.Append(" | baseUnit: ");
            builder.Append(material.BaseUnit);
            builder.Append(" | units: ");
            builder.Append(string.Join(", ", material.AvailableUnits));
            builder.Append(" | gudang: ");
            builder.Append(material.ExistsInWarehouse ? "ya" : "tidak");
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
                ["name"] = new Dictionary<string, object?> { ["type"] = "string" },
                ["description"] = new Dictionary<string, object?> { ["type"] = new object[] { "string", "null" } },
                ["portionYield"] = new Dictionary<string, object?> { ["type"] = "number", ["minimum"] = 0.0001 },
                ["portionUnit"] = new Dictionary<string, object?> { ["type"] = "string" },
                ["targetMarginPercent"] = new Dictionary<string, object?> { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 500 },
                ["warnings"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?> { ["type"] = "string" }
                },
                ["groups"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["name"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["notes"] = new Dictionary<string, object?> { ["type"] = new object[] { "string", "null" } },
                            ["materials"] = new Dictionary<string, object?>
                            {
                                ["type"] = "array",
                                ["items"] = new Dictionary<string, object?>
                                {
                                    ["type"] = "object",
                                    ["additionalProperties"] = false,
                                    ["properties"] = new Dictionary<string, object?>
                                    {
                                        ["rawMaterialName"] = new Dictionary<string, object?> { ["type"] = new object[] { "string", "null" } },
                                        ["materialCode"] = new Dictionary<string, object?> { ["type"] = new object[] { "string", "null" } },
                                        ["quantity"] = new Dictionary<string, object?> { ["type"] = "number", ["minimum"] = 0.0001 },
                                        ["unit"] = new Dictionary<string, object?> { ["type"] = "string" },
                                        ["wastePercent"] = new Dictionary<string, object?> { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 100 },
                                        ["notes"] = new Dictionary<string, object?> { ["type"] = new object[] { "string", "null" } }
                                    },
                                    ["required"] = new[] { "rawMaterialName", "materialCode", "quantity", "unit", "wastePercent", "notes" }
                                }
                            }
                        },
                        ["required"] = new[] { "name", "notes", "materials" }
                    }
                },
                ["costs"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["type"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { "overhead", "production" } },
                            ["name"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["amount"] = new Dictionary<string, object?> { ["type"] = "number", ["minimum"] = 0 },
                            ["notes"] = new Dictionary<string, object?> { ["type"] = new object[] { "string", "null" } }
                        },
                        ["required"] = new[] { "type", "name", "amount", "notes" }
                    }
                }
            },
            ["required"] = new[] { "name", "description", "portionYield", "portionUnit", "targetMarginPercent", "warnings", "groups", "costs" }
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

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static RecipeCostType NormalizeCostType(string? value)
        => string.Equals(Normalize(value), "production", StringComparison.OrdinalIgnoreCase)
            ? RecipeCostType.Production
            : RecipeCostType.Overhead;

    private sealed class GeminiRecipeDraftEnvelope
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public decimal PortionYield { get; set; }
        public string? PortionUnit { get; set; }
        public decimal TargetMarginPercent { get; set; }
        public List<string> Warnings { get; set; } = [];
        public List<GeminiRecipeGroupEnvelope> Groups { get; set; } = [];
        public List<GeminiRecipeCostEnvelope> Costs { get; set; } = [];
    }

    private sealed class GeminiRecipeGroupEnvelope
    {
        public string? Name { get; set; }
        public string? Notes { get; set; }
        public List<GeminiRecipeMaterialEnvelope> Materials { get; set; } = [];
    }

    private sealed class GeminiRecipeMaterialEnvelope
    {
        public string? RawMaterialName { get; set; }
        public string? MaterialCode { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; } = string.Empty;
        public decimal WastePercent { get; set; }
        public string? Notes { get; set; }
    }

    private sealed class GeminiRecipeCostEnvelope
    {
        public string? Type { get; set; }
        public string? Name { get; set; }
        public decimal Amount { get; set; }
        public string? Notes { get; set; }
    }
}
