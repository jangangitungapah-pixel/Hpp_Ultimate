using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Hpp_Ultimate.Domain;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hpp_Ultimate.Services;

public sealed class SeededBusinessDataStore
{
    private const string TableName = "AppState";
    private const string DemoSeedAction = "Seed demo operasi 6 hari";
    private readonly Lock _gate = new();
    private readonly BusinessDataStoreProvider _provider;
    private readonly string _connectionString;
    private readonly string? _localSqlitePath;
    private readonly ILogger<SeededBusinessDataStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly List<Product> _products = [];
    private readonly List<RawMaterial> _rawMaterials = [];
    private readonly List<MaterialPriceEntry> _materialPrices = [];
    private readonly List<RecipeBook> _recipes = [];
    private readonly List<StockMovementEntry> _stockMovements = [];
    private readonly List<ProductRecipe> _productRecipes = [];
    private readonly List<BomItem> _bomItems = [];
    private readonly List<ProductionBatch> _productionBatches = [];
    private readonly List<LaborCostEntry> _laborCosts = [];
    private readonly List<OverheadCostEntry> _overheadCosts = [];
    private readonly List<PurchaseOrder> _purchaseOrders = [];
    private readonly List<PurchaseOrderLine> _purchaseOrderLines = [];
    private readonly List<ManualLedgerEntry> _manualLedgerEntries = [];
    private readonly List<SaleTransaction> _sales = [];
    private readonly List<SaleLine> _saleLines = [];
    private readonly List<BusinessUser> _users = [];
    private readonly List<AuditLogEntry> _auditLogEntries = [];
    private AuthSession? _authSession;
    private BusinessSettings _businessSettings = new(
        "Usaha Baru",
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        "IDR",
        "pcs",
        "kg",
        100,
        11m,
        false,
        "Asia/Jakarta",
        DateTime.Now,
        string.Empty);

    public SeededBusinessDataStore(SeededBusinessDataStoreOptions options, ILogger<SeededBusinessDataStore> logger)
    {
        _provider = options.Provider;
        _connectionString = options.ConnectionString;
        _localSqlitePath = options.LocalSqlitePath;
        _logger = logger;
        LoadOrInitialize();
    }

    public event Action? Changed;

    public long Version { get; private set; } = 1;

    public IReadOnlyList<Product> Products => _products;
    public IReadOnlyList<RawMaterial> RawMaterials => _rawMaterials;
    public IReadOnlyList<MaterialPriceEntry> MaterialPrices => _materialPrices;
    public IReadOnlyList<RecipeBook> Recipes => _recipes;
    public IReadOnlyList<StockMovementEntry> StockMovements => _stockMovements;
    public IReadOnlyList<ProductRecipe> ProductRecipes => _productRecipes;
    public IReadOnlyList<BomItem> BomItems => _bomItems;
    public IReadOnlyList<ProductionBatch> ProductionBatches => _productionBatches;
    public IReadOnlyList<LaborCostEntry> LaborCosts => _laborCosts;
    public IReadOnlyList<OverheadCostEntry> OverheadCosts => _overheadCosts;
    public IReadOnlyList<PurchaseOrder> PurchaseOrders => _purchaseOrders;
    public IReadOnlyList<PurchaseOrderLine> PurchaseOrderLines => _purchaseOrderLines;
    public IReadOnlyList<ManualLedgerEntry> ManualLedgerEntries => _manualLedgerEntries;
    public IReadOnlyList<SaleTransaction> Sales => _sales;
    public IReadOnlyList<SaleLine> SaleLines => _saleLines;
    public IReadOnlyList<BusinessUser> Users => _users;
    public IReadOnlyList<AuditLogEntry> AuditLogEntries => _auditLogEntries;
    public AuthSession? AuthSession => _authSession;
    public BusinessSettings BusinessSettings => _businessSettings;

    private void LoadOrInitialize()
    {
        if (_provider == BusinessDataStoreProvider.Sqlite &&
            !string.IsNullOrWhiteSpace(_localSqlitePath) &&
            Path.GetDirectoryName(_localSqlitePath) is { } sqliteDirectory)
        {
            Directory.CreateDirectory(sqliteDirectory);
        }

        EnsureDatabase();

        if (!TryLoadFromDatabase())
        {
            if (TryImportLegacySqliteState() && TryLoadFromDatabase())
            {
                _logger.LogInformation("Imported legacy SQLite state into Postgres successfully.");
            }
            else
            {
                InitializeEmptyState();
                PersistStateUnsafe();
            }
        }

        EnsureOperationalDemoSeed();
    }

    private void EnsureOperationalDemoSeed()
    {
        if (_auditLogEntries.Any(item => item.Area == "System" && item.Action == DemoSeedAction))
        {
            return;
        }

        if (_rawMaterials.Count >= 10 || _recipes.Count >= 10 || _purchaseOrders.Count >= 10 || _sales.Count >= 10)
        {
            return;
        }

        GenerateOperationalDemoSeed();
        PersistStateUnsafe();
    }

    private void GenerateOperationalDemoSeed()
    {
        _products.Clear();
        _rawMaterials.Clear();
        _materialPrices.Clear();
        _recipes.Clear();
        _stockMovements.Clear();
        _productRecipes.Clear();
        _bomItems.Clear();
        _productionBatches.Clear();
        _laborCosts.Clear();
        _overheadCosts.Clear();
        _purchaseOrders.Clear();
        _purchaseOrderLines.Clear();
        _manualLedgerEntries.Clear();
        _sales.Clear();
        _saleLines.Clear();

        var random = new Random(20260404);
        var now = DateTime.Now;
        var seedStart = now.Date.AddDays(-5).AddHours(6);
        var activeUser = _users.FirstOrDefault(item => item.Status == UserStatus.Active);
        var actorName = activeUser?.FullName ?? "System Demo";
        var actorEmail = activeUser?.Email;
        var actorId = activeUser?.Id;

        var materials = CreateDemoMaterials(seedStart);
        _rawMaterials.AddRange(materials);
        _materialPrices.AddRange(CreateDemoMaterialPrices(materials, seedStart, random));

        var openingMovements = materials
            .Select((material, index) =>
            {
                var quantity = material.BaseUnit switch
                {
                    "gr" => 6000m + (index % 5) * 2200m,
                    "ml" => 4500m + (index % 5) * 1800m,
                    _ => 36m + (index % 6) * 12m
                };

                return new StockMovementEntry(
                    Guid.NewGuid(),
                    material.Id,
                    StockMovementType.OpeningBalance,
                    quantity,
                    seedStart.AddMinutes(index * 9),
                    "Saldo awal operasional 6 hari");
            })
            .ToArray();
        _stockMovements.AddRange(openingMovements);

        var recipes = CreateDemoRecipes(materials, seedStart, random);
        _recipes.AddRange(recipes);

        var completedBatches = CreateCompletedProductionBatches(materials, recipes, seedStart, random);
        _productionBatches.AddRange(completedBatches.Batches);
        _stockMovements.AddRange(completedBatches.StockMovements);
        _laborCosts.AddRange(completedBatches.LaborCosts);
        _overheadCosts.AddRange(completedBatches.OverheadCosts);

        var liveBatches = CreateLiveProductionBatches(materials, recipes, now, random);
        _productionBatches.AddRange(liveBatches.Batches);
        _stockMovements.AddRange(liveBatches.StockMovements);

        var purchases = CreateDemoPurchaseOrders(materials, seedStart, now, random);
        _purchaseOrders.AddRange(purchases.Orders);
        _purchaseOrderLines.AddRange(purchases.Lines);
        _stockMovements.AddRange(purchases.StockMovements);

        ApplyWarehouseTuning(materials, now, random);

        var sales = CreateDemoSales(materials, recipes, completedBatches.CompletedPortionMap, seedStart, now, activeUser, random);
        _sales.AddRange(sales.Sales);
        _saleLines.AddRange(sales.Lines);

        _manualLedgerEntries.AddRange(CreateManualLedgerEntries(seedStart, now, random));
        _auditLogEntries.Insert(0, new AuditLogEntry(
            Guid.NewGuid(),
            "System",
            DemoSeedAction,
            actorName,
            actorEmail,
            actorId,
            "Demo Data",
            null,
            "Data operasional contoh lintas 6 hari dibuat otomatis untuk material, gudang, resep, produksi, belanja, POS, dan pembukuan.",
            now));

        _purchaseOrders.Sort((left, right) => right.OrderedAt.CompareTo(left.OrderedAt));
        _manualLedgerEntries.Sort((left, right) => right.OccurredAt.CompareTo(left.OccurredAt));
        _sales.Sort((left, right) => right.SoldAt.CompareTo(left.SoldAt));
        _auditLogEntries.Sort((left, right) => right.OccurredAt.CompareTo(left.OccurredAt));
        EnsureMaterialPriceSeed();
        Version++;
    }

    private static IReadOnlyList<RawMaterial> CreateDemoMaterials(DateTime seedStart)
    {
        var specs = new (string Name, string Brand, string BaseUnit, decimal NetQuantity, string NetUnit, decimal PricePerPack, decimal MinimumStock)[]
        {
            ("Tepung Terigu Protein Sedang", "Segitiga Biru", "gr", 1000m, "gr", 16000m, 250m),
            ("Butter Margarine", "Palmia Royal", "gr", 200m, "gr", 11000m, 120m),
            ("Gula Pasir", "Gulaku", "gr", 1000m, "gr", 18500m, 300m),
            ("Telur Ayam", "Layer Fresh", "pcs", 10m, "pcs", 29000m, 12m),
            ("Susu Cair UHT", "Ultra Milk", "ml", 1000m, "ml", 22000m, 350m),
            ("Susu Bubuk Full Cream", "Dancow", "gr", 400m, "gr", 48000m, 120m),
            ("Ragi Instan", "Fermipan", "gr", 100m, "gr", 12500m, 25m),
            ("Cokelat Bubuk", "Van Houten", "gr", 165m, "gr", 34000m, 50m),
            ("Keju Cheddar", "Kraft", "gr", 165m, "gr", 26000m, 60m),
            ("Cokelat Compound", "Colatta", "gr", 1000m, "gr", 52000m, 180m),
            ("Krim Kental Manis", "Frisian Flag", "gr", 370m, "gr", 13500m, 90m),
            ("Mesis Cokelat", "Ceres", "gr", 500m, "gr", 29500m, 90m),
            ("Kacang Almond Slice", "Blue Diamond", "gr", 250m, "gr", 52000m, 45m),
            ("Kacang Mede", "Golden Nut", "gr", 250m, "gr", 58000m, 45m),
            ("Pisang Cavendish", "Sunpride", "gr", 1000m, "gr", 24000m, 250m),
            ("Strawberry Frozen", "BerryBest", "gr", 500m, "gr", 48000m, 80m),
            ("Blueberry Filling", "Vizyon", "gr", 250m, "gr", 26000m, 55m),
            ("Matcha Powder", "Uji", "gr", 100m, "gr", 42000m, 20m),
            ("Kopi Arabika Grind", "Kopi Tuku", "gr", 250m, "gr", 44000m, 60m),
            ("Vanilla Essence", "McNally", "ml", 100m, "ml", 24000m, 20m),
            ("Minyak Goreng", "Bimoli", "ml", 1000m, "ml", 21000m, 250m),
            ("Whipping Cream", "Anchor", "ml", 1000m, "ml", 87000m, 200m),
            ("Cream Cheese", "Anchor", "gr", 1000m, "gr", 118000m, 150m),
            ("Sosis Sapi", "Bernardi", "gr", 500m, "gr", 41000m, 110m),
            ("Daging Asap", "Fiesta", "gr", 500m, "gr", 53000m, 110m),
            ("Ayam Fillet", "Fiesta", "gr", 1000m, "gr", 56000m, 220m),
            ("Mayonaise", "Maestro", "gr", 1000m, "gr", 26000m, 180m),
            ("Saus Tomat", "ABC", "gr", 550m, "gr", 14000m, 100m),
            ("Saus Sambal", "ABC", "gr", 550m, "gr", 14000m, 100m),
            ("Bawang Bombay", "Lokal", "gr", 1000m, "gr", 28000m, 180m),
            ("Bawang Putih Bubuk", "Koepoe Koepoe", "gr", 100m, "gr", 16000m, 20m),
            ("Cabai Bubuk", "Ladaku", "gr", 60m, "gr", 12500m, 15m),
            ("Saus Keju", "Master", "gr", 1000m, "gr", 47000m, 140m),
            ("Kentang Beku", "Fiesta", "gr", 1000m, "gr", 39000m, 180m),
            ("Saus BBQ", "Del Monte", "gr", 250m, "gr", 19000m, 45m),
            ("Nori Flakes", "Ajitsuke", "gr", 100m, "gr", 23000m, 20m),
            ("Beras Ketan", "Rose Brand", "gr", 1000m, "gr", 25000m, 250m),
            ("Santan Instan", "Kara", "ml", 200m, "ml", 9000m, 80m),
            ("Tepung Tapioka", "Gunung Agung", "gr", 500m, "gr", 8500m, 90m),
            ("Mie Kering", "La Fonte", "gr", 450m, "gr", 17000m, 90m),
            ("Sirup Gula Aren", "Palmia", "ml", 1000m, "ml", 47000m, 140m),
            ("Cokelat Chips", "Bake Time", "gr", 250m, "gr", 22000m, 40m),
            ("Cup Plastik 16 oz", "PackPro", "pcs", 50m, "pcs", 32000m, 20m),
            ("Lid Cup 16 oz", "PackPro", "pcs", 50m, "pcs", 18000m, 20m),
            ("Pouch Standing", "FlexyPack", "pcs", 100m, "pcs", 55000m, 35m),
            ("Sticker Label", "Printink", "pcs", 100m, "pcs", 25000m, 30m),
            ("Box Brownies", "PaperCo", "pcs", 25m, "pcs", 22000m, 8m),
            ("Tisu Makan", "Nice", "pcs", 100m, "pcs", 14000m, 25m),
            ("Sarung Tangan Plastik", "Safe Hand", "pcs", 100m, "pcs", 9000m, 20m),
            ("Gas Kaleng Portable", "Hi-Cook", "pcs", 4m, "pcs", 98000m, 1m)
        };

        return specs
            .Select((spec, index) => new RawMaterial(
                Guid.NewGuid(),
                $"BHN-{index + 1:000}",
                spec.Name,
                spec.Brand,
                spec.BaseUnit,
                spec.NetQuantity,
                spec.NetUnit,
                spec.PricePerPack,
                [],
                $"Stok operasional contoh untuk {spec.Name}.",
                MaterialStatus.Active,
                seedStart.AddMinutes(index * 11),
                seedStart.AddHours(8).AddMinutes(index * 7),
                spec.MinimumStock))
            .ToArray();
    }

    private static IReadOnlyList<MaterialPriceEntry> CreateDemoMaterialPrices(IReadOnlyList<RawMaterial> materials, DateTime seedStart, Random random)
    {
        var entries = new List<MaterialPriceEntry>(materials.Count * 2);
        foreach (var material in materials)
        {
            var previousPrice = decimal.Round(material.PricePerPack * (0.9m + ((decimal)random.Next(0, 8) / 100m)), 0);
            entries.Add(new MaterialPriceEntry(
                Guid.NewGuid(),
                material.Id,
                seedStart.AddHours(1),
                previousPrice,
                material.NetQuantity,
                material.NetUnit,
                material.BaseUnit,
                seedStart.AddHours(1),
                null,
                "Harga pembuka minggu operasional"));
            entries.Add(new MaterialPriceEntry(
                Guid.NewGuid(),
                material.Id,
                material.UpdatedAt,
                material.PricePerPack,
                material.NetQuantity,
                material.NetUnit,
                material.BaseUnit,
                material.UpdatedAt,
                previousPrice,
                "Penyesuaian harga selama 6 hari operasional"));
        }

        return entries;
    }

    private static IReadOnlyList<RecipeBook> CreateDemoRecipes(IReadOnlyList<RawMaterial> materials, DateTime seedStart, Random random)
    {
        var bases = new[]
        {
            "Roti Sobek", "Brownies", "Donat", "Cookies", "Pudding", "Muffin", "Croissant", "Sandwich", "Rice Bowl", "Mie Box"
        };
        var variants = new[]
        {
            "Cokelat", "Keju", "Matcha", "Kopi", "Red Velvet"
        };
        var portionUnits = new[] { "pcs", "box", "pouch", "cup", "tray" };
        var recipes = new List<RecipeBook>(50);

        for (var index = 0; index < 50; index++)
        {
            var baseName = bases[index / variants.Length];
            var variant = variants[index % variants.Length];
            var name = $"{baseName} {variant}";
            var portionUnit = portionUnits[index % portionUnits.Length];
            var portionYield = 12m + (index % 5) * 4m;
            var margin = 28m + (index % 6) * 4m;
            var materialOffset = index % 35;

            var primary = materials[materialOffset];
            var secondary = materials[(materialOffset + 3) % 35];
            var tertiary = materials[(materialOffset + 7) % 35];
            var quaternary = materials[(materialOffset + 11) % 35];

            var groups = new[]
            {
                new RecipeMaterialGroup(
                    Guid.NewGuid(),
                    index % 2 == 0 ? "Base" : "Adonan",
                    "Kelompok bahan utama.",
                    new[]
                    {
                        CreateRecipeLine(primary, 80m + (index % 5) * 25m),
                        CreateRecipeLine(secondary, secondary.BaseUnit == "pcs" ? 2m + (index % 3) : 45m + (index % 4) * 20m)
                    }),
                new RecipeMaterialGroup(
                    Guid.NewGuid(),
                    "Finishing",
                    "Pelengkap dan topping akhir.",
                    new[]
                    {
                        CreateRecipeLine(tertiary, tertiary.BaseUnit == "pcs" ? 1m + (index % 2) : 30m + (index % 3) * 15m),
                        CreateRecipeLine(quaternary, quaternary.BaseUnit == "pcs" ? 1m : 20m + (index % 3) * 10m)
                    })
            };

            var materialCost = groups
                .SelectMany(group => group.Materials)
                .Sum(line =>
                {
                    var material = materials.First(item => item.Id == line.MaterialId);
                    return RecipeCatalogMath.CalculateLineCost(line, material);
                });
            var costs = new[]
            {
                new RecipeCostComponent(Guid.NewGuid(), RecipeCostType.Overhead, "Gas & utilitas", 6000m + (index % 4) * 1000m, "Biaya energi per batch."),
                new RecipeCostComponent(Guid.NewGuid(), RecipeCostType.Production, "Tenaga bantu", 5000m + (index % 3) * 1500m, "Biaya operator per batch.")
            };
            var totalCost = materialCost + costs.Sum(item => item.Amount);
            var suggestedPrice = decimal.Round((totalCost / portionYield) * (1m + margin / 100m), 0);

            recipes.Add(new RecipeBook(
                Guid.NewGuid(),
                $"RCP-{index + 1:000}",
                name,
                $"Resep contoh {name} untuk simulasi operasi 6 hari.",
                portionYield,
                portionUnit,
                RecipeStatus.Active,
                groups,
                costs,
                seedStart.AddHours(10).AddMinutes(index * 13),
                seedStart.AddDays(index % 6).AddHours(11).AddMinutes(index * 5 % 60),
                portionYield,
                portionUnit,
                margin,
                suggestedPrice));
        }

        return recipes;
    }

    private static RecipeMaterialLine CreateRecipeLine(RawMaterial material, decimal quantity)
        => new(
            Guid.NewGuid(),
            material.Id,
            quantity,
            material.BaseUnit,
            material.BaseUnit == "pcs" ? 0m : 2m,
            "Komponen resep contoh.");

    private static (IReadOnlyList<ProductionBatch> Batches, IReadOnlyList<StockMovementEntry> StockMovements, IReadOnlyList<LaborCostEntry> LaborCosts, IReadOnlyList<OverheadCostEntry> OverheadCosts, IReadOnlyDictionary<Guid, decimal> OnHandMap, IReadOnlyDictionary<Guid, decimal> CompletedPortionMap) CreateCompletedProductionBatches(
        IReadOnlyList<RawMaterial> materials,
        IReadOnlyList<RecipeBook> recipes,
        DateTime seedStart,
        Random random)
    {
        var materialMap = materials.ToDictionary(item => item.Id);
        var portionMap = recipes.ToDictionary(item => item.Id, _ => 0m);
        var batches = new List<ProductionBatch>(recipes.Count);
        var stockMovements = new List<StockMovementEntry>(recipes.Count * 4);
        var laborCosts = new List<LaborCostEntry>(recipes.Count);
        var overheadCosts = new List<OverheadCostEntry>(recipes.Count);

        for (var index = 0; index < recipes.Count; index++)
        {
            var recipe = recipes[index];
            var batchCount = 2 + (index % 3);
            var queuedAt = seedStart.AddDays(index % 6).AddHours(6 + (index % 3)).AddMinutes((index * 9) % 60);
            var startedAt = queuedAt.AddMinutes(15 + (index % 5) * 5);
            var targetDuration = 35 + (index % 5) * 10;
            var completedAt = startedAt.AddMinutes(targetDuration);
            var batchId = Guid.NewGuid();
            var costSummary = BuildRecipeRequirementSummary(recipe, materialMap, batchCount);
            var laborCost = decimal.Round(recipe.Costs.Where(item => item.Type == RecipeCostType.Production).Sum(item => item.Amount), 2);
            var overheadCost = decimal.Round(recipe.Costs.Where(item => item.Type == RecipeCostType.Overhead).Sum(item => item.Amount), 2);

            batches.Add(new ProductionBatch(
                batchId,
                Guid.Empty,
                $"PRD-{index + 1:000}",
                startedAt,
                batchCount,
                queuedAt,
                recipe.Id,
                $"Produksi reguler shift {(index % 2 == 0 ? "pagi" : "sore")}.",
                decimal.Round(costSummary.MaterialCost, 2),
                laborCost,
                overheadCost,
                batchCount,
                recipe.PortionYield,
                targetDuration,
                ProductionRunStatus.Completed,
                completedAt));

            stockMovements.AddRange(costSummary.Requirements.Select(requirement => new StockMovementEntry(
                Guid.NewGuid(),
                requirement.MaterialId,
                StockMovementType.ProductionUsage,
                -requirement.RequiredQuantity,
                startedAt,
                $"Pemakaian bahan untuk {recipe.Name} ({batchCount} batch)",
                batchId)));

            laborCosts.Add(new LaborCostEntry(
                Guid.NewGuid(),
                batchId,
                laborCost,
                $"Tenaga bantu {recipe.Name}",
                completedAt));

            overheadCosts.Add(new OverheadCostEntry(
                Guid.NewGuid(),
                batchId,
                overheadCost,
                $"Utilitas produksi {recipe.Name}",
                completedAt));

            portionMap[recipe.Id] += recipe.PortionYield * batchCount;
        }

        return (batches, stockMovements, laborCosts, overheadCosts, portionMap, portionMap);
    }

    private static (IReadOnlyList<ProductionBatch> Batches, IReadOnlyList<StockMovementEntry> StockMovements) CreateLiveProductionBatches(
        IReadOnlyList<RawMaterial> materials,
        IReadOnlyList<RecipeBook> recipes,
        DateTime now,
        Random random)
    {
        var materialMap = materials.ToDictionary(item => item.Id);
        var batches = new List<ProductionBatch>();
        var stockMovements = new List<StockMovementEntry>();
        var queueCount = 6;
        var runningCount = 4;

        for (var index = 0; index < queueCount; index++)
        {
            var recipe = recipes[(index * 3 + 1) % recipes.Count];
            var batchCount = 1 + (index % 2);
            var queuedAt = now.AddHours(-(6 - index)).AddMinutes(-(index * 7));
            var targetDuration = 25 + (index % 4) * 10;

            batches.Add(new ProductionBatch(
                Guid.NewGuid(),
                Guid.Empty,
                $"PRD-{recipes.Count + index + 1:000}",
                queuedAt,
                batchCount,
                queuedAt,
                recipe.Id,
                "Menunggu slot oven / finishing.",
                decimal.Round(BuildRecipeRequirementSummary(recipe, materialMap, batchCount).MaterialCost, 2),
                0m,
                0m,
                batchCount,
                recipe.PortionYield,
                targetDuration,
                ProductionRunStatus.Queued,
                null));
        }

        for (var index = 0; index < runningCount; index++)
        {
            var recipe = recipes[(index * 4 + 2) % recipes.Count];
            var batchCount = 1 + (index % 3);
            var queuedAt = now.AddHours(-(3 + index)).AddMinutes(-(index * 11));
            var startedAt = queuedAt.AddMinutes(20 + index * 5);
            var targetDuration = 40 + index * 10;
            var batchId = Guid.NewGuid();
            var costSummary = BuildRecipeRequirementSummary(recipe, materialMap, batchCount);

            batches.Add(new ProductionBatch(
                batchId,
                Guid.Empty,
                $"PRD-{recipes.Count + queueCount + index + 1:000}",
                startedAt,
                batchCount,
                queuedAt,
                recipe.Id,
                "Sedang diproses oleh tim produksi.",
                decimal.Round(costSummary.MaterialCost, 2),
                0m,
                0m,
                batchCount,
                recipe.PortionYield,
                targetDuration,
                ProductionRunStatus.Running,
                null));

            stockMovements.AddRange(costSummary.Requirements.Select(requirement => new StockMovementEntry(
                Guid.NewGuid(),
                requirement.MaterialId,
                StockMovementType.ProductionUsage,
                -requirement.RequiredQuantity,
                startedAt,
                $"Pemakaian bahan untuk produksi berjalan {recipe.Name}",
                batchId)));
        }

        return (batches, stockMovements);
    }

    private static (IReadOnlyList<PurchaseOrder> Orders, IReadOnlyList<PurchaseOrderLine> Lines, IReadOnlyList<StockMovementEntry> StockMovements) CreateDemoPurchaseOrders(
        IReadOnlyList<RawMaterial> materials,
        DateTime seedStart,
        DateTime now,
        Random random)
    {
        var suppliers = new[]
        {
            "Toko Sinar Jaya",
            "CV Bahan Prima",
            "UD Maju Bersama",
            "Central Baking Mart",
            "Grosir Pantry Nusantara",
            "Toko Pendingin Sentosa"
        };
        var platforms = new[] { "Shopee", "Tokopedia", "TikTok", "WhatsApp" };
        var orders = new List<PurchaseOrder>(50);
        var lines = new List<PurchaseOrderLine>(120);
        var stockMovements = new List<StockMovementEntry>(120);

        for (var index = 0; index < 50; index++)
        {
            var orderId = Guid.NewGuid();
            var orderedAt = seedStart.AddDays(index % 6).AddHours(8 + (index % 8)).AddMinutes((index * 11) % 60);
            var lineCount = 1 + (index % 3);
            var selectedMaterials = Enumerable.Range(0, lineCount)
                .Select(offset => materials[(index * 2 + offset * 7) % materials.Count])
                .DistinctBy(item => item.Id)
                .ToArray();

            var orderLines = new List<PurchaseOrderLine>(selectedMaterials.Length);
            foreach (var material in selectedMaterials)
            {
                var packCount = 1 + ((index + orderLines.Count) % 4);
                orderLines.Add(new PurchaseOrderLine(
                    Guid.NewGuid(),
                    orderId,
                    material.Id,
                    material.Code,
                    material.Name,
                    material.Brand,
                    material.BaseUnit,
                    material.NetQuantity,
                    material.NetUnit,
                    material.NetQuantityInBaseUnit,
                    packCount,
                    material.PricePerPack,
                    decimal.Round(material.PricePerPack * packCount, 2)));
            }

            var channel = index % 4 == 0 ? PurchaseChannel.Online : PurchaseChannel.Offline;
            var shouldReceive = channel == PurchaseChannel.Offline || index % 5 != 0;
            var status = shouldReceive ? PurchaseOrderStatus.Received : PurchaseOrderStatus.Ordered;
            var receivedAt = status == PurchaseOrderStatus.Received
                ? orderedAt.AddHours(channel == PurchaseChannel.Online ? 18 + (index % 10) : 3 + (index % 6))
                : (DateTime?)null;
            if (receivedAt > now)
            {
                receivedAt = now.AddMinutes(-(index % 30));
                status = PurchaseOrderStatus.Received;
            }

            var subtotal = orderLines.Sum(item => item.LineSubtotal);
            var shippingCost = channel == PurchaseChannel.Online ? (12000m + (index % 6) * 3500m) : (index % 7 == 0 ? 5000m : 0m);
            var order = new PurchaseOrder(
                orderId,
                $"BLJ-{index + 1:000000}",
                orderedAt,
                suppliers[index % suppliers.Length],
                channel,
                channel == PurchaseChannel.Online ? platforms[index % platforms.Length] : null,
                orderLines.Count,
                orderLines.Sum(item => item.PackCount),
                subtotal,
                shippingCost,
                subtotal + shippingCost,
                status,
                channel == PurchaseChannel.Online ? "Belanja restock marketplace." : "Belanja supplier rutin.",
                receivedAt,
                index % 3 == 0 ? $"struk-{index + 1:000}.jpg" : null,
                index % 3 == 0 ? "image/jpeg" : null,
                index % 3 == 0 ? "demo-receipt" : null,
                index % 3 == 0 ? (receivedAt ?? orderedAt) : null);

            orders.Add(order);
            lines.AddRange(orderLines);

            if (status == PurchaseOrderStatus.Received)
            {
                stockMovements.AddRange(orderLines.Select(line => new StockMovementEntry(
                    Guid.NewGuid(),
                    line.MaterialId,
                    StockMovementType.StockIn,
                    decimal.Round(line.BaseQuantityPerPack * line.PackCount, 4),
                    receivedAt ?? orderedAt,
                    $"Belanja {order.PurchaseNumber} dari {order.SupplierName}",
                    orderId)));
            }
        }

        return (orders, lines, stockMovements);
    }

    private void ApplyWarehouseTuning(IReadOnlyList<RawMaterial> materials, DateTime now, Random random)
    {
        var onHandMap = _stockMovements
            .GroupBy(item => item.MaterialId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));

        foreach (var material in materials.Take(8))
        {
            var current = onHandMap.GetValueOrDefault(material.Id);
            if (current <= 0)
            {
                continue;
            }

            var target = material.MinimumStock <= 0
                ? 0m
                : material.MinimumStock * (material.BaseUnit == "pcs" ? 0.5m : 0.35m);
            var adjustment = decimal.Round(current - target, 4);
            if (adjustment <= 0)
            {
                continue;
            }

            _stockMovements.Add(new StockMovementEntry(
                Guid.NewGuid(),
                material.Id,
                StockMovementType.Adjustment,
                -adjustment,
                now.AddMinutes(-45 - random.Next(120)),
                $"Penyesuaian stok opname untuk {material.Name}"));
        }
    }

    private static (IReadOnlyList<SaleTransaction> Sales, IReadOnlyList<SaleLine> Lines) CreateDemoSales(
        IReadOnlyList<RawMaterial> materials,
        IReadOnlyList<RecipeBook> recipes,
        IReadOnlyDictionary<Guid, decimal> completedPortionMap,
        DateTime seedStart,
        DateTime now,
        BusinessUser? activeUser,
        Random random)
    {
        var materialMap = materials.ToDictionary(item => item.Id);
        var availablePortions = completedPortionMap.ToDictionary(item => item.Key, item => item.Value);
        var customerNames = new[]
        {
            "Alya Catering",
            "Budi Kantor",
            "Citra Event",
            "Dina Retail",
            "Eko Komunitas",
            "Farah Preorder",
            "Gita Office",
            "Hana Titip Jual",
            "Ivan Repeat Order",
            "Jasmine Reseller"
        };
        var sales = new List<SaleTransaction>(50);
        var lines = new List<SaleLine>(70);
        var cashierName = activeUser?.FullName ?? "Kasir Demo";
        var cashierUserId = activeUser?.Id;

        for (var index = 0; index < 50; index++)
        {
            var recipe = recipes[index % recipes.Count];
            var soldAt = seedStart.AddDays(index % 6).AddHours(10 + (index % 9)).AddMinutes((index * 13) % 60);
            if (soldAt > now)
            {
                soldAt = now.AddMinutes(-(index + 5));
            }

            var primaryQuantity = Math.Max(1, Math.Min(4, (int)Math.Floor(availablePortions.GetValueOrDefault(recipe.Id) / 6m)));
            primaryQuantity = Math.Max(1, Math.Min(primaryQuantity, 2 + (index % 3)));

            var orderLines = new List<SaleLine>();
            AddSaleLine(orderLines, recipe, primaryQuantity, materialMap, random);
            availablePortions[recipe.Id] = Math.Max(0m, availablePortions.GetValueOrDefault(recipe.Id) - primaryQuantity);

            if (index % 5 == 0)
            {
                var secondary = recipes[(index + 11) % recipes.Count];
                var available = availablePortions.GetValueOrDefault(secondary.Id);
                if (available >= 1)
                {
                    AddSaleLine(orderLines, secondary, 1, materialMap, random);
                    availablePortions[secondary.Id] = Math.Max(0m, available - 1);
                }
            }

            var paymentMethod = index % 4 == 0 ? "Transfer Bank" : "Cash";
            var isPaid = paymentMethod == "Cash" || index % 8 != 0;
            var totalRevenue = decimal.Round(orderLines.Sum(item => item.UnitPrice * item.Quantity), 2);
            var totalHpp = decimal.Round(orderLines.Sum(item => item.HppPerUnit * item.Quantity), 2);
            var saleId = Guid.NewGuid();
            var receiptNumber = $"TRX-{index + 1:000000}";

            sales.Add(new SaleTransaction(
                saleId,
                receiptNumber,
                soldAt,
                cashierUserId,
                cashierName,
                paymentMethod,
                orderLines.Count,
                orderLines.Sum(item => item.Quantity),
                totalRevenue,
                totalHpp,
                totalRevenue - totalHpp,
                isPaid ? totalRevenue : 0m,
                0m,
                SaleStatus.Completed,
                index % 6 == 0 ? "Pesanan operasional demo." : null,
                customerNames[index % customerNames.Length],
                isPaid,
                isPaid ? soldAt : null));

            lines.AddRange(orderLines.Select(item => item with { SaleId = saleId }));
        }

        return (sales, lines);
    }

    private static IReadOnlyList<ManualLedgerEntry> CreateManualLedgerEntries(DateTime seedStart, DateTime now, Random random)
    {
        var templates = new[]
        {
            new { Title = "Isi ulang gas produksi", Direction = LedgerEntryDirection.Expense, Counterparty = "Toko Gas Mapan", Min = 85000m, Max = 130000m, Notes = "Belanja gas dapur harian." },
            new { Title = "Token listrik workshop", Direction = LedgerEntryDirection.Expense, Counterparty = "PLN Mobile", Min = 100000m, Max = 250000m, Notes = "Top up listrik area produksi." },
            new { Title = "Parkir & bongkar muat", Direction = LedgerEntryDirection.Expense, Counterparty = "Petugas pasar", Min = 5000m, Max = 25000m, Notes = "Biaya kecil operasional." },
            new { Title = "Pendapatan pesanan katering", Direction = LedgerEntryDirection.Income, Counterparty = "Corporate Client", Min = 250000m, Max = 750000m, Notes = "Pemasukan tambahan luar POS." },
            new { Title = "Fee titip jual", Direction = LedgerEntryDirection.Expense, Counterparty = "Mitra Titip Jual", Min = 30000m, Max = 95000m, Notes = "Komisi untuk titik jual." },
            new { Title = "Pendapatan kelas baking", Direction = LedgerEntryDirection.Income, Counterparty = "Peserta Workshop", Min = 350000m, Max = 950000m, Notes = "Pendapatan event luar produksi utama." }
        };

        return Enumerable.Range(0, 18)
            .Select(index =>
            {
                var template = templates[index % templates.Length];
                var occurredAt = seedStart.AddDays(index % 6).AddHours(7 + (index % 10)).AddMinutes((index * 17) % 60);
                if (occurredAt > now)
                {
                    occurredAt = now.AddMinutes(-(index + 10));
                }

                var amount = template.Min + random.Next((int)((template.Max - template.Min) / 5000m) + 1) * 5000m;
                return new ManualLedgerEntry(
                    Guid.NewGuid(),
                    occurredAt,
                    template.Title,
                    template.Direction,
                    amount,
                    template.Counterparty,
                    template.Notes);
            })
            .OrderByDescending(item => item.OccurredAt)
            .ToArray();
    }

    private static RecipeRequirementSummary BuildRecipeRequirementSummary(
        RecipeBook recipe,
        IReadOnlyDictionary<Guid, RawMaterial> materialMap,
        int batchCount)
    {
        var aggregated = new Dictionary<Guid, decimal>();

        foreach (var group in recipe.Groups)
        {
            foreach (var line in group.Materials)
            {
                if (!materialMap.TryGetValue(line.MaterialId, out var material))
                {
                    continue;
                }

                var baseQuantity = RecipeCatalogMath.ConvertToBaseQuantity(line.Quantity, line.Unit, material);
                if (baseQuantity <= 0)
                {
                    continue;
                }

                var requiredPerBatch = baseQuantity * (1m + Math.Max(0m, line.WastePercent) / 100m);
                aggregated[line.MaterialId] = aggregated.TryGetValue(line.MaterialId, out var existing)
                    ? existing + requiredPerBatch
                    : requiredPerBatch;
            }
        }

        var requirements = aggregated
            .Select(item =>
            {
                var material = materialMap[item.Key];
                var requiredQuantity = decimal.Round(item.Value * Math.Max(1, batchCount), 4);
                return new SeedMaterialRequirement(
                    item.Key,
                    requiredQuantity,
                    decimal.Round(requiredQuantity * material.CostPerBaseUnit, 2));
            })
            .ToArray();

        return new RecipeRequirementSummary(
            requirements,
            requirements.Sum(item => item.EstimatedCost));
    }

    private static void AddSaleLine(
        ICollection<SaleLine> lines,
        RecipeBook recipe,
        int quantity,
        IReadOnlyDictionary<Guid, RawMaterial> materialMap,
        Random random)
    {
        var financials = InventoryMath.CalculateRecipeFinancials(recipe, materialMap.Values.ToArray());
        var hppPerUnit = decimal.Round(financials.HppPerPortion, 2);
        var unitPrice = recipe.SuggestedSellingPrice > 0
            ? recipe.SuggestedSellingPrice
            : decimal.Round(hppPerUnit * (1.35m + (random.Next(0, 3) * 0.05m)), 0);

        lines.Add(new SaleLine(
            Guid.NewGuid(),
            Guid.Empty,
            recipe.Id,
            recipe.Id,
            recipe.Code,
            recipe.Name,
            recipe.PortionUnit,
            quantity,
            unitPrice,
            hppPerUnit));
    }

    private sealed record SeedMaterialRequirement(Guid MaterialId, decimal RequiredQuantity, decimal EstimatedCost);

    private sealed record RecipeRequirementSummary(IReadOnlyList<SeedMaterialRequirement> Requirements, decimal MaterialCost);

    private void EnsureDatabase()
    {
        using var connection = CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = _provider switch
        {
            BusinessDataStoreProvider.Postgres => $"""
                CREATE TABLE IF NOT EXISTS {TableName} (
                    StateKey TEXT PRIMARY KEY,
                    Json TEXT NOT NULL,
                    UpdatedAt TIMESTAMPTZ NOT NULL
                );
                """,
            _ => $"""
                CREATE TABLE IF NOT EXISTS {TableName} (
                    StateKey TEXT PRIMARY KEY,
                    Json TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """
        };
        command.ExecuteNonQuery();
    }

    private bool TryLoadFromDatabase()
    {
        using var connection = CreateConnection();
        connection.Open();

        var state = LoadStateDictionary(connection);

        if (state.Count == 0)
        {
            return false;
        }

        _products.Clear();
        _rawMaterials.Clear();
        _materialPrices.Clear();
        _recipes.Clear();
        _stockMovements.Clear();
        _productRecipes.Clear();
        _bomItems.Clear();
        _productionBatches.Clear();
        _laborCosts.Clear();
        _overheadCosts.Clear();
        _purchaseOrders.Clear();
        _purchaseOrderLines.Clear();
        _manualLedgerEntries.Clear();
        _sales.Clear();
        _saleLines.Clear();
        _users.Clear();
        _auditLogEntries.Clear();

        _products.AddRange(Deserialize<List<Product>>(state, "products") ?? []);
        _rawMaterials.AddRange(LoadRawMaterials(state));
        _materialPrices.AddRange(LoadMaterialPrices(state, _rawMaterials));
        _recipes.AddRange(LoadRecipes(state));
        _stockMovements.AddRange(Deserialize<List<StockMovementEntry>>(state, "stockMovements") ?? []);
        _productRecipes.AddRange(Deserialize<List<ProductRecipe>>(state, "productRecipes") ?? []);
        _bomItems.AddRange(Deserialize<List<BomItem>>(state, "bomItems") ?? []);
        _productionBatches.AddRange(Deserialize<List<ProductionBatch>>(state, "productionBatches") ?? []);
        _laborCosts.AddRange(Deserialize<List<LaborCostEntry>>(state, "laborCosts") ?? []);
        _overheadCosts.AddRange(Deserialize<List<OverheadCostEntry>>(state, "overheadCosts") ?? []);
        _purchaseOrders.AddRange((Deserialize<List<PurchaseOrder>>(state, "purchaseOrders") ?? [])
            .OrderByDescending(item => item.OrderedAt));
        _purchaseOrderLines.AddRange(Deserialize<List<PurchaseOrderLine>>(state, "purchaseOrderLines") ?? []);
        _manualLedgerEntries.AddRange((Deserialize<List<ManualLedgerEntry>>(state, "manualLedgerEntries") ?? [])
            .OrderByDescending(item => item.OccurredAt));
        _sales.AddRange((Deserialize<List<SaleTransaction>>(state, "sales") ?? [])
            .OrderByDescending(item => item.SoldAt));
        _saleLines.AddRange(Deserialize<List<SaleLine>>(state, "saleLines") ?? []);
        _users.AddRange(Deserialize<List<BusinessUser>>(state, "users") ?? []);
        _auditLogEntries.AddRange((Deserialize<List<AuditLogEntry>>(state, "auditLogEntries") ?? [])
            .OrderByDescending(item => item.OccurredAt));
        _authSession = Deserialize<AuthSession?>(state, "authSession");
        _businessSettings = Deserialize<BusinessSettings>(state, "businessSettings") ?? _businessSettings;

        EnsureMaterialPriceSeed();
        return true;
    }

    private void PersistStateUnsafe()
    {
        using var connection = CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        UpsertState(connection, transaction, "products", _products);
        UpsertState(connection, transaction, "rawMaterials", _rawMaterials);
        UpsertState(connection, transaction, "materialPrices", _materialPrices);
        UpsertState(connection, transaction, "recipes", _recipes);
        UpsertState(connection, transaction, "stockMovements", _stockMovements);
        UpsertState(connection, transaction, "productRecipes", _productRecipes);
        UpsertState(connection, transaction, "bomItems", _bomItems);
        UpsertState(connection, transaction, "productionBatches", _productionBatches);
        UpsertState(connection, transaction, "laborCosts", _laborCosts);
        UpsertState(connection, transaction, "overheadCosts", _overheadCosts);
        UpsertState(connection, transaction, "purchaseOrders", _purchaseOrders);
        UpsertState(connection, transaction, "purchaseOrderLines", _purchaseOrderLines);
        UpsertState(connection, transaction, "manualLedgerEntries", _manualLedgerEntries);
        UpsertState(connection, transaction, "sales", _sales);
        UpsertState(connection, transaction, "saleLines", _saleLines);
        UpsertState(connection, transaction, "users", _users);
        UpsertState(connection, transaction, "auditLogEntries", _auditLogEntries);
        UpsertState(connection, transaction, "authSession", _authSession);
        UpsertState(connection, transaction, "businessSettings", _businessSettings);

        transaction.Commit();
    }

    private void UpsertState(DbConnection connection, DbTransaction transaction, string key, object? value)
        => UpsertStateJson(connection, transaction, key, JsonSerializer.Serialize(value, _jsonOptions));

    private void UpsertStateJson(DbConnection connection, DbTransaction transaction, string key, string json)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {TableName} (StateKey, Json, UpdatedAt)
            VALUES (@key, @json, @updatedAt)
            ON CONFLICT(StateKey) DO UPDATE SET
                Json = EXCLUDED.Json,
                UpdatedAt = EXCLUDED.UpdatedAt;
            """;
        AddParameter(command, "@key", key);
        AddParameter(command, "@json", json);
        AddParameter(
            command,
            "@updatedAt",
            _provider == BusinessDataStoreProvider.Postgres
                ? DateTime.UtcNow
                : DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private T? Deserialize<T>(IReadOnlyDictionary<string, string> state, string key)
    {
        if (!state.TryGetValue(key, out var json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private IReadOnlyList<RawMaterial> LoadRawMaterials(IReadOnlyDictionary<string, string> state)
    {
        var current = Deserialize<List<RawMaterial>>(state, "rawMaterials");
        if (current is { Count: > 0 })
        {
            return current.Select(SanitizeMaterial).ToArray();
        }

        var legacy = Deserialize<List<LegacyRawMaterial>>(state, "rawMaterials") ?? [];
        return legacy.Select(item =>
        {
            var normalizedUnit = MaterialUnitCatalog.NormalizeUnit(item.Unit);
            var baseUnit = MaterialUnitCatalog.GetCompatibleUnits(normalizedUnit).Any()
                ? normalizedUnit
                : "pcs";

            return SanitizeMaterial(new RawMaterial(
                item.Id,
                item.Code,
                item.Name,
                item.SupplierName,
                baseUnit,
                1m,
                baseUnit,
                item.PurchasePrice,
                [],
                item.Description,
                item.Status,
                item.CreatedAt,
                item.UpdatedAt,
                item.MinimumStock));
        }).ToArray();
    }

    private IReadOnlyList<MaterialPriceEntry> LoadMaterialPrices(IReadOnlyDictionary<string, string> state, IReadOnlyList<RawMaterial> materials)
    {
        var current = Deserialize<List<MaterialPriceEntry>>(state, "materialPrices");
        if (current is { Count: > 0 })
        {
            return current;
        }

        var materialMap = materials.ToDictionary(item => item.Id);
        var legacy = Deserialize<List<LegacyMaterialPriceEntry>>(state, "materialPrices") ?? [];
        return legacy
            .Select(item =>
            {
                if (!materialMap.TryGetValue(item.MaterialId, out var material))
                {
                    return null;
                }

                return new MaterialPriceEntry(
                    item.Id,
                    item.MaterialId,
                    item.EffectiveAt,
                    item.PricePerUnit * material.NetQuantityInBaseUnit,
                    material.NetQuantity,
                    material.NetUnit,
                    material.BaseUnit,
                    item.UpdatedAt,
                    item.PreviousPrice is null ? null : item.PreviousPrice.Value * material.NetQuantityInBaseUnit,
                    item.Note);
            })
            .OfType<MaterialPriceEntry>()
            .ToArray();
    }

    private void EnsureMaterialPriceSeed()
    {
        foreach (var material in _rawMaterials.Where(material => _materialPrices.All(item => item.MaterialId != material.Id)))
        {
            _materialPrices.Add(new MaterialPriceEntry(
                Guid.NewGuid(),
                material.Id,
                material.CreatedAt,
                material.PricePerPack,
                material.NetQuantity,
                material.NetUnit,
                material.BaseUnit,
                material.UpdatedAt,
                null,
                "Harga awal material"));
        }
    }

    private static RawMaterial SanitizeMaterial(RawMaterial material)
        => material with
        {
            Code = material.Code ?? string.Empty,
            Name = material.Name ?? string.Empty,
            Brand = string.IsNullOrWhiteSpace(material.Brand) ? null : material.Brand.Trim(),
            BaseUnit = string.IsNullOrWhiteSpace(material.BaseUnit) ? "pcs" : MaterialUnitCatalog.NormalizeUnit(material.BaseUnit),
            NetUnit = string.IsNullOrWhiteSpace(material.NetUnit)
                ? (string.IsNullOrWhiteSpace(material.BaseUnit) ? "pcs" : MaterialUnitCatalog.NormalizeUnit(material.BaseUnit))
                : MaterialUnitCatalog.NormalizeUnit(material.NetUnit),
            UnitConversions = material.UnitConversions?
                .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.UnitName) && item.ConversionQuantity > 0)
                .Select(item => new MaterialUnitConversion(item.UnitName.Trim(), item.ConversionQuantity))
                .ToArray() ?? [],
            Description = string.IsNullOrWhiteSpace(material.Description) ? null : material.Description.Trim(),
            MinimumStock = Math.Max(0m, material.MinimumStock)
        };

    private DbConnection CreateConnection()
        => _provider switch
        {
            BusinessDataStoreProvider.Postgres => new NpgsqlConnection(_connectionString),
            _ => new SqliteConnection(_connectionString)
        };

    private Dictionary<string, string> LoadStateDictionary(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT StateKey, Json FROM {TableName};";

        using var reader = command.ExecuteReader();
        var state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            state[reader.GetString(0)] = reader.GetString(1);
        }

        return state;
    }

    private bool TryImportLegacySqliteState()
    {
        if (_provider != BusinessDataStoreProvider.Postgres ||
            string.IsNullOrWhiteSpace(_localSqlitePath) ||
            !File.Exists(_localSqlitePath))
        {
            return false;
        }

        var legacyState = LoadLegacySqliteState(_localSqlitePath);
        if (legacyState.Count == 0)
        {
            return false;
        }

        using var connection = CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var entry in legacyState)
        {
            UpsertStateJson(connection, transaction, entry.Key, entry.Value);
        }

        transaction.Commit();
        return true;
    }

    private static Dictionary<string, string> LoadLegacySqliteState(string sqlitePath)
    {
        using var connection = new SqliteConnection($"Data Source={sqlitePath}");
        connection.Open();

        using (var existsCommand = connection.CreateCommand())
        {
            existsCommand.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = @tableName;";
            existsCommand.Parameters.AddWithValue("@tableName", TableName);
            var exists = existsCommand.ExecuteScalar();
            if (exists is null)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT StateKey, Json FROM {TableName};";
        using var reader = command.ExecuteReader();
        var state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            state[reader.GetString(0)] = reader.GetString(1);
        }

        return state;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private void InitializeEmptyState()
    {
        _products.Clear();
        _rawMaterials.Clear();
        _materialPrices.Clear();
        _recipes.Clear();
        _stockMovements.Clear();
        _productRecipes.Clear();
        _bomItems.Clear();
        _productionBatches.Clear();
        _laborCosts.Clear();
        _overheadCosts.Clear();
        _purchaseOrders.Clear();
        _purchaseOrderLines.Clear();
        _manualLedgerEntries.Clear();
        _sales.Clear();
        _saleLines.Clear();
        _users.Clear();
        _auditLogEntries.Clear();
        _authSession = null;
        _businessSettings = _businessSettings with
        {
            UpdatedAt = DateTime.Now
        };
    }

    public void Touch()
    {
        using var scope = _gate.EnterScope();
        TouchUnsafe();
    }

    private void TouchUnsafe()
    {
        PersistStateUnsafe();
        Version++;
        var handler = Changed;
        if (handler is not null)
        {
            ThreadPool.QueueUserWorkItem(static state => ((Action)state!).Invoke(), handler);
        }
    }

    public string GenerateNextProductCode()
    {
        using var scope = _gate.EnterScope();
        var nextNumber = _products.Select(product => product.Code)
            .Where(code => code.StartsWith("PRD-", StringComparison.OrdinalIgnoreCase))
            .Select(code => code[4..])
            .Select(part => int.TryParse(part, out var number) ? number : 0)
            .DefaultIfEmpty()
            .Max() + 1;

        return $"PRD-{nextNumber:000}";
    }

    public string GenerateNextMaterialCode()
    {
        using var scope = _gate.EnterScope();
        var nextNumber = _rawMaterials.Select(material => material.Code)
            .Where(code => code.StartsWith("BHN-", StringComparison.OrdinalIgnoreCase))
            .Select(code => code[4..])
            .Select(part => int.TryParse(part, out var number) ? number : 0)
            .DefaultIfEmpty()
            .Max() + 1;

        return $"BHN-{nextNumber:000}";
    }

    private IReadOnlyList<RecipeBook> LoadRecipes(IReadOnlyDictionary<string, string> state)
    {
        var current = Deserialize<List<RecipeBook>>(state, "recipes");
        if (current is { Count: > 0 })
        {
            return current.Select(SanitizeRecipe).ToArray();
        }

        var legacy = Deserialize<List<LegacyRecipeBook>>(state, "recipes") ?? [];
        return legacy.Select(item => SanitizeRecipe(new RecipeBook(
            item.Id,
            item.Code,
            item.Name,
            item.Description,
            item.OutputQuantity,
            item.OutputUnit,
            item.Status,
            item.Groups,
            item.Costs,
            item.CreatedAt,
            item.UpdatedAt,
            item.OutputQuantity <= 0 ? 1m : item.OutputQuantity,
            RecipePortionUnitCatalog.Normalize(item.OutputUnit),
            0m,
            0m))).ToArray();
    }

    private static RecipeBook SanitizeRecipe(RecipeBook recipe)
    {
        var portionYield = recipe.PortionYield <= 0 ? (recipe.OutputQuantity <= 0 ? 1m : recipe.OutputQuantity) : recipe.PortionYield;
        var portionUnit = RecipePortionUnitCatalog.Normalize(
            string.IsNullOrWhiteSpace(recipe.PortionUnit)
                ? recipe.OutputUnit
                : recipe.PortionUnit);
        var portioningMode = recipe.PortioningMode;
        var portionWeightGr = recipe.PortionWeightGr < 0 ? 0m : recipe.PortionWeightGr;
        var portionWeightGroupId = recipe.PortionWeightGroupId is Guid groupId && recipe.Groups.Any(item => item.Id == groupId)
            ? recipe.PortionWeightGroupId
            : null;

        if (portioningMode == RecipePortioningMode.WeightBased && portionWeightGr <= 0)
        {
            portioningMode = RecipePortioningMode.Manual;
        }

        return recipe with
        {
            OutputQuantity = portionYield,
            OutputUnit = portionUnit,
            PortionYield = portionYield,
            PortionUnit = portionUnit,
            TargetMarginPercent = recipe.TargetMarginPercent < 0 ? 0m : recipe.TargetMarginPercent,
            SuggestedSellingPrice = recipe.SuggestedSellingPrice < 0 ? 0m : recipe.SuggestedSellingPrice,
            PortioningMode = portioningMode,
            PortionWeightGr = portionWeightGr,
            PortionWeightGroupId = portionWeightGroupId
        };
    }

    public string GenerateNextRecipeCode()
    {
        using var scope = _gate.EnterScope();
        var nextNumber = _recipes.Select(recipe => recipe.Code)
            .Where(code => code.StartsWith("RCP-", StringComparison.OrdinalIgnoreCase))
            .Select(code => code[4..])
            .Select(part => int.TryParse(part, out var number) ? number : 0)
            .DefaultIfEmpty()
            .Max() + 1;

        return $"RCP-{nextNumber:000}";
    }

    public Product AddProduct(Product product)
    {
        using var scope = _gate.EnterScope();
        _products.Add(product);
        TouchUnsafe();
        return product;
    }

    public Product UpdateProduct(Product product)
    {
        using var scope = _gate.EnterScope();
        var index = _products.FindIndex(item => item.Id == product.Id);
        if (index >= 0)
        {
            _products[index] = product;
            TouchUnsafe();
        }

        return product;
    }

    public bool RemoveProduct(Guid id)
    {
        using var scope = _gate.EnterScope();
        var removed = _products.RemoveAll(item => item.Id == id) > 0;
        if (!removed)
        {
            return false;
        }

        _productRecipes.RemoveAll(item => item.ProductId == id);
        _bomItems.RemoveAll(item => item.ProductId == id);
        TouchUnsafe();
        return true;
    }

    public Product? FindProduct(Guid id)
    {
        using var scope = _gate.EnterScope();
        return _products.FirstOrDefault(item => item.Id == id);
    }

    public bool ProductCodeExists(string code, Guid? exceptId = null)
    {
        using var scope = _gate.EnterScope();
        return _products.Any(product => product.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && (!exceptId.HasValue || product.Id != exceptId.Value));
    }

    public RawMaterial AddRawMaterial(RawMaterial material)
    {
        using var scope = _gate.EnterScope();
        _rawMaterials.Add(material);
        _materialPrices.Add(new MaterialPriceEntry(
            Guid.NewGuid(),
            material.Id,
            material.CreatedAt,
            material.PricePerPack,
            material.NetQuantity,
            material.NetUnit,
            material.BaseUnit,
            material.UpdatedAt,
            null,
            "Harga awal material"));
        TouchUnsafe();
        return material;
    }

    public RawMaterial UpdateRawMaterial(RawMaterial material, decimal? previousPricePerPack = null, string priceNote = "Update harga material")
    {
        using var scope = _gate.EnterScope();
        var index = _rawMaterials.FindIndex(item => item.Id == material.Id);
        if (index >= 0)
        {
            var existing = _rawMaterials[index];
            _rawMaterials[index] = material;

            var shouldTrackPrice = previousPricePerPack.HasValue
                ? previousPricePerPack.Value != material.PricePerPack
                : existing.PricePerPack != material.PricePerPack
                    || existing.NetQuantity != material.NetQuantity
                    || !existing.NetUnit.Equals(material.NetUnit, StringComparison.OrdinalIgnoreCase)
                    || !existing.BaseUnit.Equals(material.BaseUnit, StringComparison.OrdinalIgnoreCase);

            if (shouldTrackPrice)
            {
                _materialPrices.Add(new MaterialPriceEntry(
                    Guid.NewGuid(),
                    material.Id,
                    material.UpdatedAt,
                    material.PricePerPack,
                    material.NetQuantity,
                    material.NetUnit,
                    material.BaseUnit,
                    material.UpdatedAt,
                    previousPricePerPack ?? existing.PricePerPack,
                    priceNote));
            }

            TouchUnsafe();
        }

        return material;
    }

    public RawMaterial? FindRawMaterial(Guid id)
    {
        using var scope = _gate.EnterScope();
        return _rawMaterials.FirstOrDefault(item => item.Id == id);
    }

    public bool RemoveRawMaterial(Guid id)
    {
        using var scope = _gate.EnterScope();
        var removed = _rawMaterials.RemoveAll(item => item.Id == id) > 0;
        if (!removed)
        {
            return false;
        }

        _materialPrices.RemoveAll(item => item.MaterialId == id);
        _stockMovements.RemoveAll(item => item.MaterialId == id);
        TouchUnsafe();
        return true;
    }

    public bool MaterialCodeExists(string code, Guid? exceptId = null)
    {
        using var scope = _gate.EnterScope();
        return _rawMaterials.Any(material => material.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && (!exceptId.HasValue || material.Id != exceptId.Value));
    }

    public RecipeBook AddRecipeBook(RecipeBook recipe)
    {
        using var scope = _gate.EnterScope();
        _recipes.Add(recipe);
        TouchUnsafe();
        return recipe;
    }

    public RecipeBook UpdateRecipeBook(RecipeBook recipe)
    {
        using var scope = _gate.EnterScope();
        var index = _recipes.FindIndex(item => item.Id == recipe.Id);
        if (index >= 0)
        {
            _recipes[index] = recipe;
            TouchUnsafe();
        }

        return recipe;
    }

    public RecipeBook? FindRecipeBook(Guid id)
    {
        using var scope = _gate.EnterScope();
        return _recipes.FirstOrDefault(item => item.Id == id);
    }

    public bool RemoveRecipeBook(Guid id)
    {
        using var scope = _gate.EnterScope();
        var removed = _recipes.RemoveAll(item => item.Id == id) > 0;
        if (removed)
        {
            TouchUnsafe();
        }

        return removed;
    }

    public bool RecipeCodeExists(string code, Guid? exceptId = null)
    {
        using var scope = _gate.EnterScope();
        return _recipes.Any(recipe => recipe.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && (!exceptId.HasValue || recipe.Id != exceptId.Value));
    }

    public StockMovementEntry AddStockMovement(StockMovementEntry entry)
    {
        using var scope = _gate.EnterScope();
        _stockMovements.Add(entry);
        TouchUnsafe();
        return entry;
    }

    public IReadOnlyList<StockMovementEntry> AddStockMovements(IEnumerable<StockMovementEntry> entries)
    {
        using var scope = _gate.EnterScope();
        var added = entries.ToArray();
        _stockMovements.AddRange(added);
        TouchUnsafe();
        return added;
    }

    public bool HasBom(Guid productId)
    {
        using var scope = _gate.EnterScope();
        return _bomItems.Any(item => item.ProductId == productId);
    }

    public bool HasProduction(Guid productId)
    {
        using var scope = _gate.EnterScope();
        return _productionBatches.Any(item => item.ProductId == productId);
    }

    public bool MaterialUsedInBom(Guid materialId)
    {
        using var scope = _gate.EnterScope();
        return _bomItems.Any(item => item.MaterialId == materialId)
            || _recipes.Any(recipe => recipe.Groups.Any(group => group.Materials.Any(item => item.MaterialId == materialId)));
    }

    public bool MaterialUsedInProduction(Guid materialId)
    {
        using var scope = _gate.EnterScope();
        var affectedProducts = _bomItems.Where(item => item.MaterialId == materialId).Select(item => item.ProductId).Distinct().ToArray();
        return _productionBatches.Any(batch => affectedProducts.Contains(batch.ProductId));
    }

    public ProductRecipe? FindRecipe(Guid productId)
    {
        using var scope = _gate.EnterScope();
        return _productRecipes.FirstOrDefault(item => item.ProductId == productId);
    }

    public ProductRecipe UpsertRecipe(ProductRecipe recipe)
    {
        using var scope = _gate.EnterScope();
        var index = _productRecipes.FindIndex(item => item.ProductId == recipe.ProductId);
        if (index >= 0)
        {
            _productRecipes[index] = recipe;
        }
        else
        {
            _productRecipes.Add(recipe);
        }

        TouchUnsafe();
        return recipe;
    }

    public bool RemoveProductRecipe(Guid productId)
    {
        using var scope = _gate.EnterScope();
        var removed = _productRecipes.RemoveAll(item => item.ProductId == productId) > 0;
        if (removed)
        {
            TouchUnsafe();
        }

        return removed;
    }

    public BomItem UpsertBomItem(BomItem item)
    {
        using var scope = _gate.EnterScope();
        var index = _bomItems.FindIndex(entry => entry.ProductId == item.ProductId && entry.MaterialId == item.MaterialId);
        if (index >= 0)
        {
            _bomItems[index] = item;
        }
        else
        {
            _bomItems.Add(item);
        }

        TouchUnsafe();
        return item;
    }

    public bool RemoveBomItem(Guid productId, Guid materialId)
    {
        using var scope = _gate.EnterScope();
        var removed = _bomItems.RemoveAll(item => item.ProductId == productId && item.MaterialId == materialId) > 0;
        if (removed)
        {
            TouchUnsafe();
        }

        return removed;
    }

    public IReadOnlyList<BomItem> ReplaceBomItems(Guid productId, IEnumerable<BomItem> items)
    {
        using var scope = _gate.EnterScope();
        _bomItems.RemoveAll(item => item.ProductId == productId);
        var replacements = items.ToArray();
        _bomItems.AddRange(replacements);
        TouchUnsafe();
        return replacements;
    }

    public ProductionBatch? FindBatch(Guid id)
    {
        using var scope = _gate.EnterScope();
        return _productionBatches.FirstOrDefault(item => item.Id == id);
    }

    public ProductionBatch AddProductionBatch(ProductionBatch batch)
    {
        using var scope = _gate.EnterScope();
        _productionBatches.Add(batch);
        TouchUnsafe();
        return batch;
    }

    public ProductionBatch AddProductionBatch(
        ProductionBatch batch,
        IEnumerable<StockMovementEntry> stockMovements,
        IEnumerable<LaborCostEntry> laborCosts,
        IEnumerable<OverheadCostEntry> overheadCosts)
    {
        using var scope = _gate.EnterScope();
        _productionBatches.Add(batch);
        _stockMovements.AddRange(stockMovements);
        _laborCosts.AddRange(laborCosts);
        _overheadCosts.AddRange(overheadCosts);
        TouchUnsafe();
        return batch;
    }

    public ProductionBatch? UpdateProductionBatch(ProductionBatch batch)
    {
        using var scope = _gate.EnterScope();
        var index = _productionBatches.FindIndex(item => item.Id == batch.Id);
        if (index < 0)
        {
            return null;
        }

        _productionBatches[index] = batch;
        TouchUnsafe();
        return batch;
    }

    public LaborCostEntry? FindLaborCost(Guid id)
    {
        using var scope = _gate.EnterScope();
        return _laborCosts.FirstOrDefault(item => item.Id == id);
    }

    public LaborCostEntry AddLaborCost(LaborCostEntry entry)
    {
        using var scope = _gate.EnterScope();
        _laborCosts.Add(entry);
        TouchUnsafe();
        return entry;
    }

    public LaborCostEntry UpdateLaborCost(LaborCostEntry entry)
    {
        using var scope = _gate.EnterScope();
        var index = _laborCosts.FindIndex(item => item.Id == entry.Id);
        if (index >= 0)
        {
            _laborCosts[index] = entry;
            TouchUnsafe();
        }

        return entry;
    }

    public bool RemoveLaborCost(Guid id)
    {
        using var scope = _gate.EnterScope();
        var removed = _laborCosts.RemoveAll(item => item.Id == id) > 0;
        if (removed)
        {
            TouchUnsafe();
        }

        return removed;
    }

    public OverheadCostEntry? FindOverheadCost(Guid id)
    {
        using var scope = _gate.EnterScope();
        return _overheadCosts.FirstOrDefault(item => item.Id == id);
    }

    public OverheadCostEntry AddOverheadCost(OverheadCostEntry entry)
    {
        using var scope = _gate.EnterScope();
        _overheadCosts.Add(entry);
        TouchUnsafe();
        return entry;
    }

    public OverheadCostEntry UpdateOverheadCost(OverheadCostEntry entry)
    {
        using var scope = _gate.EnterScope();
        var index = _overheadCosts.FindIndex(item => item.Id == entry.Id);
        if (index >= 0)
        {
            _overheadCosts[index] = entry;
            TouchUnsafe();
        }

        return entry;
    }

    public bool RemoveOverheadCost(Guid id)
    {
        using var scope = _gate.EnterScope();
        var removed = _overheadCosts.RemoveAll(item => item.Id == id) > 0;
        if (removed)
        {
            TouchUnsafe();
        }

        return removed;
    }

    public PurchaseOrder? FindPurchaseOrder(Guid id)
    {
        using var scope = _gate.EnterScope();
        return _purchaseOrders.FirstOrDefault(item => item.Id == id);
    }

    public IReadOnlyList<PurchaseOrderLine> FindPurchaseOrderLines(Guid purchaseOrderId)
    {
        using var scope = _gate.EnterScope();
        return _purchaseOrderLines.Where(item => item.PurchaseOrderId == purchaseOrderId).ToArray();
    }

    public PurchaseOrder AddPurchaseOrder(PurchaseOrder order, IEnumerable<PurchaseOrderLine> lines, IEnumerable<StockMovementEntry>? stockMovements = null)
    {
        using var scope = _gate.EnterScope();
        _purchaseOrders.Insert(0, order);
        _purchaseOrderLines.AddRange(lines);
        if (stockMovements is not null)
        {
            _stockMovements.AddRange(stockMovements);
        }

        TouchUnsafe();
        return order;
    }

    public PurchaseOrder? UpdatePurchaseOrder(PurchaseOrder order, IEnumerable<StockMovementEntry>? stockMovements = null)
    {
        using var scope = _gate.EnterScope();
        var index = _purchaseOrders.FindIndex(item => item.Id == order.Id);
        if (index < 0)
        {
            return null;
        }

        _purchaseOrders[index] = order;
        if (stockMovements is not null)
        {
            _stockMovements.AddRange(stockMovements);
        }

        TouchUnsafe();
        return order;
    }

    public ManualLedgerEntry AddManualLedgerEntry(ManualLedgerEntry entry)
    {
        using var scope = _gate.EnterScope();
        _manualLedgerEntries.Insert(0, entry);
        TouchUnsafe();
        return entry;
    }

    public SaleTransaction? FindSale(Guid id)
    {
        using var scope = _gate.EnterScope();
        return _sales.FirstOrDefault(item => item.Id == id);
    }

    public IReadOnlyList<SaleLine> FindSaleLines(Guid saleId)
    {
        using var scope = _gate.EnterScope();
        return _saleLines.Where(item => item.SaleId == saleId).ToArray();
    }

    public SaleTransaction AddSale(SaleTransaction sale, IEnumerable<SaleLine> lines)
    {
        using var scope = _gate.EnterScope();
        _sales.Insert(0, sale);
        _saleLines.AddRange(lines);
        TouchUnsafe();
        return sale;
    }

    public SaleTransaction UpdateSale(SaleTransaction sale)
    {
        using var scope = _gate.EnterScope();
        var index = _sales.FindIndex(item => item.Id == sale.Id);
        if (index >= 0)
        {
            _sales[index] = sale;
            TouchUnsafe();
        }

        return sale;
    }

    public BusinessSettings GetBusinessSettings()
    {
        using var scope = _gate.EnterScope();
        return _businessSettings;
    }

    public BusinessSettings UpdateBusinessSettings(BusinessSettings settings)
    {
        using var scope = _gate.EnterScope();
        _businessSettings = settings;
        TouchUnsafe();
        return settings;
    }

    public (int Products, int Materials, int StockMovements, int Recipes, int ProductionBatches) ClearOperationalData()
    {
        using var scope = _gate.EnterScope();

        var summary = (
            Products: _products.Count,
            Materials: _rawMaterials.Count,
            StockMovements: _stockMovements.Count,
            Recipes: _recipes.Count + _productRecipes.Count + _bomItems.Count,
            ProductionBatches: _productionBatches.Count + _purchaseOrders.Count);

        _products.Clear();
        _rawMaterials.Clear();
        _materialPrices.Clear();
        _recipes.Clear();
        _stockMovements.Clear();
        _productRecipes.Clear();
        _bomItems.Clear();
        _productionBatches.Clear();
        _laborCosts.Clear();
        _overheadCosts.Clear();
        _purchaseOrders.Clear();
        _purchaseOrderLines.Clear();
        _manualLedgerEntries.Clear();
        _sales.Clear();
        _saleLines.Clear();

        TouchUnsafe();
        return summary;
    }

    public BusinessUser? FindUser(Guid id)
    {
        using var scope = _gate.EnterScope();
        return _users.FirstOrDefault(item => item.Id == id);
    }

    public bool UserIdentityExists(string email, string username, Guid? exceptId = null)
    {
        using var scope = _gate.EnterScope();
        return _users.Any(user =>
            (!exceptId.HasValue || user.Id != exceptId.Value) &&
            (user.Email.Equals(email, StringComparison.OrdinalIgnoreCase) || user.Username.Equals(username, StringComparison.OrdinalIgnoreCase)));
    }

    public BusinessUser AddUser(BusinessUser user)
    {
        using var scope = _gate.EnterScope();
        _users.Add(user);
        TouchUnsafe();
        return user;
    }

    public BusinessUser UpdateUser(BusinessUser user)
    {
        using var scope = _gate.EnterScope();
        var index = _users.FindIndex(item => item.Id == user.Id);
        if (index >= 0)
        {
            _users[index] = user;
            TouchUnsafe();
        }

        return user;
    }

    public AuditLogEntry AddAuditLog(AuditLogEntry entry)
    {
        using var scope = _gate.EnterScope();
        _auditLogEntries.Insert(0, entry);

        const int maxEntries = 400;
        if (_auditLogEntries.Count > maxEntries)
        {
            _auditLogEntries.RemoveRange(maxEntries, _auditLogEntries.Count - maxEntries);
        }

        TouchUnsafe();
        return entry;
    }

    public void SetSession(AuthSession? session)
    {
        using var scope = _gate.EnterScope();
        _authSession = session;
        TouchUnsafe();
    }

    public AppDataSnapshot CreateSnapshot()
    {
        using var scope = _gate.EnterScope();
        return new AppDataSnapshot(
            _products.ToArray(),
            _rawMaterials.ToArray(),
            _materialPrices.ToArray(),
            _recipes.ToArray(),
            _stockMovements.ToArray(),
            _productRecipes.ToArray(),
            _bomItems.ToArray(),
            _productionBatches.ToArray(),
            _laborCosts.ToArray(),
            _overheadCosts.ToArray(),
            _purchaseOrders.ToArray(),
            _purchaseOrderLines.ToArray(),
            _manualLedgerEntries.ToArray(),
            _sales.ToArray(),
            _saleLines.ToArray(),
            _users.ToArray(),
            _auditLogEntries.ToArray(),
            _businessSettings,
            _authSession);
    }

    public void RestoreSnapshot(AppDataSnapshot snapshot, bool clearSession = true)
    {
        using var scope = _gate.EnterScope();
        _products.Clear();
        _products.AddRange(snapshot.Products);
        _rawMaterials.Clear();
        _rawMaterials.AddRange(snapshot.RawMaterials.Select(SanitizeMaterial));
        _materialPrices.Clear();
        _materialPrices.AddRange(snapshot.MaterialPrices);
        _recipes.Clear();
        _recipes.AddRange(snapshot.Recipes.Select(SanitizeRecipe));
        _stockMovements.Clear();
        _stockMovements.AddRange(snapshot.StockMovements);
        _productRecipes.Clear();
        _productRecipes.AddRange(snapshot.ProductRecipes);
        _bomItems.Clear();
        _bomItems.AddRange(snapshot.BomItems);
        _productionBatches.Clear();
        _productionBatches.AddRange(snapshot.ProductionBatches);
        _laborCosts.Clear();
        _laborCosts.AddRange(snapshot.LaborCosts);
        _overheadCosts.Clear();
        _overheadCosts.AddRange(snapshot.OverheadCosts);
        _purchaseOrders.Clear();
        _purchaseOrders.AddRange(snapshot.PurchaseOrders.OrderByDescending(item => item.OrderedAt));
        _purchaseOrderLines.Clear();
        _purchaseOrderLines.AddRange(snapshot.PurchaseOrderLines);
        _manualLedgerEntries.Clear();
        _manualLedgerEntries.AddRange(snapshot.ManualLedgerEntries.OrderByDescending(item => item.OccurredAt));
        _sales.Clear();
        _sales.AddRange(snapshot.Sales.OrderByDescending(item => item.SoldAt));
        _saleLines.Clear();
        _saleLines.AddRange(snapshot.SaleLines);
        _users.Clear();
        _users.AddRange(snapshot.Users);
        _auditLogEntries.Clear();
        _auditLogEntries.AddRange(snapshot.AuditLogs.OrderByDescending(item => item.OccurredAt));
        _businessSettings = snapshot.BusinessSettings;
        _authSession = clearSession ? null : snapshot.AuthSession;
        EnsureMaterialPriceSeed();
        TouchUnsafe();
    }

    private sealed record LegacyRawMaterial(
        Guid Id,
        string Code,
        string Name,
        string Category,
        string? SupplierId,
        string? SupplierName,
        string Unit,
        decimal? UnitConversion,
        decimal PurchasePrice,
        decimal Stock,
        decimal MinimumStock,
        string? StorageLocation,
        string? Description,
        MaterialStatus Status,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private sealed record LegacyMaterialPriceEntry(
        Guid Id,
        Guid MaterialId,
        DateTime EffectiveAt,
        decimal PricePerUnit,
        DateTime UpdatedAt,
        decimal? PreviousPrice,
        string Note);

    private sealed record LegacyRecipeBook(
        Guid Id,
        string Code,
        string Name,
        string? Description,
        decimal OutputQuantity,
        string OutputUnit,
        RecipeStatus Status,
        IReadOnlyList<RecipeMaterialGroup> Groups,
        IReadOnlyList<RecipeCostComponent> Costs,
        DateTime CreatedAt,
        DateTime UpdatedAt);
}
