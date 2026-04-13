using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed record GeminiAiOptions(
    string? ApiKey,
    string Model,
    string EndpointBaseUrl)
{
    public static GeminiAiOptions FromConfiguration(IConfiguration configuration, BusinessSettings? settings = null)
        => new(
            settings?.GeminiApiKey
                ?? configuration["Gemini:ApiKey"]
                ?? configuration["GEMINI_API_KEY"],
            configuration["Gemini:Model"]
                ?? "gemini-2.5-flash",
            configuration["Gemini:EndpointBaseUrl"]
                ?? "https://generativelanguage.googleapis.com/v1beta");
}
