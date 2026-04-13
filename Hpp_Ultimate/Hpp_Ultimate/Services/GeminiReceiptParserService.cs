using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class GeminiReceiptParserService(
    HttpClient httpClient,
    IConfiguration configuration,
    SeededBusinessDataStore store,
    ILogger<GeminiReceiptParserService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GeminiAiOptions _options = GeminiAiOptions.FromConfiguration(configuration, store.GetBusinessSettings());

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public string ConfigurationHint
        => IsConfigured
            ? $"Gemini aktif ({_options.Model})"
            : "Tambahkan GEMINI_API_KEY atau Gemini:ApiKey untuk mengaktifkan parser struk AI.";

    public async Task<ShoppingAiParseResult> ParseShoppingReceiptAsync(ShoppingAiReceiptRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new ShoppingAiParseResult(false, ConfigurationHint);
        }

        if (request.Materials.Count == 0)
        {
            return new ShoppingAiParseResult(false, "Katalog material kosong. AI belum bisa mencocokkan isi struk.");
        }

        if (string.IsNullOrWhiteSpace(request.Base64Content))
        {
            return new ShoppingAiParseResult(false, "File struk belum valid.");
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
                        new { text = BuildPrompt(request.Materials) },
                        new
                        {
                            inline_data = new
                            {
                                mime_type = string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType,
                                data = request.Base64Content
                            }
                        }
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
                logger.LogWarning("Gemini receipt parse failed with status {StatusCode}: {Body}", response.StatusCode, body);
                return new ShoppingAiParseResult(false, "Gemini belum berhasil membaca struk. Coba ulangi dengan foto/scan yang lebih jelas.");
            }

            var rawJson = ExtractResponseText(body);
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                logger.LogWarning("Gemini returned empty content for receipt parse.");
                return new ShoppingAiParseResult(false, "Gemini tidak mengembalikan hasil yang bisa dipakai.");
            }

            var parsed = JsonSerializer.Deserialize<GeminiReceiptEnvelope>(rawJson, JsonOptions);
            if (parsed is null)
            {
                return new ShoppingAiParseResult(false, "Hasil AI belum bisa dipahami aplikasi.");
            }

            var materialsByCode = request.Materials.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);
            var lines = new List<ShoppingAiReceiptLine>();
            var warnings = new List<string>();

            foreach (var line in parsed.LineItems)
            {
                if (line.PackCount <= 0)
                {
                    continue;
                }

                var code = Normalize(line.MatchedMaterialCode);
                if (code is null || !materialsByCode.ContainsKey(code))
                {
                    warnings.Add($"Item struk \"{Normalize(line.RawItemName) ?? "Tanpa nama"}\" belum ditemukan di katalog material.");
                    continue;
                }

                lines.Add(new ShoppingAiReceiptLine(
                    Normalize(line.RawItemName) ?? materialsByCode[code].Name,
                    materialsByCode[code].Code,
                    line.PackCount,
                    line.PricePerPack,
                    line.LineSubtotal,
                    Normalize(line.MatchReason),
                    Normalize(line.SuggestedBrand),
                    Normalize(line.SuggestedBaseUnit),
                    line.SuggestedNetQuantity,
                    Normalize(line.SuggestedNetUnit)));
            }

            if (lines.Count == 0)
            {
                if (warnings.Count == 0)
                {
                    warnings.Add("AI belum menemukan item struk yang bisa dicocokkan ke katalog.");
                }

                return new ShoppingAiParseResult(false, "Struk terbaca, tetapi belum ada item yang bisa dimasukkan ke draft.", new ShoppingAiReceiptDraft(
                    Normalize(parsed.SupplierName),
                    ParseOptionalDate(parsed.OrderedAt),
                    NormalizeChannel(parsed.Channel),
                    NormalizePlatform(parsed.EcommercePlatform),
                    parsed.ShippingCost < 0 ? 0 : decimal.Round(parsed.ShippingCost, 2),
                    Normalize(parsed.Notes),
                    lines,
                    warnings));
            }

            foreach (var warning in parsed.Warnings)
            {
                var normalized = Normalize(warning);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    warnings.Add(normalized);
                }
            }

            var draft = new ShoppingAiReceiptDraft(
                Normalize(parsed.SupplierName),
                ParseOptionalDate(parsed.OrderedAt),
                NormalizeChannel(parsed.Channel),
                NormalizePlatform(parsed.EcommercePlatform),
                parsed.ShippingCost < 0 ? 0 : decimal.Round(parsed.ShippingCost, 2),
                Normalize(parsed.Notes),
                lines,
                warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

            return new ShoppingAiParseResult(true, $"Struk berhasil dibaca AI. {lines.Count} item masuk ke draft belanja.", draft);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Gemini receipt parsing error.");
            return new ShoppingAiParseResult(false, "Terjadi kendala saat menghubungi Gemini. Coba lagi beberapa saat.");
        }
    }

    private static string BuildPrompt(IReadOnlyList<ShoppingMaterialOption> materials)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Baca file struk belanja ini dan ekstrak menjadi draft belanja material untuk aplikasi operasional.");
        builder.AppendLine("Kembalikan JSON yang mengikuti schema.");
        builder.AppendLine("Aturan penting:");
        builder.AppendLine("1. Cocokkan item struk hanya ke katalog material yang diberikan.");
        builder.AppendLine("2. Jika tidak yakin, isi matchedMaterialCode = null dan tambahkan warning singkat.");
        builder.AppendLine("3. packCount adalah jumlah pack yang dibeli, selalu integer minimal 1.");
        builder.AppendLine("4. channel hanya boleh offline atau online.");
        builder.AppendLine("5. ecommercePlatform isi hanya Shopee, Tokopedia, TikTok, WhatsApp, atau null.");
        builder.AppendLine("6. orderedAt gunakan format ISO 8601 bila terlihat jelas, jika tidak null.");
        builder.AppendLine("7. shippingCost isi 0 jika tidak ada.");
        builder.AppendLine("8. Jangan menebak item katalog yang tidak cukup yakin.");
        builder.AppendLine("9. Untuk item yang belum match, tetap isi suggestedBrand, suggestedBaseUnit, suggestedNetQuantity, dan suggestedNetUnit bila informasi pack terlihat.");
        builder.AppendLine();
        builder.AppendLine("Katalog material aktif:");

        foreach (var material in materials.OrderBy(item => item.Name))
        {
            builder.Append("- code: ");
            builder.Append(material.Code);
            builder.Append(" | name: ");
            builder.Append(material.Name);
            builder.Append(" | brand: ");
            builder.Append(string.IsNullOrWhiteSpace(material.Brand) ? "-" : material.Brand);
            builder.Append(" | pack: ");
            builder.Append(material.NetQuantity.ToString("0.####", CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append(material.NetUnit);
            builder.Append(" | baseUnit: ");
            builder.Append(material.BaseUnit);
            builder.Append(" | lookup: ");
            builder.Append(material.LookupLabel);
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
                ["supplierName"] = new Dictionary<string, object?>
                {
                    ["type"] = new object[] { "string", "null" },
                    ["description"] = "Nama toko atau supplier pada struk."
                },
                ["orderedAt"] = new Dictionary<string, object?>
                {
                    ["type"] = new object[] { "string", "null" },
                    ["format"] = "date-time",
                    ["description"] = "Tanggal dan waktu transaksi dalam ISO 8601 jika terlihat jelas."
                },
                ["channel"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["enum"] = new[] { "offline", "online" },
                    ["description"] = "Jenis belanja."
                },
                ["ecommercePlatform"] = new Dictionary<string, object?>
                {
                    ["type"] = new object[] { "string", "null" },
                    ["description"] = "Isi Shopee, Tokopedia, TikTok, atau WhatsApp jika channel online."
                },
                ["shippingCost"] = new Dictionary<string, object?>
                {
                    ["type"] = "number",
                    ["minimum"] = 0,
                    ["description"] = "Jumlah ongkir."
                },
                ["notes"] = new Dictionary<string, object?>
                {
                    ["type"] = new object[] { "string", "null" },
                    ["description"] = "Catatan singkat tambahan jika ada."
                },
                ["warnings"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string"
                    }
                },
                ["lineItems"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["rawItemName"] = new Dictionary<string, object?>
                            {
                                ["type"] = new object[] { "string", "null" },
                                ["description"] = "Nama item asli dari struk."
                            },
                            ["matchedMaterialCode"] = new Dictionary<string, object?>
                            {
                                ["type"] = new object[] { "string", "null" },
                                ["description"] = "Code material dari katalog jika cocok dengan percaya diri tinggi."
                            },
                            ["packCount"] = new Dictionary<string, object?>
                            {
                                ["type"] = "integer",
                                ["minimum"] = 1
                            },
                            ["pricePerPack"] = new Dictionary<string, object?>
                            {
                                ["type"] = new object[] { "number", "null" },
                                ["minimum"] = 0
                            },
                            ["lineSubtotal"] = new Dictionary<string, object?>
                            {
                                ["type"] = new object[] { "number", "null" },
                                ["minimum"] = 0
                            },
                            ["matchReason"] = new Dictionary<string, object?>
                            {
                                ["type"] = new object[] { "string", "null" },
                                ["description"] = "Alasan singkat mengapa item dicocokkan ke material ini."
                            },
                            ["suggestedBrand"] = new Dictionary<string, object?>
                            {
                                ["type"] = new object[] { "string", "null" },
                                ["description"] = "Merk yang terlihat di struk atau kemasan jika ada."
                            },
                            ["suggestedBaseUnit"] = new Dictionary<string, object?>
                            {
                                ["type"] = new object[] { "string", "null" },
                                ["description"] = "Saran base unit material seperti gr, ml, pcs, box, sachet, atau pack."
                            },
                            ["suggestedNetQuantity"] = new Dictionary<string, object?>
                            {
                                ["type"] = new object[] { "number", "null" },
                                ["minimum"] = 0
                            },
                            ["suggestedNetUnit"] = new Dictionary<string, object?>
                            {
                                ["type"] = new object[] { "string", "null" },
                                ["description"] = "Satuan netto pack seperti gr, kg, ml, l, pcs."
                            }
                        },
                        ["required"] = new[] { "rawItemName", "matchedMaterialCode", "packCount", "pricePerPack", "lineSubtotal", "matchReason", "suggestedBrand", "suggestedBaseUnit", "suggestedNetQuantity", "suggestedNetUnit" }
                    }
                }
            },
            ["required"] = new[] { "supplierName", "orderedAt", "channel", "ecommercePlatform", "shippingCost", "notes", "warnings", "lineItems" }
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

    private static string? NormalizePlatform(string? value)
    {
        var normalized = Normalize(value)?.ToLowerInvariant();
        return normalized switch
        {
            "shopee" => "Shopee",
            "tokopedia" => "Tokopedia",
            "tiktok" => "TikTok",
            "tiktok shop" => "TikTok",
            "whatsapp" => "WhatsApp",
            _ => null
        };
    }

    private static PurchaseChannel NormalizeChannel(string? value)
        => string.Equals(Normalize(value), "online", StringComparison.OrdinalIgnoreCase)
            ? PurchaseChannel.Online
            : PurchaseChannel.Offline;

    private static DateTime? ParseOptionalDate(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;

    private sealed class GeminiReceiptEnvelope
    {
        public string? SupplierName { get; set; }

        public string? OrderedAt { get; set; }

        public string? Channel { get; set; }

        public string? EcommercePlatform { get; set; }

        public decimal ShippingCost { get; set; }

        public string? Notes { get; set; }

        public List<string> Warnings { get; set; } = [];

        public List<GeminiReceiptLineEnvelope> LineItems { get; set; } = [];
    }

    private sealed class GeminiReceiptLineEnvelope
    {
        public string? RawItemName { get; set; }

        public string? MatchedMaterialCode { get; set; }

        public int PackCount { get; set; }

        public decimal? PricePerPack { get; set; }

        public decimal? LineSubtotal { get; set; }

        public string? MatchReason { get; set; }

        public string? SuggestedBrand { get; set; }

        public string? SuggestedBaseUnit { get; set; }

        public decimal? SuggestedNetQuantity { get; set; }

        public string? SuggestedNetUnit { get; set; }
    }
}
