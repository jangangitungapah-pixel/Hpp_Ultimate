using System.Text;
using System.Text.Json;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class DataOpsService(
    IHostEnvironment environment,
    SeededBusinessDataStore store,
    WorkspaceAccessService access,
    AuditTrailService auditTrail)
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public Task<DataOpsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAdmin();
        if (!accessDecision.Allowed)
        {
            throw new InvalidOperationException(accessDecision.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DataOpsSnapshot(
            ListFiles(GetBackupDirectory(), "Backup"),
            ListFiles(GetExportDirectory(), "Export"),
            Directory.Exists(GetBackupDirectory()) ? Directory.GetFiles(GetBackupDirectory()).Length : 0,
            Directory.Exists(GetExportDirectory()) ? Directory.GetFiles(GetExportDirectory()).Length : 0));
    }

    public async Task<DataOperationResult> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAdmin();
        if (!accessDecision.Allowed)
        {
            return new DataOperationResult(false, accessDecision.Message);
        }

        var actor = accessDecision.Actor!;
        var path = Path.Combine(GetBackupDirectory(), $"hpp-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(store.CreateSnapshot(), _jsonOptions);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8, cancellationToken);
        auditTrail.Record(actor, "DataOps", "Create backup", Path.GetFileName(path), null, $"Backup penuh dibuat ke {path}.");
        return new DataOperationResult(true, "Backup penuh berhasil dibuat.", path);
    }

    public async Task<DataOperationResult> RestoreBackupAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAdmin();
        if (!accessDecision.Allowed)
        {
            return new DataOperationResult(false, accessDecision.Message);
        }

        var actor = accessDecision.Actor!;
        var path = ResolveFile(GetBackupDirectory(), fileName);
        if (path is null || !File.Exists(path))
        {
            return new DataOperationResult(false, "File backup tidak ditemukan.");
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var snapshot = JsonSerializer.Deserialize<AppDataSnapshot>(json, _jsonOptions);
        if (snapshot is null)
        {
            return new DataOperationResult(false, "Isi backup tidak valid.");
        }

        store.RestoreSnapshot(snapshot, clearSession: true);
        auditTrail.Record(actor, "DataOps", "Restore backup", fileName, null, $"Backup {fileName} dipulihkan.");
        return new DataOperationResult(true, "Backup berhasil dipulihkan. Sesi login lama telah ditutup.", path);
    }

    public async Task<DataOperationResult> ExportRecipesAsync(CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAdmin();
        if (!accessDecision.Allowed)
        {
            return new DataOperationResult(false, accessDecision.Message);
        }

        var actor = accessDecision.Actor!;
        var path = Path.Combine(GetExportDirectory(), $"recipes-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(store.Recipes, _jsonOptions);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8, cancellationToken);
        auditTrail.Record(actor, "DataOps", "Export recipes", Path.GetFileName(path), null, $"Export resep dibuat ke {path}.");
        return new DataOperationResult(true, "Export resep berhasil dibuat.", path);
    }

    public async Task<DataOperationResult> ExportSalesJsonAsync(CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAdmin();
        if (!accessDecision.Allowed)
        {
            return new DataOperationResult(false, accessDecision.Message);
        }

        var actor = accessDecision.Actor!;
        var path = Path.Combine(GetExportDirectory(), $"sales-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bundle = new SalesDataBundle(store.Sales.ToArray(), store.SaleLines.ToArray());
        var json = JsonSerializer.Serialize(bundle, _jsonOptions);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8, cancellationToken);
        auditTrail.Record(actor, "DataOps", "Export sales", Path.GetFileName(path), null, $"Export transaksi dibuat ke {path}.");
        return new DataOperationResult(true, "Export transaksi JSON berhasil dibuat.", path);
    }

    public async Task<DataOperationResult> ExportSalesCsvAsync(CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAdmin();
        if (!accessDecision.Allowed)
        {
            return new DataOperationResult(false, accessDecision.Message);
        }

        var actor = accessDecision.Actor!;
        var path = Path.Combine(GetExportDirectory(), $"sales-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var lines = new List<string>
        {
            "\"receipt\",\"sold_at\",\"cashier\",\"payment_method\",\"status\",\"product_code\",\"product_name\",\"qty\",\"unit_price\",\"hpp_per_unit\",\"revenue\",\"profit\""
        };

        foreach (var sale in store.Sales.OrderByDescending(item => item.SoldAt))
        {
            foreach (var line in store.FindSaleLines(sale.Id))
            {
                lines.Add(Csv(
                    sale.ReceiptNumber,
                    sale.SoldAt.ToString("O"),
                    sale.CashierName,
                    sale.PaymentMethod,
                    sale.Status,
                    line.ProductCode,
                    line.ProductName,
                    line.Quantity,
                    line.UnitPrice,
                    line.HppPerUnit,
                    line.UnitPrice * line.Quantity,
                    (line.UnitPrice - line.HppPerUnit) * line.Quantity));
            }
        }

        await File.WriteAllTextAsync(path, string.Join(Environment.NewLine, lines), Encoding.UTF8, cancellationToken);
        auditTrail.Record(actor, "DataOps", "Export sales csv", Path.GetFileName(path), null, $"Export transaksi CSV dibuat ke {path}.");
        return new DataOperationResult(true, "Export transaksi CSV berhasil dibuat.", path);
    }

    public async Task<DataOperationResult> ImportRecipesAsync(string fileName, Stream stream, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAdmin();
        if (!accessDecision.Allowed)
        {
            return new DataOperationResult(false, accessDecision.Message);
        }

        var actor = accessDecision.Actor!;
        var recipes = await JsonSerializer.DeserializeAsync<List<RecipeBook>>(stream, _jsonOptions, cancellationToken);
        if (recipes is null || recipes.Count == 0)
        {
            return new DataOperationResult(false, "File resep kosong atau tidak valid.");
        }

        var imported = 0;
        foreach (var recipe in recipes.Select(SanitizeRecipe))
        {
            var existing = store.FindRecipeBook(recipe.Id) ?? store.Recipes.FirstOrDefault(item => item.Code.Equals(recipe.Code, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                store.AddRecipeBook(recipe);
            }
            else
            {
                store.UpdateRecipeBook(recipe with { Id = existing.Id, CreatedAt = existing.CreatedAt, UpdatedAt = DateTime.Now });
            }
            imported++;
        }

        auditTrail.Record(actor, "DataOps", "Import recipes", fileName, null, $"{imported} resep diimpor dari file.");
        return new DataOperationResult(true, $"{imported} resep berhasil diimpor.");
    }

    public async Task<DataOperationResult> ImportSalesAsync(string fileName, Stream stream, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAdmin();
        if (!accessDecision.Allowed)
        {
            return new DataOperationResult(false, accessDecision.Message);
        }

        var actor = accessDecision.Actor!;
        var bundle = await JsonSerializer.DeserializeAsync<SalesDataBundle>(stream, _jsonOptions, cancellationToken);
        if (bundle is null || bundle.Sales.Count == 0)
        {
            return new DataOperationResult(false, "File transaksi kosong atau tidak valid.");
        }

        var imported = 0;
        foreach (var sale in bundle.Sales)
        {
            if (store.Sales.Any(item => item.Id == sale.Id || item.ReceiptNumber.Equals(sale.ReceiptNumber, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var lines = bundle.Lines.Where(item => item.SaleId == sale.Id).ToArray();
            if (lines.Length == 0)
            {
                continue;
            }

            store.AddSale(sale, lines);
            imported++;
        }

        auditTrail.Record(actor, "DataOps", "Import sales", fileName, null, $"{imported} transaksi diimpor dari file.");
        return new DataOperationResult(true, $"{imported} transaksi berhasil diimpor.");
    }

    private string GetBackupDirectory()
        => Path.Combine(environment.ContentRootPath, "App_Data", "Backups");

    private string GetExportDirectory()
        => Path.Combine(environment.ContentRootPath, "App_Data", "Exports");

    private static IReadOnlyList<BackupFileItem> ListFiles(string directory, string kind)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<BackupFileItem>();
        }

        return Directory.GetFiles(directory)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new BackupFileItem(info.Name, info.FullName, info.Length, info.LastWriteTime, kind);
            })
            .OrderByDescending(item => item.UpdatedAt)
            .ToArray();
    }

    private static string? ResolveFile(string directory, string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return null;
        }

        return Path.Combine(directory, safeName);
    }

    private static RecipeBook SanitizeRecipe(RecipeBook recipe)
    {
        var portionYield = recipe.PortionYield <= 0 ? (recipe.OutputQuantity <= 0 ? 1m : recipe.OutputQuantity) : recipe.PortionYield;
        var portionUnit = RecipePortionUnitCatalog.Normalize(
            string.IsNullOrWhiteSpace(recipe.PortionUnit)
                ? recipe.OutputUnit
                : recipe.PortionUnit);

        return recipe with
        {
            OutputQuantity = portionYield,
            OutputUnit = portionUnit,
            PortionYield = portionYield,
            PortionUnit = portionUnit
        };
    }

    private static string Csv(params object?[] values)
        => string.Join(",", values.Select(value =>
        {
            var text = value?.ToString() ?? string.Empty;
            return $"\"{text.Replace("\"", "\"\"")}\"";
        }));
}
