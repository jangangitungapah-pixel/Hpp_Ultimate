using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class ProductionService(
    SeededBusinessDataStore store,
    WorkspaceAccessService access,
    AuditTrailService auditTrail)
{
    public Task<ProductionSnapshot> GetSnapshotAsync(
        string? search = null,
        Guid? selectedRecipeId = null,
        int draftBatchCount = 1,
        CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            throw new InvalidOperationException(accessDecision.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var recipes = BuildRecipeOptions(normalizedSearch);
        var resolvedSelectedId = selectedRecipeId is Guid requested && recipes.Any(item => item.RecipeId == requested)
            ? requested
            : recipes.FirstOrDefault()?.RecipeId;

        var draft = resolvedSelectedId is Guid recipeId
            ? BuildDraft(recipeId, Math.Max(1, draftBatchCount))
            : null;

        var queue = store.ProductionBatches
            .Where(item => item.Status == ProductionRunStatus.Queued)
            .OrderBy(item => item.QueuedAt ?? item.ProducedAt)
            .Select(item => MapBatchListItem(item, BuildDraft(item.RecipeId, Math.Max(1, item.BatchCount))))
            .ToArray();

        var history = store.ProductionBatches
            .Where(item => item.Status != ProductionRunStatus.Queued)
            .OrderByDescending(item => item.Status == ProductionRunStatus.Running)
            .ThenByDescending(item => item.CompletedAt ?? item.ProducedAt)
            .Take(50)
            .Select(item => MapBatchListItem(item))
            .ToArray();

        return Task.FromResult(new ProductionSnapshot(
            recipes,
            recipes.Count,
            queue.Length,
            history.Count(item => item.Status == ProductionRunStatus.Running),
            history.Count(item => item.Status == ProductionRunStatus.Completed),
            resolvedSelectedId,
            draft,
            queue,
            history));
    }

    public Task<ProductionMutationResult> UpsertQueueAsync(ProductionBatchCreateRequest request, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            return Task.FromResult(new ProductionMutationResult(false, accessDecision.Message));
        }

        var actor = accessDecision.Actor!;
        cancellationToken.ThrowIfCancellationRequested();

        if (request.RecipeId is not Guid recipeId)
        {
            return Task.FromResult(new ProductionMutationResult(false, "Resep wajib dipilih."));
        }

        if (request.BatchCount <= 0)
        {
            return Task.FromResult(new ProductionMutationResult(false, "Jumlah batch minimal 1."));
        }

        if (request.TargetDurationMinutes <= 0)
        {
            return Task.FromResult(new ProductionMutationResult(false, "Target waktu produksi minimal 1 menit."));
        }

        var draft = BuildDraft(recipeId, request.BatchCount);
        if (draft is null || !draft.CanQueue)
        {
            return Task.FromResult(new ProductionMutationResult(false, draft?.ValidationMessages.FirstOrDefault() ?? "Draft produksi tidak dapat disusun."));
        }

        if (request.BatchId is Guid batchId)
        {
            var existing = store.FindBatch(batchId);
            if (existing is null || existing.Status != ProductionRunStatus.Queued)
            {
                return Task.FromResult(new ProductionMutationResult(false, "Antrian produksi tidak ditemukan."));
            }

            var updated = existing with
            {
                RecipeId = draft.RecipeId,
                QuantityProduced = request.BatchCount,
                Notes = NormalizeOptional(request.Notes),
                MaterialCost = decimal.Round(draft.EstimatedMaterialCost, 2),
                BatchCount = request.BatchCount,
                PortionYieldPerBatch = draft.PortionYieldPerBatch,
                TargetDurationMinutes = request.TargetDurationMinutes
            };

            store.UpdateProductionBatch(updated);
            auditTrail.Record(actor, "Production", "Update antrian produksi", draft.RecipeName, draft.RecipeId, $"Antrian {updated.BatchCode} diperbarui untuk {request.BatchCount} batch.");
            return Task.FromResult(new ProductionMutationResult(true, "Antrian produksi berhasil diperbarui.", MapBatchListItem(updated, draft)));
        }

        var queuedAt = DateTime.Now;
        var batch = new ProductionBatch(
            Guid.NewGuid(),
            Guid.Empty,
            GenerateNextBatchCode(),
            queuedAt,
            request.BatchCount,
            queuedAt,
            draft.RecipeId,
            NormalizeOptional(request.Notes),
            decimal.Round(draft.EstimatedMaterialCost, 2),
            0m,
            0m,
            request.BatchCount,
            draft.PortionYieldPerBatch,
            request.TargetDurationMinutes,
            ProductionRunStatus.Queued,
            null);

        store.AddProductionBatch(batch);
        auditTrail.Record(actor, "Production", "Tambah antrian produksi", draft.RecipeName, draft.RecipeId, $"Antrian {batch.BatchCode} ditambahkan untuk {request.BatchCount} batch.");
        return Task.FromResult(new ProductionMutationResult(true, draft.CanStart ? "Antrian produksi ditambahkan." : "Antrian ditambahkan, tetapi stok bahan saat ini belum cukup untuk mulai produksi.", MapBatchListItem(batch, draft)));
    }

    public Task<ProductionMutationResult> StartQueuedProductionAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            return Task.FromResult(new ProductionMutationResult(false, accessDecision.Message));
        }

        var actor = accessDecision.Actor!;
        cancellationToken.ThrowIfCancellationRequested();

        var existing = store.FindBatch(batchId);
        if (existing is null || existing.Status != ProductionRunStatus.Queued)
        {
            return Task.FromResult(new ProductionMutationResult(false, "Antrian produksi tidak ditemukan."));
        }

        var draft = BuildDraft(existing.RecipeId, Math.Max(1, existing.BatchCount));
        if (draft is null)
        {
            return Task.FromResult(new ProductionMutationResult(false, "Draft produksi tidak dapat disusun."));
        }

        if (!draft.CanStart)
        {
            return Task.FromResult(new ProductionMutationResult(false, draft.ValidationMessages.FirstOrDefault() ?? "Produksi belum bisa dimulai."));
        }

        var startedAt = DateTime.Now;
        var updated = existing with
        {
            ProducedAt = startedAt,
            Notes = NormalizeOptional(existing.Notes),
            MaterialCost = decimal.Round(draft.EstimatedMaterialCost, 2),
            BatchCount = Math.Max(1, existing.BatchCount),
            PortionYieldPerBatch = draft.PortionYieldPerBatch,
            Status = ProductionRunStatus.Running,
            CompletedAt = null
        };

        var stockMovements = draft.Requirements
            .Select(line => new StockMovementEntry(
                Guid.NewGuid(),
                line.MaterialId,
                StockMovementType.ProductionUsage,
                -line.RequiredQuantity,
                startedAt,
                $"Pemakaian produksi {existing.BatchCode} untuk resep {draft.RecipeName}",
                existing.Id))
            .ToArray();

        store.UpdateProductionBatch(updated);
        store.AddStockMovements(stockMovements);
        auditTrail.Record(actor, "Production", "Mulai produksi", draft.RecipeName, draft.RecipeId, $"Produksi {existing.BatchCode} dimulai untuk {existing.BatchCount} batch resep {draft.RecipeName}.");
        return Task.FromResult(new ProductionMutationResult(true, "Produksi berhasil dimulai. Stok bahan langsung dikurangi.", MapBatchListItem(updated)));
    }

    public Task<ProductionMutationResult> CompleteProductionAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            return Task.FromResult(new ProductionMutationResult(false, accessDecision.Message));
        }

        var actor = accessDecision.Actor!;
        cancellationToken.ThrowIfCancellationRequested();

        var existing = store.FindBatch(batchId);
        if (existing is null)
        {
            return Task.FromResult(new ProductionMutationResult(false, "Produksi tidak ditemukan."));
        }

        if (existing.Status == ProductionRunStatus.Completed)
        {
            return Task.FromResult(new ProductionMutationResult(false, "Produksi ini sudah selesai."));
        }

        if (existing.Status == ProductionRunStatus.Queued)
        {
            return Task.FromResult(new ProductionMutationResult(false, "Antrian belum dimulai."));
        }

        var completedAt = DateTime.Now;
        var updated = existing with
        {
            Status = ProductionRunStatus.Completed,
            CompletedAt = completedAt
        };

        store.UpdateProductionBatch(updated);

        var recipe = existing.RecipeId == Guid.Empty ? null : store.FindRecipeBook(existing.RecipeId);
        auditTrail.Record(actor, "Production", "Selesaikan produksi", recipe?.Name ?? existing.BatchCode, existing.RecipeId, $"Produksi {existing.BatchCode} diselesaikan.");
        return Task.FromResult(new ProductionMutationResult(true, "Produksi ditandai selesai.", MapBatchListItem(updated)));
    }

    private IReadOnlyList<ProductionRecipeOption> BuildRecipeOptions(string? search)
    {
        var rows = store.Recipes.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            rows = rows.Where(item =>
                item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Code.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return rows
            .OrderByDescending(item => item.UpdatedAt)
            .Select(recipe =>
            {
                var draft = BuildDraft(recipe.Id, 1);
                var readiness = draft?.ValidationMessages.FirstOrDefault() ?? "Siap dijadwalkan.";

                return new ProductionRecipeOption(
                    recipe.Id,
                    recipe.Code,
                    recipe.Name,
                    recipe.Status,
                    recipe.OutputQuantity,
                    recipe.OutputUnit,
                    recipe.PortionYield,
                    recipe.PortionUnit,
                    recipe.Groups.Sum(group => group.Materials.Count),
                    store.ProductionBatches.Any(item => item.RecipeId == recipe.Id && item.Status == ProductionRunStatus.Running),
                    draft?.CanStart ?? false,
                    readiness);
            })
            .ToArray();
    }

    private ProductionDraft? BuildDraft(Guid recipeId, int batchCount)
    {
        var recipe = store.FindRecipeBook(recipeId);
        if (recipe is null)
        {
            return null;
        }

        var validationMessages = new List<string>();
        if (recipe.Status != RecipeStatus.Active)
        {
            validationMessages.Add("Resep masih draft atau tidak aktif.");
        }

        var materialMap = store.RawMaterials.ToDictionary(item => item.Id);
        var onHandMap = store.StockMovements
            .GroupBy(item => item.MaterialId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));

        var aggregated = new Dictionary<Guid, decimal>();
        foreach (var group in recipe.Groups)
        {
            foreach (var line in group.Materials)
            {
                if (!materialMap.TryGetValue(line.MaterialId, out var material))
                {
                    validationMessages.Add($"Material resep {line.MaterialId} tidak ditemukan di katalog.");
                    continue;
                }

                var baseQuantity = RecipeCatalogMath.ConvertToBaseQuantity(line.Quantity, line.Unit, material);
                if (baseQuantity <= 0)
                {
                    validationMessages.Add($"Satuan material {material.Name} tidak kompatibel.");
                    continue;
                }

                var requiredPerBatch = baseQuantity * (1m + Math.Max(0m, line.WastePercent) / 100m);
                aggregated[line.MaterialId] = aggregated.TryGetValue(line.MaterialId, out var existing)
                    ? existing + requiredPerBatch
                    : requiredPerBatch;
            }
        }

        if (aggregated.Count == 0)
        {
            validationMessages.Add("Resep belum memiliki material produksi.");
        }

        var hasStockIssue = false;
        var requirements = aggregated
            .Select(item =>
            {
                materialMap.TryGetValue(item.Key, out var material);
                var requiredQuantity = decimal.Round(item.Value * batchCount, 4);
                var availableQuantity = onHandMap.GetValueOrDefault(item.Key);
                var shortageQuantity = Math.Max(0m, requiredQuantity - availableQuantity);
                var estimatedCost = decimal.Round(requiredQuantity * (material?.CostPerBaseUnit ?? 0m), 2);

                if (material is null)
                {
                    hasStockIssue = true;
                    validationMessages.Add($"Material {item.Key} tidak ditemukan di katalog.");
                }
                else if (availableQuantity <= 0)
                {
                    hasStockIssue = true;
                    validationMessages.Add($"Stok {material.Name} kosong.");
                }
                else if (shortageQuantity > 0)
                {
                    hasStockIssue = true;
                    validationMessages.Add($"Stok {material.Name} kurang {shortageQuantity:0.####} {material.BaseUnit}.");
                }

                return new ProductionRequirementLine(
                    item.Key,
                    material?.Code ?? "-",
                    material?.Name ?? "Material tidak ditemukan",
                    material?.Brand,
                    material?.BaseUnit ?? "-",
                    decimal.Round(item.Value, 4),
                    requiredQuantity,
                    availableQuantity,
                    shortageQuantity,
                    estimatedCost,
                    material is not null && shortageQuantity <= 0);
            })
            .OrderBy(item => item.MaterialName)
            .ToArray();

        var canQueue = recipe.Status == RecipeStatus.Active && requirements.Length > 0;
        var canStart = canQueue && !hasStockIssue;

        return new ProductionDraft(
            recipe.Id,
            recipe.Code,
            recipe.Name,
            recipe.OutputQuantity,
            recipe.OutputUnit,
            recipe.PortionYield,
            recipe.PortionUnit,
            Math.Max(1, batchCount),
            decimal.Round(recipe.OutputQuantity * Math.Max(1, batchCount), 2),
            decimal.Round(recipe.PortionYield * Math.Max(1, batchCount), 2),
            requirements.Sum(item => item.EstimatedCost),
            canQueue,
            canStart,
            validationMessages.Distinct().ToArray(),
            requirements);
    }

    private ProductionBatchListItem MapBatchListItem(ProductionBatch batch, ProductionDraft? draft = null)
    {
        var recipe = batch.RecipeId == Guid.Empty ? null : store.FindRecipeBook(batch.RecipeId);
        var product = batch.ProductId == Guid.Empty ? null : store.FindProduct(batch.ProductId);
        var batchCount = batch.BatchCount > 0 ? batch.BatchCount : Math.Max(1, batch.QuantityProduced);
        var outputQuantityPerBatch = recipe?.OutputQuantity ?? 0m;
        var outputUnit = recipe?.OutputUnit ?? product?.Unit ?? "batch";
        var totalOutputQuantity = outputQuantityPerBatch > 0
            ? decimal.Round(outputQuantityPerBatch * batchCount, 2)
            : batch.QuantityProduced;
        var portionYieldPerBatch = batch.PortionYieldPerBatch > 0 ? batch.PortionYieldPerBatch : recipe?.PortionYield ?? 0m;
        var portionUnit = recipe?.PortionUnit ?? product?.Unit ?? outputUnit;
        var totalPortions = portionYieldPerBatch > 0
            ? decimal.Round(portionYieldPerBatch * batchCount, 2)
            : batchCount;

        return new ProductionBatchListItem(
            batch.Id,
            batch.BatchCode,
            batch.RecipeId,
            recipe?.Code ?? product?.Code ?? "-",
            recipe?.Name ?? product?.Name ?? "Produksi",
            batchCount,
            totalOutputQuantity,
            outputUnit,
            totalPortions,
            portionUnit,
            batch.QueuedAt ?? batch.ProducedAt,
            batch.Status == ProductionRunStatus.Queued ? null : batch.ProducedAt,
            batch.CompletedAt,
            batch.TargetDurationMinutes,
            batch.Status,
            batch.MaterialCost,
            batch.Notes,
            draft?.CanStart ?? true,
            draft?.ValidationMessages.FirstOrDefault());
    }

    private string GenerateNextBatchCode()
    {
        var nextNumber = store.ProductionBatches
            .Select(item => item.BatchCode)
            .Where(code => code.StartsWith("PRD-", StringComparison.OrdinalIgnoreCase) || code.StartsWith("BAT-", StringComparison.OrdinalIgnoreCase))
            .Select(code => code.Contains('-') ? code[(code.IndexOf('-') + 1)..] : code)
            .Select(part => int.TryParse(part, out var number) ? number : 0)
            .DefaultIfEmpty()
            .Max() + 1;

        return $"PRD-{nextNumber:000}";
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
