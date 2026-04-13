using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class GeminiPosOrderParserService(
    HttpClient httpClient,
    IConfiguration configuration,
    SeededBusinessDataStore store,
    ILogger<GeminiPosOrderParserService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GeminiAiOptions _options = GeminiAiOptions.FromConfiguration(configuration, store.GetBusinessSettings());

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public string ConfigurationHint
        => IsConfigured
            ? $"Gemini aktif ({_options.Model})"
            : "Tambahkan GEMINI_API_KEY atau Gemini:ApiKey untuk mengaktifkan parser pesanan AI.";

    public async Task<PosAiOrderResult> ParseOrderAsync(PosAiOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new PosAiOrderResult(false, ConfigurationHint);
        }

        if (request.Products.Count == 0)
        {
            return new PosAiOrderResult(false, "Belum ada menu aktif yang bisa dipilih AI.");
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return new PosAiOrderResult(false, "Tulis pesanan natural language dulu sebelum meminta bantuan AI.");
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
                logger.LogWarning("Gemini POS order parse failed with status {StatusCode}: {Body}", response.StatusCode, body);
                return new PosAiOrderResult(false, "Gemini belum berhasil membaca draft pesanan POS.");
            }

            var rawJson = ExtractResponseText(body);
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new PosAiOrderResult(false, "Gemini tidak mengembalikan hasil pesanan yang bisa dipakai.");
            }

            var envelope = JsonSerializer.Deserialize<GeminiPosOrderEnvelope>(rawJson, JsonOptions);
            if (envelope is null)
            {
                return new PosAiOrderResult(false, "Hasil parser pesanan AI belum bisa dipahami aplikasi.");
            }

            var productsByCode = request.Products.ToDictionary(item => item.ProductCode, StringComparer.OrdinalIgnoreCase);
            var code = Normalize(envelope.ProductCode);
            var warnings = new List<string>();

            foreach (var warning in envelope.Warnings)
            {
                var normalizedWarning = Normalize(warning);
                if (!string.IsNullOrWhiteSpace(normalizedWarning))
                {
                    warnings.Add(normalizedWarning);
                }
            }

            if (!string.IsNullOrWhiteSpace(code) && !productsByCode.ContainsKey(code))
            {
                warnings.Add($"Kode menu AI \"{code}\" tidak ada di daftar POS aktif.");
                code = null;
            }

            var quantity = Math.Max(1, envelope.Quantity);
            var paymentMethod = NormalizePaymentMethod(envelope.PaymentMethod);
            var suggestion = new PosAiOrderSuggestion(
                code,
                quantity,
                Normalize(envelope.CustomerName),
                paymentMethod,
                ParseOptionalDate(envelope.SoldAt),
                Normalize(envelope.Reason) ?? "Draft pesanan dibentuk dari prompt natural language dan daftar menu aktif.",
                warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

            var message = string.IsNullOrWhiteSpace(code)
                ? "Gemini membaca pesanan, tetapi belum cukup yakin memilih menu. Cek warning lalu pilih menu manual."
                : $"Gemini menyiapkan draft pesanan untuk {productsByCode[code].ProductName}.";

            return new PosAiOrderResult(true, message, suggestion);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Gemini POS order parsing error.");
            return new PosAiOrderResult(false, "Terjadi kendala saat menghubungi Gemini untuk draft pesanan POS.");
        }
    }

    private static string BuildPrompt(PosAiOrderRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Baca draft pesanan kasir dalam bahasa natural Indonesia dan ubah menjadi satu draft checkout POS.");
        builder.AppendLine("Aplikasi POS ini hanya mendukung satu menu utama per checkout.");
        builder.AppendLine("Jika prompt berisi banyak menu, pilih menu utama yang paling jelas lalu tambahkan warning.");
        builder.AppendLine("Kembalikan JSON yang mengikuti schema.");
        builder.AppendLine("Aturan:");
        builder.AppendLine("1. productCode harus dipilih hanya dari daftar menu aktif yang diberikan.");
        builder.AppendLine("2. Jika tidak yakin, productCode = null dan tambahkan warning singkat.");
        builder.AppendLine("3. quantity harus integer minimal 1.");
        builder.AppendLine("4. paymentMethod hanya boleh Cash atau Transfer Bank.");
        builder.AppendLine("5. customerName isi nama pembeli/pemesan jika ada, jika tidak null.");
        builder.AppendLine("6. soldAt gunakan ISO 8601 bila prompt menyebut tanggal/jam yang jelas, jika tidak null.");
        builder.AppendLine("7. reason harus singkat dan konkret.");
        builder.AppendLine("8. Jangan membuat menu yang tidak ada di daftar.");
        builder.AppendLine();
        builder.Append("Waktu draft saat ini: ");
        builder.AppendLine(request.DraftSoldAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        builder.AppendLine();
        builder.AppendLine("Daftar menu aktif:");

        foreach (var product in request.Products.OrderBy(item => item.ProductName))
        {
            builder.Append("- code: ");
            builder.Append(product.ProductCode);
            builder.Append(" | name: ");
            builder.Append(product.ProductName);
            builder.Append(" | stok: ");
            builder.Append(product.OnHandQuantity.ToString("0.##", CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append(product.UnitLabel);
            builder.Append(" | harga: ");
            builder.Append(product.SuggestedPrice.ToString("0.##", CultureInfo.InvariantCulture));
            builder.Append(" | status: ");
            builder.Append(product.CanSell ? "siap jual" : "stok kosong");
            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("Draft pesanan dari operator:");
        builder.AppendLine(request.Prompt.Trim());
        return builder.ToString();
    }

    private static object BuildResponseSchema()
        => new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new Dictionary<string, object?>
            {
                ["productCode"] = new Dictionary<string, object?>
                {
                    ["type"] = new object[] { "string", "null" }
                },
                ["quantity"] = new Dictionary<string, object?>
                {
                    ["type"] = "integer",
                    ["minimum"] = 1
                },
                ["customerName"] = new Dictionary<string, object?>
                {
                    ["type"] = new object[] { "string", "null" }
                },
                ["paymentMethod"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["enum"] = new[] { "Cash", "Transfer Bank" }
                },
                ["soldAt"] = new Dictionary<string, object?>
                {
                    ["type"] = new object[] { "string", "null" },
                    ["format"] = "date-time"
                },
                ["reason"] = new Dictionary<string, object?>
                {
                    ["type"] = "string"
                },
                ["warnings"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string"
                    }
                }
            },
            ["required"] = new[] { "productCode", "quantity", "customerName", "paymentMethod", "soldAt", "reason", "warnings" }
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

    private static DateTime? ParseOptionalDate(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;

    private static string NormalizePaymentMethod(string? value)
        => string.Equals(value?.Trim(), "Transfer Bank", StringComparison.OrdinalIgnoreCase)
            ? "Transfer Bank"
            : "Cash";

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private sealed class GeminiPosOrderEnvelope
    {
        public string? ProductCode { get; set; }
        public int Quantity { get; set; }
        public string? CustomerName { get; set; }
        public string? PaymentMethod { get; set; }
        public string? SoldAt { get; set; }
        public string? Reason { get; set; }
        public List<string> Warnings { get; set; } = [];
    }
}
