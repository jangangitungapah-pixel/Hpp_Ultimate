using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;
using Hpp_Ultimate.Services;
using Xunit;

namespace Hpp_Ultimate.Tests;

public sealed class PhaseTwoWorkflowTests
{
    [Fact]
    public async Task ProductCatalogService_LinkRecipeAsync_SyncsBom()
    {
        using var scope = new TestStoreScope();
        var store = scope.Store;
        var now = DateTime.Now;
        SeedAuthenticatedStaff(store, now);
        var material = SeedMaterial(store, now);
        var recipe = new RecipeBook(
            Guid.NewGuid(),
            "RCP-001",
            "Saus Dasar",
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
        var product = new Product(Guid.NewGuid(), "PRD-001", "Saus Botol", "Saus", "pcs", 25000m, string.Empty, ProductStatus.Active, now, now);
        store.AddProduct(product);

        var service = new ProductCatalogService(new MemoryCache(new MemoryCacheOptions()), store, new WorkspaceAccessService(store), new AuditTrailService(store));
        var result = await service.LinkRecipeAsync(new ProductRecipeLinkRequest
        {
            ProductId = product.Id,
            RecipeId = recipe.Id,
            YieldPercentage = 100m
        });

        Assert.True(result.Success);
        Assert.Single(store.BomItems, item => item.ProductId == product.Id);
        Assert.Equal(50m, store.BomItems.Single(item => item.ProductId == product.Id).QuantityPerUnit);
    }

    [Fact]
    public async Task WarehouseService_RecordMovementAsync_TracksSignedBalance()
    {
        using var scope = new TestStoreScope();
        var store = scope.Store;
        var now = DateTime.Now;
        var material = SeedMaterial(store, now);
        SeedAuthenticatedStaff(store, now);

        var service = new WarehouseService(new MemoryCache(new MemoryCacheOptions()), store, new WorkspaceAccessService(store), new AuditTrailService(store));

        var opening = await service.RecordMovementAsync(new StockMovementRequest
        {
            MaterialId = material.Id,
            Type = StockMovementType.OpeningBalance,
            Quantity = 100m
        });

        var outgoing = await service.RecordMovementAsync(new StockMovementRequest
        {
            MaterialId = material.Id,
            Type = StockMovementType.StockOut,
            Quantity = 35m
        });

        Assert.True(opening.Success);
        Assert.True(outgoing.Success);
        Assert.Equal(65m, store.StockMovements.Where(item => item.MaterialId == material.Id).Sum(item => item.Quantity));
    }

    [Fact]
    public async Task ProductionService_RecordBatchAsync_ConsumesMaterialAndStoresBatchCosts()
    {
        using var scope = new TestStoreScope();
        var store = scope.Store;
        var now = DateTime.Now;
        var material = SeedMaterial(store, now);
        SeedAuthenticatedStaff(store, now);
        store.AddStockMovement(new StockMovementEntry(Guid.NewGuid(), material.Id, StockMovementType.OpeningBalance, 1000m, now, "Saldo awal"));

        var recipe = new RecipeBook(
            Guid.NewGuid(),
            "RCP-001",
            "Saus Dasar",
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
            [new RecipeCostComponent(Guid.NewGuid(), RecipeCostType.Production, "Gas", 2000m, null)],
            now,
            now,
            10m);
        store.AddRecipeBook(recipe);

        var product = new Product(Guid.NewGuid(), "PRD-001", "Saus Botol", "Saus", "pcs", 25000m, string.Empty, ProductStatus.Active, now, now);
        store.AddProduct(product);
        store.UpsertRecipe(new ProductRecipe(product.Id, recipe.OutputQuantity, 100m, null, now, recipe.Id));
        store.ReplaceBomItems(product.Id, [new BomItem(product.Id, material.Id, 50m)]);

        var service = new ProductionService(store, new WorkspaceAccessService(store), new AuditTrailService(store));
        var result = await service.RecordBatchAsync(new ProductionBatchCreateRequest
        {
            ProductId = product.Id,
            QuantityProduced = 8,
            LaborCosts = [new ProductionCostInput { Name = "Operator", Amount = 10000m }],
            OverheadCosts = [new ProductionCostInput { Name = "Listrik", Amount = 5000m }]
        });

        Assert.True(result.Success);
        Assert.Single(store.ProductionBatches);
        Assert.Equal(600m, store.StockMovements.Where(item => item.MaterialId == material.Id).Sum(item => item.Quantity));
        var batch = store.ProductionBatches.Single();
        Assert.Equal(20000m, batch.MaterialCost);
        Assert.Equal(10000m, batch.LaborCost);
        Assert.Equal(5000m, batch.OverheadCost);
    }

    private static void SeedAuthenticatedStaff(SeededBusinessDataStore store, DateTime now)
    {
        var user = new BusinessUser(Guid.NewGuid(), "Operator", "operator@test.local", "operator", UserRole.Staff, UserStatus.Active, PasswordHasher.HashPassword("secret123"), now, now, null);
        store.AddUser(user);
        store.SetSession(new AuthSession(user.Id, user.FullName, user.Email, user.Role, now, false));
    }

    private static RawMaterial SeedMaterial(SeededBusinessDataStore store, DateTime now)
        => store.AddRawMaterial(new RawMaterial(
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
}
