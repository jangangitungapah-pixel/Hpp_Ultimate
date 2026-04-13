using System.Text;
using System.Text.Json;
using Hpp_Ultimate.Domain;
using Microsoft.Extensions.Caching.Memory;

namespace Hpp_Ultimate.Services;

public sealed class GeminiRecipeWeightEstimatorService(
    HttpClient httpClient,
    IConfiguration configuration,
    SeededBusinessDataStore store,
    IMemoryCache cache,
    ILogger<GeminiRecipeWeightEstimatorService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GeminiAiOptions _options = GeminiAiOptions.FromConfiguration(configuration, store.GetBusinessSettings());

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<RecipeDoughWeightEstimateResult> EstimateAsync(RecipeUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var materialMap = store.RawMaterials.ToDictionary(item => item.Id);
        var warnings = new List<string>();
        var aiCandidates = new List<AiWeightContext>();
        var lineEstimates = new List<RecipeDoughWeightLineEstimate>();
        var totalWeightGr = 0m;
        var directLineCount = 0;
        var aiLineCount = 0;
        var unresolvedLineCount = 0;

        foreach (var group in request.Groups)
        {
            foreach (var line in group.Materials)
            {
                if (line.MaterialId is not Guid materialId)
                {
                    unresolvedLineCount++;
                    lineEstimates.Add(new RecipeDoughWeightLineEstimate(
                        group.Id,
                        line.Id,
                        null,
                        "Material belum dipilih",
                        0m,
                        false,
                        false,
                        "Pilih material terlebih dahulu agar beratnya bisa dihitung."));
                    continue;
                }

                if (!materialMap.TryGetValue(materialId, out var material))
                {
                    unresolvedLineCount++;
                    warnings.Add($"Material dengan ID {materialId} tidak ditemukan saat menghitung berat adonan.");
                    lineEstimates.Add(new RecipeDoughWeightLineEstimate(
                        group.Id,
                        line.Id,
                        materialId,
                        "Material tidak ditemukan",
                        0m,
                        false,
                        false,
                        "Material katalog tidak ditemukan."));
                    continue;
                }

                if (line.Quantity <= 0 || string.IsNullOrWhiteSpace(line.Unit))
                {
                    unresolvedLineCount++;
                    lineEstimates.Add(new RecipeDoughWeightLineEstimate(
                        group.Id,
                        line.Id,
                        materialId,
                        material.Name,
                        0m,
                        false,
                        false,
                        "Lengkapi kuantitas dan satuan untuk menghitung gram."));
                    continue;
                }

                if (TryConvertWeightToGr(line, material, out var directWeightGr, out var directMessage, out var usedEstimatedFallback))
                {
                    var normalizedWeightGr = NormalizeWeight(directWeightGr);
                    totalWeightGr += normalizedWeightGr;
                    if (usedEstimatedFallback)
                    {
                        aiLineCount++;
                    }
                    else
                    {
                        directLineCount++;
                    }

                    lineEstimates.Add(new RecipeDoughWeightLineEstimate(
                        group.Id,
                        line.Id,
                        materialId,
                        material.Name,
                        normalizedWeightGr,
                        true,
                        usedEstimatedFallback,
                        directMessage));
                    continue;
                }

                var cacheKey = BuildCacheKey(material, line);
                if (cache.TryGetValue<decimal>(cacheKey, out var cachedWeightGr) && cachedWeightGr > 0)
                {
                    var normalizedWeightGr = NormalizeWeight(cachedWeightGr);
                    totalWeightGr += normalizedWeightGr;
                    aiLineCount++;
                    lineEstimates.Add(new RecipeDoughWeightLineEstimate(
                        group.Id,
                        line.Id,
                        materialId,
                        material.Name,
                        normalizedWeightGr,
                        true,
                        true,
                        "Estimasi Gemini dari jenis bahan dan satuan."));
                    continue;
                }

                if (!IsConfigured)
                {
                    unresolvedLineCount++;
                    warnings.Add($"Aktifkan Gemini untuk mengestimasi {material.Name} ({line.Quantity:0.##} {line.Unit}) ke gram.");
                    lineEstimates.Add(new RecipeDoughWeightLineEstimate(
                        group.Id,
                        line.Id,
                        materialId,
                        material.Name,
                        0m,
                        false,
                        false,
                        "Butuh Gemini untuk konversi bahan non-gram."));
                    continue;
                }

                aiCandidates.Add(new AiWeightContext(
                    group.Id,
                    group.Name,
                    line.Id,
                    materialId,
                    cacheKey,
                    material.Code,
                    material.Name,
                    material.Brand,
                    material.Description,
                    material.BaseUnit,
                    material.NetQuantity,
                    material.NetUnit,
                    line.Quantity,
                    line.Unit,
                    line.Notes));
            }
        }

        if (aiCandidates.Count > 0)
        {
            var aiEstimates = await EstimateMissingWeightsWithAiAsync(aiCandidates, cancellationToken);
            foreach (var candidate in aiCandidates)
            {
                if (aiEstimates.TryGetValue(candidate.CacheKey, out var estimatedWeightGr) && estimatedWeightGr > 0)
                {
                    var normalizedWeightGr = NormalizeWeight(estimatedWeightGr);
                    totalWeightGr += normalizedWeightGr;
                    aiLineCount++;
                    cache.Set(candidate.CacheKey, normalizedWeightGr, TimeSpan.FromHours(12));
                    lineEstimates.Add(new RecipeDoughWeightLineEstimate(
                        candidate.GroupId,
                        candidate.LineId,
                        candidate.MaterialId,
                        candidate.MaterialName,
                        normalizedWeightGr,
                        true,
                        true,
                        "Estimasi Gemini dari jenis bahan dan satuan."));
                }
                else
                {
                    unresolvedLineCount++;
                    warnings.Add($"Gemini belum bisa mengestimasi berat {candidate.MaterialName} ({candidate.Quantity:0.##} {candidate.Unit}).");
                    lineEstimates.Add(new RecipeDoughWeightLineEstimate(
                        candidate.GroupId,
                        candidate.LineId,
                        candidate.MaterialId,
                        candidate.MaterialName,
                        0m,
                        false,
                        false,
                        "Estimasi gram belum tersedia."));
                }
            }
        }

        var normalizedTotalWeightGr = NormalizeWeight(totalWeightGr);
        var groupEstimates = BuildGroupEstimates(request.Groups, lineEstimates);

        return new RecipeDoughWeightEstimateResult(
            normalizedTotalWeightGr,
            unresolvedLineCount == 0,
            aiLineCount > 0,
            directLineCount,
            aiLineCount,
            unresolvedLineCount,
            BuildMessage(normalizedTotalWeightGr, directLineCount, aiLineCount, unresolvedLineCount),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray(),
            groupEstimates,
            lineEstimates);
    }

    private static IReadOnlyList<RecipeDoughWeightGroupEstimate> BuildGroupEstimates(
        IReadOnlyList<RecipeGroupInput> groups,
        IReadOnlyList<RecipeDoughWeightLineEstimate> lines)
    {
        var linesByGroup = lines
            .GroupBy(line => line.GroupId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var result = new List<RecipeDoughWeightGroupEstimate>(groups.Count);
        foreach (var group in groups)
        {
            var groupLines = linesByGroup.TryGetValue(group.Id, out var mappedLines)
                ? mappedLines
                : [];

            var resolvedLineCount = groupLines.Count(line => line.IsResolved);
            var aiLineCount = groupLines.Count(line => line.IsResolved && line.UsedAi);
            var unresolvedLineCount = groupLines.Count(line => !line.IsResolved);
            var totalWeightGr = NormalizeWeight(groupLines.Where(line => line.IsResolved).Sum(line => line.WeightGr));

            result.Add(new RecipeDoughWeightGroupEstimate(
                group.Id,
                group.Name,
                totalWeightGr,
                unresolvedLineCount == 0,
                aiLineCount > 0,
                resolvedLineCount,
                aiLineCount,
                unresolvedLineCount,
                BuildGroupMessage(group, totalWeightGr, resolvedLineCount, aiLineCount, unresolvedLineCount)));
        }

        return result;
    }

    private static string BuildGroupMessage(
        RecipeGroupInput group,
        decimal totalWeightGr,
        int resolvedLineCount,
        int aiLineCount,
        int unresolvedLineCount)
    {
        if (group.Materials.Count == 0)
        {
            return "Belum ada material di kelompok ini.";
        }

        if (resolvedLineCount == 0)
        {
            return unresolvedLineCount > 0
                ? $"{unresolvedLineCount} bahan belum masuk total gram."
                : "Belum ada material yang dihitung.";
        }

        if (unresolvedLineCount > 0)
        {
            return $"Perkiraan sementara {FormatWeight(totalWeightGr)}. {unresolvedLineCount} bahan belum masuk total.";
        }

        if (aiLineCount > 0)
        {
            return $"Termasuk estimasi berat untuk {aiLineCount} bahan.";
        }

        return $"{resolvedLineCount} bahan sudah dihitung ke gram.";
    }

    private static decimal NormalizeWeight(decimal value)
        => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private async Task<IReadOnlyDictionary<string, decimal>> EstimateMissingWeightsWithAiAsync(
        IReadOnlyList<AiWeightContext> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0 || !IsConfigured)
        {
            return new Dictionary<string, decimal>();
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
                        new { text = BuildPrompt(candidates) }
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
                logger.LogWarning("Gemini recipe weight estimation failed with status {StatusCode}: {Body}", response.StatusCode, body);
                return new Dictionary<string, decimal>();
            }

            var rawJson = ExtractResponseText(body);
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new Dictionary<string, decimal>();
            }

            var envelope = JsonSerializer.Deserialize<GeminiRecipeWeightEnvelope>(rawJson, JsonOptions);
            if (envelope is null || envelope.Items.Count == 0)
            {
                return new Dictionary<string, decimal>();
            }

            return envelope.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.CacheKey) && item.EstimatedWeightGr > 0)
                .GroupBy(item => item.CacheKey!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => decimal.Round(group.First().EstimatedWeightGr, 2, MidpointRounding.AwayFromZero),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Gemini recipe weight estimation error.");
            return new Dictionary<string, decimal>();
        }
    }

    private static bool TryConvertWeightToGr(
        RecipeMaterialInput line,
        RawMaterial material,
        out decimal totalWeightGr,
        out string message,
        out bool usedEstimatedFallback)
    {
        totalWeightGr = 0m;
        message = "Konversi langsung ke gram.";
        usedEstimatedFallback = false;

        if (line.Quantity <= 0 || string.IsNullOrWhiteSpace(line.Unit))
        {
            return false;
        }

        var normalizedUnit = MaterialUnitCatalog.NormalizeUnit(line.Unit);
        if (string.Equals(MaterialUnitCatalog.GetFamily(normalizedUnit), "mass", StringComparison.OrdinalIgnoreCase))
        {
            totalWeightGr = MaterialUnitCatalog.Convert(line.Quantity, normalizedUnit, "gr");
            return totalWeightGr > 0;
        }

        var baseQuantity = RecipeCatalogMath.ConvertToBaseQuantity(line.Quantity, line.Unit, material);
        if (baseQuantity <= 0)
        {
            return false;
        }

        if (!string.Equals(MaterialUnitCatalog.GetFamily(material.BaseUnit), "mass", StringComparison.OrdinalIgnoreCase))
        {
            if (TryEstimateNonMassIngredientToGr(baseQuantity, material, out totalWeightGr, out message))
            {
                usedEstimatedFallback = true;
                return totalWeightGr > 0;
            }

            return false;
        }

        totalWeightGr = MaterialUnitCatalog.Convert(baseQuantity, material.BaseUnit, "gr");
        return totalWeightGr > 0;
    }

    private static bool TryEstimateNonMassIngredientToGr(
        decimal baseQuantity,
        RawMaterial material,
        out decimal totalWeightGr,
        out string message)
    {
        totalWeightGr = 0m;
        message = "Estimasi lokal dari jenis bahan dan satuan.";

        var baseFamily = MaterialUnitCatalog.GetFamily(material.BaseUnit);
        if (string.Equals(baseFamily, "volume", StringComparison.OrdinalIgnoreCase)
            && TryResolveDensityGrPerMl(material, out var densityGrPerMl))
        {
            var volumeMl = MaterialUnitCatalog.Convert(baseQuantity, material.BaseUnit, "ml");
            if (volumeMl > 0)
            {
                totalWeightGr = volumeMl * densityGrPerMl;
                return true;
            }
        }

        if (string.Equals(baseFamily, "count", StringComparison.OrdinalIgnoreCase)
            && TryResolveWeightPerPieceGr(material, out var weightPerPieceGr))
        {
            var pieceCount = MaterialUnitCatalog.Convert(baseQuantity, material.BaseUnit, "pcs");
            if (pieceCount > 0)
            {
                totalWeightGr = pieceCount * weightPerPieceGr;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveDensityGrPerMl(RawMaterial material, out decimal densityGrPerMl)
    {
        densityGrPerMl = 0m;

        var haystack = BuildMaterialSearchText(material);
        if (ContainsPhrase(haystack, "air mineral")
            || ContainsPhrase(haystack, "mineral water")
            || ContainsWord(haystack, "sanqua")
            || ContainsWord(haystack, "aqua")
            || ContainsWord(haystack, "water")
            || ContainsWord(haystack, "air"))
        {
            densityGrPerMl = 1m;
            return true;
        }

        if (ContainsPhrase(haystack, "susu cair")
            || ContainsPhrase(haystack, "uht")
            || ContainsWord(haystack, "milk")
            || ContainsWord(haystack, "susu"))
        {
            densityGrPerMl = 1.03m;
            return true;
        }

        if (ContainsPhrase(haystack, "whipping cream")
            || ContainsPhrase(haystack, "heavy cream")
            || ContainsPhrase(haystack, "krim")
            || ContainsPhrase(haystack, "cream"))
        {
            densityGrPerMl = 0.99m;
            return true;
        }

        if (ContainsWord(haystack, "minyak") || ContainsWord(haystack, "oil"))
        {
            densityGrPerMl = 0.92m;
            return true;
        }

        if (ContainsWord(haystack, "sirup") || ContainsWord(haystack, "syrup"))
        {
            densityGrPerMl = 1.32m;
            return true;
        }

        if (ContainsPhrase(haystack, "kental manis") || ContainsWord(haystack, "condensed"))
        {
            densityGrPerMl = 1.28m;
            return true;
        }

        if (ContainsWord(haystack, "madu") || ContainsWord(haystack, "honey"))
        {
            densityGrPerMl = 1.42m;
            return true;
        }

        return false;
    }

    private static bool TryResolveWeightPerPieceGr(RawMaterial material, out decimal weightPerPieceGr)
    {
        weightPerPieceGr = 0m;
        var haystack = BuildMaterialSearchText(material);

        if (ContainsWord(haystack, "telur") || ContainsWord(haystack, "egg"))
        {
            weightPerPieceGr = 55m;
            return true;
        }

        return false;
    }

    private static string BuildMaterialSearchText(RawMaterial material)
        => $" {material.Name} {material.Brand} {material.Description} ".ToLowerInvariant();

    private static bool ContainsWord(string haystack, string word)
        => haystack.Contains($" {word.ToLowerInvariant()} ", StringComparison.Ordinal);

    private static bool ContainsPhrase(string haystack, string phrase)
        => haystack.Contains(phrase.ToLowerInvariant(), StringComparison.Ordinal);

    private static string BuildCacheKey(RawMaterial material, RecipeMaterialInput line)
        => string.Join('|',
            material.Id.ToString("N"),
            MaterialUnitCatalog.NormalizeUnit(material.BaseUnit),
            MaterialUnitCatalog.NormalizeUnit(line.Unit),
            decimal.Round(line.Quantity, 4, MidpointRounding.AwayFromZero).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));

    private static string BuildPrompt(IReadOnlyList<AiWeightContext> candidates)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Konversikan bahan resep non-gram ke estimasi berat gram.");
        builder.AppendLine("Tujuan: hitung total berat adonan untuk produksi.");
        builder.AppendLine("Aturan:");
        builder.AppendLine("1. estimatedWeightGr harus mewakili berat bahan untuk quantity dan unit yang diminta, dalam gram.");
        builder.AppendLine("2. Gunakan konteks jenis bahan, satuan, kemasan, dan deskripsi bahan.");
        builder.AppendLine("3. Untuk bahan cair seperti air atau susu, boleh gunakan densitas kuliner umum jika wajar.");
        builder.AppendLine("4. Untuk bahan hitungan seperti telur, margarin sachet, vanili sachet, atau box, estimasikan gram yang realistis sesuai bahan.");
        builder.AppendLine("5. Jika bahan sangat tidak pasti, tetap berikan estimasi terbaik yang konservatif dan realistis.");
        builder.AppendLine("6. Fokus pada berat isi bahan yang masuk ke adonan, bukan berat kemasan luar.");
        builder.AppendLine();
        builder.AppendLine("Daftar bahan yang perlu dikonversi:");

        foreach (var item in candidates)
        {
            builder.Append("- cacheKey: ");
            builder.Append(item.CacheKey);
            builder.Append(" | code: ");
            builder.Append(item.MaterialCode);
            builder.Append(" | bahan: ");
            builder.Append(item.MaterialName);
            builder.Append(" | brand: ");
            builder.Append(string.IsNullOrWhiteSpace(item.Brand) ? "-" : item.Brand);
            builder.Append(" | deskripsi: ");
            builder.Append(string.IsNullOrWhiteSpace(item.Description) ? "-" : item.Description);
            builder.Append(" | baseUnit katalog: ");
            builder.Append(item.BaseUnit);
            builder.Append(" | netto pack: ");
            builder.Append(item.NetQuantity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append(item.NetUnit);
            builder.Append(" | quantity resep: ");
            builder.Append(item.Quantity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append(item.Unit);
            builder.Append(" | catatan resep: ");
            builder.Append(string.IsNullOrWhiteSpace(item.Notes) ? "-" : item.Notes);
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
                ["items"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["cacheKey"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["estimatedWeightGr"] = new Dictionary<string, object?> { ["type"] = "number", ["minimum"] = 0.0001 },
                            ["confidence"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { "high", "medium", "low" } },
                            ["reason"] = new Dictionary<string, object?> { ["type"] = "string" }
                        },
                        ["required"] = new[] { "cacheKey", "estimatedWeightGr", "confidence", "reason" }
                    }
                }
            },
            ["required"] = new[] { "items" }
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

    private static string BuildMessage(decimal totalWeightGr, int directLineCount, int aiLineCount, int unresolvedLineCount)
    {
        if (directLineCount == 0 && aiLineCount == 0)
        {
            return unresolvedLineCount > 0
                ? "Berat adonan belum lengkap. Ada bahan non-gram yang masih perlu estimasi."
                : "Belum ada bahan yang dihitung.";
        }

        if (unresolvedLineCount > 0)
        {
            return $"Perkiraan sementara {FormatWeight(totalWeightGr)}. {unresolvedLineCount} bahan non-gram belum masuk total.";
        }

        if (aiLineCount > 0)
        {
            return $"Termasuk estimasi berat untuk {aiLineCount} bahan non-gram.";
        }

        return $"{directLineCount} bahan berhasil dihitung langsung ke gram.";
    }

    private static string FormatWeight(decimal totalWeightGr)
        => $"{decimal.Round(totalWeightGr, 0, MidpointRounding.AwayFromZero):N0} gr";

    private sealed record AiWeightContext(
        Guid GroupId,
        string GroupName,
        Guid LineId,
        Guid MaterialId,
        string CacheKey,
        string MaterialCode,
        string MaterialName,
        string? Brand,
        string? Description,
        string BaseUnit,
        decimal NetQuantity,
        string NetUnit,
        decimal Quantity,
        string Unit,
        string? Notes);

    private sealed class GeminiRecipeWeightEnvelope
    {
        public List<GeminiRecipeWeightItem> Items { get; set; } = [];
    }

    private sealed class GeminiRecipeWeightItem
    {
        public string? CacheKey { get; set; }
        public decimal EstimatedWeightGr { get; set; }
        public string? Confidence { get; set; }
        public string? Reason { get; set; }
    }
}
