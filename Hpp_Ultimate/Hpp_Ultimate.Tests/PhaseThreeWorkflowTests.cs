using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Hpp_Ultimate.Domain;
using Hpp_Ultimate.Services;
using Xunit;

namespace Hpp_Ultimate.Tests;

public sealed class PhaseThreeWorkflowTests
{
    [Fact]
    public async Task SalesService_CheckoutAndVoid_AdjustRecipeMenuOnHand()
    {
        using var scope = new TestStoreScope();
        var now = DateTime.Now;
        var store = scope.Store;
        var seeded = SeedSalesContext(store, now, UserRole.Staff, includeSeedSale: false);
        var access = new WorkspaceAccessService(store);
        var salesService = new SalesService(
            new MemoryCache(new MemoryCacheOptions()),
            store,
            access,
            new AuditTrailService(store),
            new HppCalculatorService(new MemoryCache(new MemoryCacheOptions()), store, access));

        var checkout = await salesService.CheckoutAsync(new PosCheckoutRequest
        {
            CustomerName = "Pelanggan Test",
            PaymentMethod = "Cash",
            AmountReceived = 100000m,
            Lines =
            [
                new PosCheckoutLineRequest
                {
                    ProductId = seeded.Product.Id,
                    RecipeId = seeded.Recipe.Id,
                    ProductCode = seeded.Product.Code,
                    ProductName = seeded.Product.Name,
                    UnitLabel = seeded.Product.Unit,
                    Quantity = 3,
                    UnitPrice = 25000m,
                    HppPerUnit = 12000m
                }
            ]
        });

        Assert.True(checkout.Success);
        Assert.Single(store.Sales);
        Assert.Equal(7m, InventoryMath.GetRecipeMenuOnHand(store, seeded.Recipe.Id));

        var voidResult = await salesService.VoidSaleAsync(new VoidSaleRequest
        {
            SaleId = checkout.Sale!.Id,
            Reason = "Salah input kasir"
        });

        Assert.True(voidResult.Success);
        Assert.Equal(SaleStatus.Voided, store.Sales.Single().Status);
        Assert.Equal(10m, InventoryMath.GetRecipeMenuOnHand(store, seeded.Recipe.Id));
    }

    [Fact]
    public async Task BookkeepingService_GetSnapshotAsync_AggregatesSalesAndManualEntries()
    {
        using var scope = new TestStoreScope();
        var now = DateTime.Now;
        var store = scope.Store;
        var seeded = SeedSalesContext(store, now, UserRole.Admin, includeSeedSale: false);
        var access = new WorkspaceAccessService(store);
        var salesService = new SalesService(
            new MemoryCache(new MemoryCacheOptions()),
            store,
            access,
            new AuditTrailService(store),
            new HppCalculatorService(new MemoryCache(new MemoryCacheOptions()), store, access));
        var bookkeeping = new BookkeepingService(
            new MemoryCache(new MemoryCacheOptions()),
            store,
            access,
            new AuditTrailService(store));

        var checkout = await salesService.CheckoutAsync(new PosCheckoutRequest
        {
            CustomerName = "Pelanggan Test",
            PaymentMethod = "Cash",
            AmountReceived = 50000m,
            Lines =
            [
                new PosCheckoutLineRequest
                {
                    ProductId = seeded.Product.Id,
                    RecipeId = seeded.Recipe.Id,
                    ProductCode = seeded.Product.Code,
                    ProductName = seeded.Product.Name,
                    UnitLabel = seeded.Product.Unit,
                    Quantity = 2,
                    UnitPrice = 25000m,
                    HppPerUnit = 12000m
                }
            ]
        });

        var manual = await bookkeeping.AddManualEntryAsync(new ManualLedgerEntryRequest
        {
            OccurredAt = now.AddMinutes(5),
            Title = "Biaya parkir",
            Direction = LedgerEntryDirection.Expense,
            Amount = 5000m
        });

        var snapshot = await bookkeeping.GetSnapshotAsync();

        Assert.True(checkout.Success);
        Assert.True(manual.Success);
        Assert.Equal(2, snapshot.TotalEntries);
        Assert.Equal(50000m, snapshot.TotalIncome);
        Assert.Equal(5000m, snapshot.TotalExpense);
        Assert.Equal(45000m, snapshot.ClosingBalance);
        Assert.Contains(snapshot.Items, item => item.SourceType == "POS");
        Assert.Contains(snapshot.Items, item => item.SourceType == "Manual");
    }

    [Fact]
    public async Task DataOpsService_CreateBackupAndRestore_RestoresSnapshotAndClearsSession()
    {
        using var scope = new TestStoreScope();
        using var tempRoot = new TemporaryDirectory();
        var now = DateTime.Now;
        var store = scope.Store;
        SeedSalesContext(store, now, UserRole.Admin, includeSeedSale: true);
        var dataOps = new DataOpsService(
            new TestHostEnvironment(tempRoot.PathValue),
            store,
            new WorkspaceAccessService(store),
            new AuditTrailService(store));

        var backup = await dataOps.CreateBackupAsync();
        Assert.True(backup.Success);
        Assert.NotNull(backup.AbsolutePath);
        Assert.True(File.Exists(backup.AbsolutePath));

        store.ClearOperationalData();
        Assert.Empty(store.Products);

        var restore = await dataOps.RestoreBackupAsync(Path.GetFileName(backup.AbsolutePath!));
        Assert.True(restore.Success);
        Assert.NotEmpty(store.Products);
        Assert.NotEmpty(store.Sales);
        Assert.Null(store.AuthSession);
    }

    private static SeededSalesContext SeedSalesContext(SeededBusinessDataStore store, DateTime now, UserRole role, bool includeSeedSale)
    {
        var user = new BusinessUser(Guid.NewGuid(), "Kasir Test", "kasir@test.local", "kasir", role, UserStatus.Active, PasswordHasher.HashPassword("secret123"), now, now, null);
        store.AddUser(user);
        store.SetSession(new AuthSession(user.Id, user.FullName, user.Email, user.Role, now, false));

        var material = store.AddRawMaterial(new RawMaterial(
            Guid.NewGuid(),
            "BHN-001",
            "Tomat",
            "Lokal",
            "gr",
            1000m,
            "gr",
            50000m,
            [],
            null,
            MaterialStatus.Active,
            now,
            now,
            0m));

        store.AddStockMovement(new StockMovementEntry(Guid.NewGuid(), material.Id, StockMovementType.OpeningBalance, 1000m, now, "Saldo awal"));

        var recipe = new RecipeBook(
            Guid.NewGuid(),
            "RCP-001",
            "Sambal Jadi",
            null,
            10m,
            "pcs",
            RecipeStatus.Active,
            [
                new RecipeMaterialGroup(
                    Guid.NewGuid(),
                    "Utama",
                    null,
                    [new RecipeMaterialLine(Guid.NewGuid(), material.Id, 500m, "gr", 0m, null)])
            ],
            [],
            now,
            now,
            10m);
        store.AddRecipeBook(recipe);

        var product = new Product(Guid.NewGuid(), "PRD-001", "Sambal Botol", "Saus", "pcs", 25000m, string.Empty, ProductStatus.Active, now, now);
        store.AddProduct(product);
        store.UpsertRecipe(new ProductRecipe(product.Id, recipe.OutputQuantity, 100m, null, now, recipe.Id));
        store.ReplaceBomItems(product.Id, [new BomItem(product.Id, material.Id, 50m)]);

        var batchId = Guid.NewGuid();
        store.AddProductionBatch(
            new ProductionBatch(batchId, Guid.Empty, "BATCH-001", now, 1, now, recipe.Id, "Batch awal", 25000m, 0m, 0m, 1, recipe.PortionYield, 30, ProductionRunStatus.Completed, now),
            [],
            [],
            []);

        if (includeSeedSale)
        {
            var saleId = Guid.NewGuid();
            store.AddSale(
                new SaleTransaction(
                    saleId,
                    "TRX-000001",
                    now,
                    user.Id,
                    user.FullName,
                    "Cash",
                    1,
                    1,
                    25000m,
                    12000m,
                    13000m,
                30000m,
                5000m,
                SaleStatus.Completed,
                "Seed sale",
                "Pelanggan Seed",
                true,
                now),
                [
                    new SaleLine(Guid.NewGuid(), saleId, product.Id, recipe.Id, product.Code, product.Name, product.Unit, 1, 25000m, 12000m)
                ]);
        }

        return new SeededSalesContext(user, material, recipe, product);
    }

    private sealed record SeededSalesContext(BusinessUser User, RawMaterial Material, RecipeBook Recipe, Product Product);

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Hpp_Ultimate.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            PathValue = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hpp-ultimate-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(PathValue);
        }

        public string PathValue { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(PathValue))
                {
                    Directory.Delete(PathValue, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
