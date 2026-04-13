namespace Hpp_Ultimate.Domain;

public sealed record ShoppingAiReceiptRequest(
    string FileName,
    string ContentType,
    string Base64Content,
    IReadOnlyList<ShoppingMaterialOption> Materials);

public sealed record ShoppingAiReceiptLine(
    string RawItemName,
    string? MaterialCode,
    int PackCount,
    decimal? PricePerPack,
    decimal? LineSubtotal,
    string? MatchReason,
    string? SuggestedBrand,
    string? SuggestedBaseUnit,
    decimal? SuggestedNetQuantity,
    string? SuggestedNetUnit);

public sealed record ShoppingAiReceiptDraft(
    string? SupplierName,
    DateTime? OrderedAt,
    PurchaseChannel Channel,
    string? EcommercePlatform,
    decimal ShippingCost,
    string? Notes,
    IReadOnlyList<ShoppingAiReceiptLine> Lines,
    IReadOnlyList<string> Warnings);

public sealed record ShoppingAiParseResult(
    bool Success,
    string Message,
    ShoppingAiReceiptDraft? Draft = null);
