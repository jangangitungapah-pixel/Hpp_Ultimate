using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;
using Hpp_Ultimate.Services;
using Xunit;

namespace Hpp_Ultimate.Tests;

public sealed class PhaseTwoWorkflowTests
{
    [Fact]
    public async Task RecipeCatalogService_SaveAsync_PersistsRecipeAndCalculatesTotals()
    {
        using var scope = new TestStoreScope();
        var store = scope.Store;
        var cache = new MemoryCache(new MemoryCacheOptions());
        var now = DateTime.Now;
        SeedAuthenticatedStaff(store, now);
        var material = SeedMaterial(store, now);
        var service = new RecipeCatalogService(cache, store, new WorkspaceAccessService(store), new AuditTrailService(store));
        var request = new RecipeUpsertRequest
        {
            Code = "RCP-001",
            Name = "Saus Dasar",
            OutputQuantity = 10m,
            OutputUnit = "pcs",
            PortionYield = 10m,
            PortionUnit = "pcs",
            Status = RecipeStatus.Active,
            Groups =
            [
                new RecipeGroupInput
                {
                    Name = "Utama",
                    Materials =
                    [
                        new RecipeMaterialInput
                        {
                            MaterialId = material.Id,
                            Quantity = 500m,
                            Unit = "gr",
                            WastePercent = 0m
                        }
                    ]
                }
            ],
            Costs =
            [
                new RecipeCostInput
                {
                    Type = RecipeCostType.Production,
                    Name = "Gas",
                    Amount = 2000m
                }
            ]
        };

        var result = await service.SaveAsync(request);
        var totals = service.CalculateTotals(request);

        Assert.True(result.Success);
        Assert.Single(store.Recipes);
        Assert.Equal(25000m, totals.MaterialCost);
        Assert.Equal(2000m, totals.OperationalCost);
        Assert.Equal(27000m, totals.TotalCost);
        Assert.Equal("Saus Dasar", store.Recipes.Single().Name);
    }

    [Fact]
    public async Task RecipeCatalogService_SaveAsync_WithManualSellingPrice_DerivesMarginFromSellingPrice()
    {
        using var scope = new TestStoreScope();
        var store = scope.Store;
        var cache = new MemoryCache(new MemoryCacheOptions());
        var now = DateTime.Now;
        SeedAuthenticatedStaff(store, now);
        var material = SeedMaterial(store, now);
        var service = new RecipeCatalogService(cache, store, new WorkspaceAccessService(store), new AuditTrailService(store));
        var request = new RecipeUpsertRequest
        {
            Code = "RCP-002",
            Name = "Saus Manual",
            OutputQuantity = 10m,
            OutputUnit = "pcs",
            PortionYield = 10m,
            PortionUnit = "pcs",
            SellingPriceMode = RecipeSellingPriceMode.ManualPrice,
            SuggestedSellingPrice = 5400m,
            Status = RecipeStatus.Active,
            Groups =
            [
                new RecipeGroupInput
                {
                    Name = "Utama",
                    Materials =
                    [
                        new RecipeMaterialInput
                        {
                            MaterialId = material.Id,
                            Quantity = 500m,
                            Unit = "gr",
                            WastePercent = 0m
                        }
                    ]
                }
            ],
            Costs =
            [
                new RecipeCostInput
                {
                    Type = RecipeCostType.Production,
                    Name = "Gas",
                    Amount = 2000m
                }
            ]
        };

        var totals = service.CalculateTotals(request);
        var result = await service.SaveAsync(request);
        var saved = Assert.Single(store.Recipes);

        Assert.True(result.Success);
        Assert.Equal(5400m, totals.SuggestedSellingPrice);
        Assert.Equal(100m, totals.TargetMarginPercent);
        Assert.Equal(RecipeSellingPriceMode.ManualPrice, saved.SellingPriceMode);
        Assert.Equal(5400m, saved.SuggestedSellingPrice);
        Assert.Equal(100m, saved.TargetMarginPercent);
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
    public async Task ProductionService_QueueStartAndCompleteProduction_ConsumesMaterialAndCreatesMenuStock()
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

        var service = new ProductionService(store, new WorkspaceAccessService(store), new AuditTrailService(store));
        var queued = await service.UpsertQueueAsync(new ProductionBatchCreateRequest
        {
            RecipeId = recipe.Id,
            BatchCount = 2,
            TargetDurationMinutes = 30,
            Notes = "Batch test"
        });
        var batchId = Assert.Single(store.ProductionBatches).Id;
        var started = await service.StartQueuedProductionAsync(batchId);
        var completed = await service.CompleteProductionAsync(batchId);
        var batch = Assert.Single(store.ProductionBatches);

        Assert.True(queued.Success);
        Assert.True(started.Success);
        Assert.True(completed.Success);
        Assert.Equal(0m, store.StockMovements.Where(item => item.MaterialId == material.Id).Sum(item => item.Quantity));
        Assert.Equal(50000m, batch.MaterialCost);
        Assert.Equal(0m, batch.LaborCost);
        Assert.Equal(0m, batch.OverheadCost);
        Assert.Equal(ProductionRunStatus.Completed, batch.Status);
        Assert.Equal(20m, InventoryMath.GetRecipeMenuOnHand(store, recipe.Id));
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
