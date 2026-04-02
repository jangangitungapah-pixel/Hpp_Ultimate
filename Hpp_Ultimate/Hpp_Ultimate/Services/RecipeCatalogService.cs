using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class RecipeCatalogService(IMemoryCache cache, SeededBusinessDataStore store)
{
    public async Task<RecipeQueryResult> QueryAsync(string? search = null, CancellationToken cancellationToken = default)
    {
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var cacheKey = $"recipes:{store.Version}:{normalizedSearch}";

        if (cache.TryGetValue(cacheKey, out RecipeQueryResult? result))
        {
            return result!;
        }

        await Task.Delay(60, cancellationToken);

        result = BuildQueryResult(normalizedSearch);
        cache.Set(cacheKey, result, TimeSpan.FromSeconds(20));
        return result;
    }

    public Task<string> GenerateNextCodeAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(store.GenerateNextRecipeCode());

    public Task<RecipeUpsertRequest?> GetDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var recipe = store.FindRecipeBook(id);
        if (recipe is null)
        {
            return Task.FromResult<RecipeUpsertRequest?>(null);
        }

        var draft = new RecipeUpsertRequest
        {
            Id = recipe.Id,
            Code = recipe.Code,
            Name = recipe.Name,
            Description = recipe.Description,
            OutputQuantity = recipe.OutputQuantity,
            OutputUnit = recipe.OutputUnit,
            PortionYield = recipe.PortionYield,
            Status = recipe.Status,
            Groups = recipe.Groups
                .Select(group => new RecipeGroupInput
                {
                    Id = group.Id,
                    Name = group.Name,
                    Notes = group.Notes,
                    Materials = group.Materials
                        .Select(item => new RecipeMaterialInput
                        {
                            Id = item.Id,
                            MaterialId = item.MaterialId,
                            Quantity = item.Quantity,
                            Unit = item.Unit,
                            WastePercent = item.WastePercent,
                            Notes = item.Notes
                        })
                        .ToList()
                })
                .ToList(),
            Costs = recipe.Costs
                .Select(item => new RecipeCostInput
                {
                    Id = item.Id,
                    Type = item.Type,
                    Name = item.Name,
                    Amount = item.Amount,
                    Notes = item.Notes
                })
                .ToList()
        };

        return Task.FromResult<RecipeUpsertRequest?>(draft);
    }

    public Task<RecipeMutationResult> SaveAsync(RecipeUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var validation = ValidateRequest(request);
        if (validation is not null)
        {
            return Task.FromResult(validation);
        }

        var now = DateTime.Now;
        var recipe = new RecipeBook(
            request.Id ?? Guid.NewGuid(),
            string.IsNullOrWhiteSpace(request.Code) ? store.GenerateNextRecipeCode() : request.Code.Trim().ToUpperInvariant(),
            request.Name.Trim(),
            NormalizeOptional(request.Description),
            request.OutputQuantity,
            request.OutputUnit.Trim().ToLowerInvariant(),
            request.Status,
            request.Groups.Select(MapGroup).ToArray(),
            request.Costs
                .Where(item => !string.IsNullOrWhiteSpace(item.Name) && item.Amount > 0)
                .Select(item => new RecipeCostComponent(
                    item.Id == Guid.Empty ? Guid.NewGuid() : item.Id,
                    item.Type,
                    item.Name.Trim(),
                    item.Amount,
                    NormalizeOptional(item.Notes)))
                .ToArray(),
            request.Id is null ? now : store.FindRecipeBook(request.Id.Value)?.CreatedAt ?? now,
            now,
            request.PortionYield);

        if (request.Id is null)
        {
            store.AddRecipeBook(recipe);
            return Task.FromResult(new RecipeMutationResult(true, "Resep baru berhasil dibuat.", recipe));
        }

        if (store.FindRecipeBook(request.Id.Value) is null)
        {
            return Task.FromResult(new RecipeMutationResult(false, "Resep tidak ditemukan."));
        }

        store.UpdateRecipeBook(recipe);
        return Task.FromResult(new RecipeMutationResult(true, "Resep berhasil diperbarui.", recipe));
    }

    public Task<RecipeMutationResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var recipe = store.FindRecipeBook(id);
        if (recipe is null)
        {
            return Task.FromResult(new RecipeMutationResult(false, "Resep tidak ditemukan."));
        }

        store.RemoveRecipeBook(id);
        return Task.FromResult(new RecipeMutationResult(true, "Resep berhasil dihapus.", recipe));
    }

    public RecipeSummaryTotals CalculateTotals(RecipeUpsertRequest request)
    {
        var materialMap = store.RawMaterials.ToDictionary(item => item.Id);
        var materialCount = 0;
        var materialCost = 0m;

        foreach (var group in request.Groups)
        {
            foreach (var item in group.Materials)
            {
                if (item.MaterialId is not Guid materialId || !materialMap.TryGetValue(materialId, out var material))
                {
                    continue;
                }

                materialCount++;
                materialCost += RecipeCatalogMath.CalculateLineCost(item, material);
            }
        }

        var operationalCost = request.Costs
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && item.Amount > 0)
            .Sum(item => item.Amount);

        var totalCost = materialCost + operationalCost;
        var costPerOutput = request.OutputQuantity <= 0 ? 0m : totalCost / request.OutputQuantity;
        var costPerPortion = request.PortionYield <= 0 ? 0m : totalCost / request.PortionYield;

        return new RecipeSummaryTotals(
            request.Groups.Count,
            materialCount,
            request.Costs.Count(item => !string.IsNullOrWhiteSpace(item.Name) && item.Amount > 0),
            request.PortionYield,
            materialCost,
            operationalCost,
            totalCost,
            costPerOutput,
            costPerPortion);
    }

    private RecipeQueryResult BuildQueryResult(string? search)
    {
        var recipes = store.Recipes.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            recipes = recipes.Where(item =>
                item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Code.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (item.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var items = recipes
            .OrderByDescending(item => item.UpdatedAt)
            .Select(MapListItem)
            .ToArray();

        return new RecipeQueryResult(items, items.Length, store.GenerateNextRecipeCode());
    }

    private RecipeListItem MapListItem(RecipeBook recipe)
    {
        var materialMap = store.RawMaterials.ToDictionary(item => item.Id);
        var materialCount = 0;
        var materialCost = 0m;

        foreach (var group in recipe.Groups)
        {
            foreach (var item in group.Materials)
            {
                if (!materialMap.TryGetValue(item.MaterialId, out var material))
                {
                    continue;
                }

                materialCount++;
                materialCost += RecipeCatalogMath.CalculateLineCost(item, material);
            }
        }

        var operationalCost = recipe.Costs.Sum(item => item.Amount);
        var totalCost = materialCost + operationalCost;
        var costPerOutput = recipe.OutputQuantity <= 0 ? 0m : totalCost / recipe.OutputQuantity;
        var costPerPortion = recipe.PortionYield <= 0 ? 0m : totalCost / recipe.PortionYield;

        return new RecipeListItem(
            recipe.Id,
            recipe.Code,
            recipe.Name,
            recipe.Description,
            recipe.OutputQuantity,
            recipe.OutputUnit,
            recipe.Status,
            recipe.Groups.Count,
            materialCount,
            recipe.Costs.Count,
            materialCost,
            operationalCost,
            totalCost,
            costPerOutput,
            recipe.PortionYield,
            costPerPortion,
            recipe.Groups.Select(item => item.Name).Where(item => !string.IsNullOrWhiteSpace(item)).Take(4).ToArray(),
            recipe.UpdatedAt);
    }

    private RecipeMutationResult? ValidateRequest(RecipeUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new RecipeMutationResult(false, "Nama resep wajib diisi.");
        }

        if (!string.IsNullOrWhiteSpace(request.Code) && store.RecipeCodeExists(request.Code.Trim(), request.Id))
        {
            return new RecipeMutationResult(false, "Kode resep sudah dipakai.");
        }

        if (request.OutputQuantity <= 0)
        {
            return new RecipeMutationResult(false, "Output batch harus lebih besar dari 0.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputUnit))
        {
            return new RecipeMutationResult(false, "Satuan output wajib diisi.");
        }

        if (request.PortionYield <= 0)
        {
            return new RecipeMutationResult(false, "Jumlah porsi harus lebih besar dari 0.");
        }

        if (request.Groups.Count == 0)
        {
            return new RecipeMutationResult(false, "Minimal satu kelompok bahan wajib dibuat.");
        }

        var materialMap = store.RawMaterials.ToDictionary(item => item.Id);
        foreach (var group in request.Groups)
        {
            if (string.IsNullOrWhiteSpace(group.Name))
            {
                return new RecipeMutationResult(false, "Nama kelompok bahan wajib diisi.");
            }

            if (group.Materials.Count == 0)
            {
                return new RecipeMutationResult(false, $"Kelompok {group.Name.Trim()} belum memiliki material.");
            }

            foreach (var item in group.Materials)
            {
                if (item.MaterialId is not Guid materialId)
                {
                    return new RecipeMutationResult(false, $"Ada material kosong di kelompok {group.Name.Trim()}.");
                }

                if (!materialMap.TryGetValue(materialId, out var material))
                {
                    return new RecipeMutationResult(false, "Salah satu material resep tidak ditemukan di katalog.");
                }

                if (item.Quantity <= 0)
                {
                    return new RecipeMutationResult(false, $"Qty material {material.Name} harus lebih besar dari 0.");
                }

                if (string.IsNullOrWhiteSpace(item.Unit))
                {
                    return new RecipeMutationResult(false, $"Unit material {material.Name} wajib dipilih.");
                }

                if (RecipeCatalogMath.ConvertToBaseQuantity(item.Quantity, item.Unit, material) <= 0)
                {
                    return new RecipeMutationResult(false, $"Unit untuk material {material.Name} tidak kompatibel dengan katalog material.");
                }

                if (item.WastePercent < 0 || item.WastePercent > 100)
                {
                    return new RecipeMutationResult(false, $"Waste % untuk material {material.Name} harus di antara 0 sampai 100.");
                }
            }
        }

        foreach (var item in request.Costs.Where(item => !string.IsNullOrWhiteSpace(item.Name) || item.Amount > 0))
        {
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                return new RecipeMutationResult(false, "Nama overhead atau biaya produksi wajib diisi.");
            }

            if (item.Amount <= 0)
            {
                return new RecipeMutationResult(false, $"Nilai biaya {item.Name.Trim()} harus lebih besar dari 0.");
            }
        }

        return null;
    }

    private static RecipeMaterialGroup MapGroup(RecipeGroupInput group)
        => new(
            group.Id == Guid.Empty ? Guid.NewGuid() : group.Id,
            group.Name.Trim(),
            NormalizeOptional(group.Notes),
            group.Materials
                .Where(item => item.MaterialId is Guid && item.Quantity > 0 && !string.IsNullOrWhiteSpace(item.Unit))
                .Select(item => new RecipeMaterialLine(
                    item.Id == Guid.Empty ? Guid.NewGuid() : item.Id,
                    item.MaterialId!.Value,
                    item.Quantity,
                    item.Unit.Trim(),
                    item.WastePercent,
                    NormalizeOptional(item.Notes)))
                .ToArray());

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
