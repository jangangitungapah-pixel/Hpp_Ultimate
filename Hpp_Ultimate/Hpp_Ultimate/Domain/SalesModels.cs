using System.ComponentModel.DataAnnotations;

namespace Hpp_Ultimate.Domain;

public enum SaleStatus
{
    Completed,
    Voided
}

public sealed record SaleTransaction(
    Guid Id,
    string ReceiptNumber,
    DateTime SoldAt,
    Guid? CashierUserId,
    string CashierName,
    string PaymentMethod,
    int LineCount,
    int TotalQuantity,
    decimal GrossRevenue,
    decimal TotalHpp,
    decimal GrossProfit,
    decimal AmountReceived,
    decimal ChangeAmount,
    SaleStatus Status,
    string? Notes,
    string? CustomerName = null,
    bool IsPaid = true,
    DateTime? PaidAt = null,
    string? VoidReason = null,
    DateTime? VoidedAt = null);

public sealed record SaleLine(
    Guid Id,
    Guid SaleId,
    Guid ProductId,
    Guid RecipeId,
    string ProductCode,
    string ProductName,
    string UnitLabel,
    int Quantity,
    decimal UnitPrice,
    decimal HppPerUnit);

public sealed record PosProductOption(
    Guid ProductId,
    Guid RecipeId,
    string ProductCode,
    string ProductName,
    string UnitLabel,
    string? Category,
    decimal OnHandQuantity,
    decimal HppPerUnit,
    decimal SuggestedPrice,
    decimal SellingPrice,
    decimal ProfitPerUnit,
    string RecipeCode,
    string RecipeName,
    bool CanSell,
    string AvailabilityMessage,
    DateTime UpdatedAt);

public sealed record PosProductDetail(
    RecipeBook Recipe,
    decimal OnHandQuantity,
    decimal HppPerUnit,
    decimal SuggestedPrice,
    HppRecipeBreakdown Breakdown);

public sealed record PosSalesHistoryItem(
    Guid Id,
    string ReceiptNumber,
    DateTime SoldAt,
    string CustomerName,
    string MenuSummary,
    string CashierName,
    string PaymentMethod,
    int TotalQuantity,
    decimal GrossRevenue,
    decimal GrossProfit,
    bool IsPaid,
    SaleStatus Status);

public sealed record PosSnapshot(
    IReadOnlyList<PosProductOption> Products,
    Guid? SelectedProductId,
    PosProductDetail? SelectedProduct,
    IReadOnlyList<PosSalesHistoryItem> RecentSales);

public sealed record PosCheckoutResult(
    bool Success,
    string Message,
    SaleTransaction? Sale = null);

public sealed class PosCheckoutLineRequest
{
    [Required]
    public Guid? ProductId { get; set; }

    [Required]
    public Guid? RecipeId { get; set; }

    [Required]
    public string ProductCode { get; set; } = string.Empty;

    [Required]
    public string ProductName { get; set; } = string.Empty;

    [Required]
    public string UnitLabel { get; set; } = "pcs";

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 1;

    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; set; }

    [Range(0, double.MaxValue)]
    public decimal HppPerUnit { get; set; }
}

public sealed class PosCheckoutRequest
{
    public List<PosCheckoutLineRequest> Lines { get; set; } = [];

    public DateTime SoldAt { get; set; } = DateTime.Now;

    [Required(ErrorMessage = "Nama pembeli wajib diisi.")]
    public string CustomerName { get; set; } = string.Empty;

    [Required]
    public string PaymentMethod { get; set; } = "Cash";

    [Range(0, double.MaxValue)]
    public decimal AmountReceived { get; set; }

    public string? Notes { get; set; }
}

public sealed record SalesHistoryLineItem(
    Guid Id,
    string ProductCode,
    string ProductName,
    string UnitLabel,
    int Quantity,
    decimal UnitPrice,
    decimal HppPerUnit,
    decimal Revenue,
    decimal Profit);

public sealed record SaleDetail(
    SaleTransaction Sale,
    IReadOnlyList<SalesHistoryLineItem> Lines);

public sealed record SalePaymentResult(
    bool Success,
    string Message,
    SaleTransaction? Sale = null);

public sealed record SalesHistorySnapshot(
    IReadOnlyList<PosSalesHistoryItem> Items,
    int TotalCount,
    decimal TotalRevenue,
    decimal TotalProfit,
    Guid? SelectedSaleId,
    SaleDetail? Detail);

public sealed record SalesDailyReportItem(
    DateOnly Date,
    int TransactionCount,
    int TotalQuantity,
    decimal GrossRevenue,
    decimal GrossProfit);

public sealed record ProductMarginReportItem(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    int QuantitySold,
    decimal Revenue,
    decimal Hpp,
    decimal Profit,
    decimal MarginPercent);

public sealed record MaterialUsageReportItem(
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string BaseUnit,
    decimal QuantityUsed,
    decimal EstimatedCost);

public sealed record MaterialPriceTrendItem(
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    DateTime EffectiveAt,
    decimal PricePerPack,
    decimal PricePerUnit,
    string Note);

public sealed record SalesReportSnapshot(
    DateOnly DateFrom,
    DateOnly DateTo,
    int TransactionCount,
    int ItemCount,
    decimal GrossRevenue,
    decimal GrossProfit,
    decimal AverageTicket,
    IReadOnlyList<SalesDailyReportItem> DailySales,
    IReadOnlyList<ProductMarginReportItem> TopProducts,
    IReadOnlyList<ProductMarginReportItem> MarginByProduct,
    IReadOnlyList<MaterialUsageReportItem> MaterialUsage,
    IReadOnlyList<MaterialPriceTrendItem> PriceTrends);

public sealed class VoidSaleRequest
{
    [Required]
    public Guid? SaleId { get; set; }

    [Required(ErrorMessage = "Alasan void wajib diisi.")]
    public string Reason { get; set; } = string.Empty;
}
