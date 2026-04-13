using System.Text;
using System.Text.Json;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class GeminiBookkeepingAssistantService(
    HttpClient httpClient,
    IConfiguration configuration,
    SeededBusinessDataStore store,
    ILogger<GeminiBookkeepingAssistantService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GeminiAiOptions _options = GeminiAiOptions.FromConfiguration(configuration, store.GetBusinessSettings());

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<BookkeepingAiDraftResult> AnalyzeManualDraftAsync(BookkeepingAiDraftRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new BookkeepingAiDraftResult(false, "Tambahkan GEMINI_API_KEY untuk memakai AI pembukuan.");
        }

        if (string.IsNullOrWhiteSpace(request.Title) || request.Amount <= 0)
        {
            return new BookkeepingAiDraftResult(false, "Isi nama list dan nominal dulu sebelum analisa AI.");
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
                        new { text = BuildDraftPrompt(request) }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseJsonSchema = BuildDraftSchema(),
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
                logger.LogWarning("Gemini bookkeeping draft analysis failed with status {StatusCode}: {Body}", response.StatusCode, body);
                return new BookkeepingAiDraftResult(false, "Gemini belum berhasil menganalisis draft pembukuan.");
            }

            var rawJson = ExtractResponseText(body);
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new BookkeepingAiDraftResult(false, "Gemini tidak mengembalikan saran draft pembukuan.");
            }

            var envelope = JsonSerializer.Deserialize<GeminiBookkeepingDraftEnvelope>(rawJson, JsonOptions);
            if (envelope is null || string.IsNullOrWhiteSpace(envelope.SuggestedTitle))
            {
                return new BookkeepingAiDraftResult(false, "Hasil AI draft pembukuan belum bisa dipahami aplikasi.");
            }

            var suggestion = new BookkeepingAiDraftSuggestion(
                string.IsNullOrWhiteSpace(envelope.SuggestedCategory) ? "Operasional umum" : envelope.SuggestedCategory.Trim(),
                NormalizeDirection(envelope.SuggestedDirection),
                envelope.SuggestedTitle.Trim(),
                Normalize(envelope.SuggestedCounterparty),
                Normalize(envelope.SuggestedNotes),
                string.IsNullOrWhiteSpace(envelope.Reason) ? "Saran dibuat dari isi draft transaksi." : envelope.Reason.Trim());

            return new BookkeepingAiDraftResult(true, "Saran AI untuk draft pembukuan siap direview.", suggestion);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Gemini bookkeeping draft analysis error.");
            return new BookkeepingAiDraftResult(false, "Terjadi kendala saat menghubungi Gemini untuk draft pembukuan.");
        }
    }

    public async Task<BookkeepingAiSummaryResult> SummarizeLedgerAsync(BookkeepingAiSummaryRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new BookkeepingAiSummaryResult(false, "Tambahkan GEMINI_API_KEY untuk memakai ringkasan AI pembukuan.");
        }

        if (request.Items.Count == 0)
        {
            return new BookkeepingAiSummaryResult(false, "Ledger masih kosong, belum ada yang bisa diringkas.");
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
                        new { text = BuildSummaryPrompt(request) }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseJsonSchema = BuildSummarySchema(),
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
                logger.LogWarning("Gemini bookkeeping summary failed with status {StatusCode}: {Body}", response.StatusCode, body);
                return new BookkeepingAiSummaryResult(false, "Gemini belum berhasil membuat ringkasan ledger.");
            }

            var rawJson = ExtractResponseText(body);
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new BookkeepingAiSummaryResult(false, "Gemini tidak mengembalikan ringkasan ledger.");
            }

            var envelope = JsonSerializer.Deserialize<GeminiBookkeepingSummaryEnvelope>(rawJson, JsonOptions);
            if (envelope is null || string.IsNullOrWhiteSpace(envelope.Headline))
            {
                return new BookkeepingAiSummaryResult(false, "Ringkasan AI ledger belum bisa dipahami aplikasi.");
            }

            var summary = new BookkeepingAiSummary(
                envelope.Headline.Trim(),
                envelope.Highlights
                    .Select(Normalize)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Take(4)
                    .Cast<string>()
                    .ToArray());

            return new BookkeepingAiSummaryResult(true, "Ringkasan AI ledger berhasil dibuat.", summary);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected Gemini bookkeeping summary error.");
            return new BookkeepingAiSummaryResult(false, "Terjadi kendala saat menghubungi Gemini untuk ringkasan ledger.");
        }
    }

    private static string BuildDraftPrompt(BookkeepingAiDraftRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Analisa draft entry pembukuan manual untuk usaha kecil.");
        builder.AppendLine("Tentukan kategori yang paling masuk akal, rapikan judul, dan koreksi arah pemasukan/pengeluaran jika draft tampak keliru.");
        builder.AppendLine("Kembalikan JSON sesuai schema.");
        builder.AppendLine();
        builder.Append("Title: ").AppendLine(request.Title);
        builder.Append("Direction: ").AppendLine(request.Direction == LedgerEntryDirection.Income ? "income" : "expense");
        builder.Append("Amount: ").AppendLine(request.Amount.ToString("0.##"));
        builder.Append("Counterparty: ").AppendLine(string.IsNullOrWhiteSpace(request.Counterparty) ? "-" : request.Counterparty);
        builder.Append("Notes: ").AppendLine(string.IsNullOrWhiteSpace(request.Notes) ? "-" : request.Notes);
        builder.AppendLine();
        builder.AppendLine("Kategori umum yang boleh dipakai: utilitas, bahan operasional, transport, komisi, pendapatan tambahan, maintenance, sewa, promosi, kas kecil, operasional umum.");
        return builder.ToString();
    }

    private static string BuildSummaryPrompt(BookkeepingAiSummaryRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Ringkas ledger pembukuan untuk operator usaha kecil dalam bahasa Indonesia singkat.");
        builder.AppendLine("Buat satu headline dan maksimal 4 highlight. Fokus pada arus kas, pengeluaran dominan, dan kondisi saldo.");
        builder.AppendLine();
        builder.Append("Total income: ").AppendLine(request.TotalIncome.ToString("0.##"));
        builder.Append("Total expense: ").AppendLine(request.TotalExpense.ToString("0.##"));
        builder.Append("Closing balance: ").AppendLine(request.ClosingBalance.ToString("0.##"));
        builder.AppendLine("Entry terbaru:");
        foreach (var item in request.Items.OrderByDescending(i => i.OccurredAt).Take(20))
        {
            builder.Append("- ");
            builder.Append(item.OccurredAt.ToString("yyyy-MM-dd HH:mm"));
            builder.Append(" | ");
            builder.Append(item.SourceType);
            builder.Append(" | ");
            builder.Append(item.Title);
            builder.Append(" | in=");
            builder.Append(item.AmountIn.ToString("0.##"));
            builder.Append(" | out=");
            builder.Append(item.AmountOut.ToString("0.##"));
            builder.Append(" | saldo=");
            builder.Append(item.RunningBalance.ToString("0.##"));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static object BuildDraftSchema()
        => new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new Dictionary<string, object?>
            {
                ["suggestedCategory"] = new Dictionary<string, object?> { ["type"] = "string" },
                ["suggestedDirection"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { "income", "expense" } },
                ["suggestedTitle"] = new Dictionary<string, object?> { ["type"] = "string" },
                ["suggestedCounterparty"] = new Dictionary<string, object?> { ["type"] = new object[] { "string", "null" } },
                ["suggestedNotes"] = new Dictionary<string, object?> { ["type"] = new object[] { "string", "null" } },
                ["reason"] = new Dictionary<string, object?> { ["type"] = "string" }
            },
            ["required"] = new[] { "suggestedCategory", "suggestedDirection", "suggestedTitle", "suggestedCounterparty", "suggestedNotes", "reason" }
        };

    private static object BuildSummarySchema()
        => new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new Dictionary<string, object?>
            {
                ["headline"] = new Dictionary<string, object?> { ["type"] = "string" },
                ["highlights"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?> { ["type"] = "string" }
                }
            },
            ["required"] = new[] { "headline", "highlights" }
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

    private static LedgerEntryDirection NormalizeDirection(string? value)
        => string.Equals(Normalize(value), "income", StringComparison.OrdinalIgnoreCase)
            ? LedgerEntryDirection.Income
            : LedgerEntryDirection.Expense;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class GeminiBookkeepingDraftEnvelope
    {
        public string? SuggestedCategory { get; set; }
        public string? SuggestedDirection { get; set; }
        public string? SuggestedTitle { get; set; }
        public string? SuggestedCounterparty { get; set; }
        public string? SuggestedNotes { get; set; }
        public string? Reason { get; set; }
    }

    private sealed class GeminiBookkeepingSummaryEnvelope
    {
        public string? Headline { get; set; }
        public List<string> Highlights { get; set; } = [];
    }
}
