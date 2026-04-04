using System.ComponentModel.DataAnnotations;

namespace Hpp_Ultimate.Domain;

public sealed record ShoppingMaterialOption(
    Guid MaterialId,
    string Code,
    string Name,
    string? Brand,
    string BaseUnit,
    decimal NetQuantity,
    string NetUnit,
    decimal PricePerPack,
    decimal CostPerBaseUnit,
    decimal OnHandQuantity,
    string LookupLabel);

public sealed record ShoppingMaterialDetail(
    RawMaterial Material,
    decimal OnHandQuantity,
    string LookupLabel);

public sealed record ShoppingHistoryItem(
    Guid Id,
    string PurchaseNumber,
    DateTime OrderedAt,
    string SupplierName,
    PurchaseChannel Channel,
    string? EcommercePlatform,
    string ItemSummary,
    int LineCount,
    int TotalPackCount,
    decimal Subtotal,
    decimal ShippingCost,
    decimal GrandTotal,
    PurchaseOrderStatus Status,
    DateTime? ReceivedAt,
    bool HasReceipt,
    string? ReceiptFileName,
    bool CanReceive);

public sealed record ShoppingSnapshot(
    IReadOnlyList<ShoppingMaterialOption> Materials,
    Guid? SelectedMaterialId,
    ShoppingMaterialDetail? SelectedMaterial,
    IReadOnlyList<ShoppingHistoryItem> History,
    int PendingReceiptCount,
    int TotalOrderCount,
    decimal TotalSpend);

public sealed record ShoppingMutationResult(
    bool Success,
    string Message,
    PurchaseOrder? Order = null);

public sealed class ShoppingCartLineRequest
{
    [Required(ErrorMessage = "Material wajib dipilih.")]
    public Guid? MaterialId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Jumlah pack minimal 1.")]
    public int PackCount { get; set; } = 1;
}

public sealed class ShoppingCheckoutRequest
{
    public List<ShoppingCartLineRequest> Lines { get; set; } = [];

    public DateTime OrderedAt { get; set; } = DateTime.Now;

    [Required(ErrorMessage = "Nama toko / supplier wajib diisi.")]
    public string SupplierName { get; set; } = string.Empty;

    public PurchaseChannel Channel { get; set; } = PurchaseChannel.Offline;

    public string? EcommercePlatform { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Ongkir tidak boleh negatif.")]
    public decimal ShippingCost { get; set; }

    public string? Notes { get; set; }
}

public sealed class ShoppingReceiptUploadRequest
{
    [Required]
    public Guid? PurchaseOrderId { get; set; }

    [Required]
    public string FileName { get; set; } = string.Empty;

    [Required]
    public string ContentType { get; set; } = "application/octet-stream";

    [Required]
    public string Base64Content { get; set; } = string.Empty;
}
