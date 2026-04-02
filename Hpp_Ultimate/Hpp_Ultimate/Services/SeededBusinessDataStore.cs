using System.Text.Json;
using Microsoft.Data.Sqlite;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class SeededBusinessDataStore : IBusinessDataStore
{
    private const string TableName = "AppState";
    private readonly Lock _gate = new();
    private readonly string _dbPath;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly List<Product> _products = [];
    private readonly List<RawMaterial> _rawMaterials = [];
    private readonly List<MaterialPriceEntry> _materialPrices = [];
    private readonly List<StockMovementEntry> _stockMovements = [];
    private readonly List<ProductRecipe> _productRecipes = [];
    private readonly List<BomItem> _bomItems = [];
    private readonly List<ProductionBatch> _productionBatches = [];
    private readonly List<LaborCostEntry> _laborCosts = [];
    private readonly List<OverheadCostEntry> _overheadCosts = [];
    private readonly List<BusinessUser> _users = [];
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
        DateTime.Now);

    public SeededBusinessDataStore(string dbPath)
    {
        _dbPath = dbPath;
        LoadOrInitialize();
    }

    public event Action? Changed;

    public long Version { get; private set; } = 1;

    public IReadOnlyList<Product> Products => _products;
    public IReadOnlyList<RawMaterial> RawMaterials => _rawMaterials;
    public IReadOnlyList<MaterialPriceEntry> MaterialPrices => _materialPrices;
    public IReadOnlyList<StockMovementEntry> StockMovements => _stockMovements;
    public IReadOnlyList<ProductRecipe> ProductRecipes => _productRecipes;
    public IReadOnlyList<BomItem> BomItems => _bomItems;
    public IReadOnlyList<ProductionBatch> ProductionBatches => _productionBatches;
    public IReadOnlyList<LaborCostEntry> LaborCosts => _laborCosts;
    public IReadOnlyList<OverheadCostEntry> OverheadCosts => _overheadCosts;
    public IReadOnlyList<BusinessUser> Users => _users;
    public AuthSession? AuthSession => _authSession;
    public BusinessSettings BusinessSettings => _businessSettings;

    private void LoadOrInitialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        EnsureDatabase();

        if (!TryLoadFromDatabase())
        {
            InitializeEmptyState();
            PersistStateUnsafe();
        }
    }

    private void EnsureDatabase()
    {
        using var connection = CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE IF NOT EXISTS {TableName} (
                StateKey TEXT PRIMARY KEY,
                Json TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private bool TryLoadFromDatabase()
    {
        using var connection = CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT StateKey, Json FROM {TableName};";

        using var reader = command.ExecuteReader();
        var state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            state[reader.GetString(0)] = reader.GetString(1);
        }

        if (state.Count == 0)
        {
            return false;
        }

        _products.Clear();
        _rawMaterials.Clear();
        _materialPrices.Clear();
        _stockMovements.Clear();
        _productRecipes.Clear();
        _bomItems.Clear();
        _productionBatches.Clear();
        _laborCosts.Clear();
        _overheadCosts.Clear();
        _users.Clear();

        _products.AddRange(Deserialize<List<Product>>(state, "products") ?? []);
        _rawMaterials.AddRange(LoadRawMaterials(state));
        _materialPrices.AddRange(LoadMaterialPrices(state, _rawMaterials));
        _stockMovements.AddRange(Deserialize<List<StockMovementEntry>>(state, "stockMovements") ?? []);
        _productRecipes.AddRange(Deserialize<List<ProductRecipe>>(state, "productRecipes") ?? []);
        _bomItems.AddRange(Deserialize<List<BomItem>>(state, "bomItems") ?? []);
        _productionBatches.AddRange(Deserialize<List<ProductionBatch>>(state, "productionBatches") ?? []);
        _laborCosts.AddRange(Deserialize<List<LaborCostEntry>>(state, "laborCosts") ?? []);
        _overheadCosts.AddRange(Deserialize<List<OverheadCostEntry>>(state, "overheadCosts") ?? []);
        _users.AddRange(Deserialize<List<BusinessUser>>(state, "users") ?? []);
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
        UpsertState(connection, transaction, "stockMovements", _stockMovements);
        UpsertState(connection, transaction, "productRecipes", _productRecipes);
        UpsertState(connection, transaction, "bomItems", _bomItems);
        UpsertState(connection, transaction, "productionBatches", _productionBatches);
        UpsertState(connection, transaction, "laborCosts", _laborCosts);
        UpsertState(connection, transaction, "overheadCosts", _overheadCosts);
        UpsertState(connection, transaction, "users", _users);
        UpsertState(connection, transaction, "authSession", _authSession);
        UpsertState(connection, transaction, "businessSettings", _businessSettings);

        transaction.Commit();
    }

    private void UpsertState(SqliteConnection connection, SqliteTransaction transaction, string key, object? value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {TableName} (StateKey, Json, UpdatedAt)
            VALUES ($key, $json, $updatedAt)
            ON CONFLICT(StateKey) DO UPDATE SET
                Json = excluded.Json,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(value, _jsonOptions));
        command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
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
                item.UpdatedAt));
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
            Description = string.IsNullOrWhiteSpace(material.Description) ? null : material.Description.Trim()
        };

    private SqliteConnection CreateConnection()
        => new($"Data Source={_dbPath}");

    private void InitializeEmptyState()
    {
        _products.Clear();
        _rawMaterials.Clear();
        _materialPrices.Clear();
        _stockMovements.Clear();
        _productRecipes.Clear();
        _bomItems.Clear();
        _productionBatches.Clear();
        _laborCosts.Clear();
        _overheadCosts.Clear();
        _users.Clear();
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
        return _bomItems.Any(item => item.MaterialId == materialId);
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

    public ProductionBatch? FindBatch(Guid id)
    {
        using var scope = _gate.EnterScope();
        return _productionBatches.FirstOrDefault(item => item.Id == id);
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
            Recipes: _productRecipes.Count + _bomItems.Count,
            ProductionBatches: _productionBatches.Count);

        _products.Clear();
        _rawMaterials.Clear();
        _materialPrices.Clear();
        _stockMovements.Clear();
        _productRecipes.Clear();
        _bomItems.Clear();
        _productionBatches.Clear();
        _laborCosts.Clear();
        _overheadCosts.Clear();

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

    public void SetSession(AuthSession? session)
    {
        using var scope = _gate.EnterScope();
        _authSession = session;
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
}
