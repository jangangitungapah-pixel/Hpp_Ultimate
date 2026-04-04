using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;
using ClosedXML.Excel;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualBasic.FileIO;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class RawMaterialCatalogService(
    IMemoryCache cache,
    SeededBusinessDataStore store,
    WorkspaceAccessService access,
    AuditTrailService auditTrail)
{
    private static readonly CultureInfo IdCulture = new("id-ID");

    public static IReadOnlyList<MaterialUnitOption> BaseUnits => MaterialUnitCatalog.BaseUnits;

    public static IReadOnlyList<MaterialUnitOption> GetCompatibleUnits(string baseUnit)
        => MaterialUnitCatalog.GetCompatibleUnits(baseUnit);

    public async Task<RawMaterialQueryResult> QueryAsync(RawMaterialQuery query, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            throw new InvalidOperationException(accessDecision.Message);
        }

        var normalized = Normalize(query);
        var cacheKey = $"rawmaterials:{store.Version}:{normalized.Search}:{normalized.Status}:{normalized.SortBy}:{normalized.Descending}:{normalized.Page}:{normalized.PageSize}";

        if (cache.TryGetValue(cacheKey, out RawMaterialQueryResult? result))
        {
            return result!;
        }

        await Task.Delay(80, cancellationToken);

        result = BuildQueryResult(normalized);
        cache.Set(cacheKey, result, TimeSpan.FromSeconds(20));
        return result;
    }

    public Task<RawMaterialDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            throw new InvalidOperationException(accessDecision.Message);
        }

        return Task.FromResult(BuildDetail(id));
    }

    public Task<string> GenerateNextCodeAsync(CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            throw new InvalidOperationException(accessDecision.Message);
        }

        return Task.FromResult(store.GenerateNextMaterialCode());
    }

    public Task<RawMaterialMutationResult> CreateAsync(RawMaterialUpsertRequest request, CancellationToken cancellationToken = default)
        => CreateAsync(request, auditMutation: true, cancellationToken);

    private Task<RawMaterialMutationResult> CreateAsync(
        RawMaterialUpsertRequest request,
        bool auditMutation,
        CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            return Task.FromResult(new RawMaterialMutationResult(false, accessDecision.Message));
        }

        var actor = accessDecision.Actor!;
        var validation = ValidateRequest(request, null);
        if (validation is not null)
        {
            return Task.FromResult(validation);
        }

        var now = DateTime.Now;
        var material = new RawMaterial(
            Guid.NewGuid(),
            string.IsNullOrWhiteSpace(request.Code) ? store.GenerateNextMaterialCode() : request.Code.Trim().ToUpperInvariant(),
            request.Name.Trim(),
            NormalizeOptional(request.Brand),
            NormalizeUnit(request.BaseUnit),
            request.NetQuantity,
            NormalizeUnit(request.NetUnit),
            request.PricePerPack,
            NormalizeConversions(request.UnitConversions),
            NormalizeOptional(request.Description),
            request.Status,
            now,
            now);

        store.AddRawMaterial(material);
        if (auditMutation)
        {
            auditTrail.Record(actor, "Material", "Tambah material", material.Name, material.Id, $"Material {material.Name} ditambahkan.");
        }

        return Task.FromResult(new RawMaterialMutationResult(true, "Material berhasil ditambahkan.", material));
    }

    public Task<RawMaterialMutationResult> UpdateAsync(Guid id, RawMaterialUpsertRequest request, CancellationToken cancellationToken = default)
        => UpdateAsync(id, request, auditMutation: true, cancellationToken);

    private Task<RawMaterialMutationResult> UpdateAsync(
        Guid id,
        RawMaterialUpsertRequest request,
        bool auditMutation,
        CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            return Task.FromResult(new RawMaterialMutationResult(false, accessDecision.Message));
        }

        var actor = accessDecision.Actor!;
        var existing = store.FindRawMaterial(id);
        if (existing is null)
        {
            return Task.FromResult(new RawMaterialMutationResult(false, "Material tidak ditemukan."));
        }

        var validation = ValidateRequest(request, id);
        if (validation is not null)
        {
            return Task.FromResult(validation);
        }

        var updated = existing with
        {
            Name = request.Name.Trim(),
            Brand = NormalizeOptional(request.Brand),
            BaseUnit = NormalizeUnit(request.BaseUnit),
            NetQuantity = request.NetQuantity,
            NetUnit = NormalizeUnit(request.NetUnit),
            PricePerPack = request.PricePerPack,
            UnitConversions = NormalizeConversions(request.UnitConversions),
            Description = NormalizeOptional(request.Description),
            Status = request.Status,
            UpdatedAt = DateTime.Now
        };

        store.UpdateRawMaterial(updated, existing.PricePerPack);
        if (auditMutation)
        {
            auditTrail.Record(actor, "Material", "Update material", updated.Name, updated.Id, $"Material {updated.Name} diperbarui.");
        }

        return Task.FromResult(new RawMaterialMutationResult(true, "Material berhasil diperbarui.", updated));
    }

    public Task<RawMaterialMutationResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            return Task.FromResult(new RawMaterialMutationResult(false, accessDecision.Message));
        }

        var actor = accessDecision.Actor!;
        var existing = store.FindRawMaterial(id);
        if (existing is null)
        {
            return Task.FromResult(new RawMaterialMutationResult(false, "Material tidak ditemukan."));
        }

        var usedInBom = store.MaterialUsedInBom(id);
        var usedInProduction = store.MaterialUsedInProduction(id);

        if (usedInBom || usedInProduction)
        {
            var updated = existing with
            {
                Status = MaterialStatus.Inactive,
                UpdatedAt = DateTime.Now
            };

            store.UpdateRawMaterial(updated, existing.PricePerPack, "Penonaktifan material");
            auditTrail.Record(actor, "Material", "Nonaktifkan material", updated.Name, updated.Id, $"Material {updated.Name} dipindahkan ke status nonaktif karena masih dipakai.");
            return Task.FromResult(new RawMaterialMutationResult(true, "Material dipindahkan ke status nonaktif karena sudah dipakai modul lain.", updated, usedInBom, usedInProduction));
        }

        store.RemoveRawMaterial(id);
        auditTrail.Record(actor, "Material", "Hapus material", existing.Name, existing.Id, $"Material {existing.Name} dihapus.");
        return Task.FromResult(new RawMaterialMutationResult(true, "Material berhasil dihapus.", existing, WasDeleted: true));
    }

    public async Task<RawMaterialImportPreviewResult> PreviewImportAsync(string fileName, Stream fileStream, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            throw new InvalidOperationException(accessDecision.Message);
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var rows = extension switch
        {
            ".csv" => await ReadCsvAsync(fileStream, cancellationToken),
            ".xlsx" => await ReadXlsxAsync(fileStream, cancellationToken),
            _ => throw new InvalidOperationException("Format file belum didukung. Gunakan .csv atau .xlsx.")
        };

        var headers = rows.Count == 0 ? Array.Empty<string>() : rows[0];
        var previewRows = new List<RawMaterialImportPreviewRow>();

        for (var index = 1; index < rows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = rows[index];
            if (raw.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            previewRows.Add(MapImportRow(index + 1, headers, raw));
        }

        return new RawMaterialImportPreviewResult(
            fileName,
            headers,
            previewRows,
            previewRows.Count(item => item.IsValid),
            previewRows.Count(item => !item.IsValid));
    }

    public async Task<RawMaterialImportResult> CommitImportAsync(RawMaterialImportCommitRequest request, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            return new RawMaterialImportResult(false, accessDecision.Message, 0, 0, 0, 0, []);
        }

        var actor = accessDecision.Actor!;
        if (!request.SkipInvalidRows && request.Rows.Any(item => !item.IsValid))
        {
            var blockingErrors = request.Rows
                .Where(item => !item.IsValid)
                .Take(5)
                .Select(item => $"Baris {item.RowNumber}: {string.Join(" ", item.Errors)}")
                .ToArray();

            return new RawMaterialImportResult(false, "Import dibatalkan karena masih ada row yang invalid.", 0, 0, 0, request.Rows.Count(item => !item.IsValid), blockingErrors);
        }

        var errors = new List<string>();
        var imported = 0;
        var updated = 0;
        var unchanged = 0;
        var skipped = 0;
        var lastRowByIdentity = request.Rows
            .Where(item => item.IsValid)
            .GroupBy(item => GetImportIdentityKey(item, includeBrand: true))
            .ToDictionary(group => group.Key, group => group.Max(item => item.RowNumber), StringComparer.Ordinal);

        foreach (var row in request.Rows.OrderBy(item => item.RowNumber))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!row.IsValid)
            {
                skipped++;
                if (!request.SkipInvalidRows)
                {
                    errors.Add($"Baris {row.RowNumber} tidak valid.");
                }

                continue;
            }

            var identityKey = GetImportIdentityKey(row, includeBrand: true);
            if (lastRowByIdentity.TryGetValue(identityKey, out var lastRowNumber) && lastRowNumber != row.RowNumber)
            {
                unchanged++;
                continue;
            }

            var existing = FindExistingMaterialForImport(row);
            if (existing is null)
            {
                var createResult = await CreateAsync(new RawMaterialUpsertRequest
                {
                    Code = await GenerateNextCodeAsync(cancellationToken),
                    Name = row.Name,
                    Brand = row.Brand,
                    BaseUnit = row.BaseUnit,
                    NetQuantity = row.NetQuantity ?? 0m,
                    NetUnit = row.NetUnit,
                    PricePerPack = row.PricePerPack ?? 0m,
                    Status = MaterialStatus.Active
                }, auditMutation: false, cancellationToken);

                if (createResult.Success)
                {
                    imported++;
                    continue;
                }

                skipped++;
                errors.Add($"Baris {row.RowNumber}: {createResult.Message}");
                continue;
            }

            if (IsEquivalentImport(existing, row))
            {
                unchanged++;
                continue;
            }

            var updateResult = await UpdateAsync(existing.Id, new RawMaterialUpsertRequest
            {
                Id = existing.Id,
                Code = existing.Code,
                Name = row.Name,
                Brand = row.Brand,
                BaseUnit = row.BaseUnit,
                NetQuantity = row.NetQuantity ?? 0m,
                NetUnit = row.NetUnit,
                PricePerPack = row.PricePerPack ?? 0m,
                UnitConversions = existing.UnitConversions
                    .Select(item => new RawMaterialUnitConversionInput
                    {
                        UnitName = item.UnitName,
                        ConversionQuantity = item.ConversionQuantity
                    })
                    .ToList(),
                Description = existing.Description,
                Status = existing.Status
            }, auditMutation: false, cancellationToken);

            if (updateResult.Success)
            {
                updated++;
                continue;
            }

            skipped++;
            errors.Add($"Baris {row.RowNumber}: {updateResult.Message}");
        }

        var success = request.SkipInvalidRows || errors.Count == 0;
        var message = $"Import selesai. {imported} material baru, {updated} material diperbarui, {unchanged} data sama dilewati, {skipped} baris dilewati karena error.";
        auditTrail.Record(actor, "Material", "Import material", "Katalog material", null, message);

        return new RawMaterialImportResult(success, message, imported, updated, unchanged, skipped, errors);
    }

    public async Task<string> ExportCsvAsync(CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            throw new InvalidOperationException(accessDecision.Message);
        }

        var result = await QueryAsync(new RawMaterialQuery(PageSize: 500), cancellationToken);
        var lines = new List<string>
        {
            "kode,nama_material,merk,base_unit,net_qty,net_unit,harga_per_pack,modal_per_unit"
        };

        lines.AddRange(result.Items.Select(item =>
            Csv(item.Code, item.Name, item.Brand, item.BaseUnit, item.NetQuantity, item.NetUnit, item.PricePerPack, item.CostPerUnit)));

        return string.Join(Environment.NewLine, lines);
    }

    public static string GetImportTemplateCsv()
        => string.Join(Environment.NewLine,
        [
            "nama_material,merk,base_unit,net_qty,net_unit,harga_per_pack",
            "\"Tepung Terigu\",\"Segitiga Biru\",\"gr\",1000,\"gr\",14500"
        ]);

    private RawMaterialQueryResult BuildQueryResult(RawMaterialQuery query)
    {
        var rows = store.RawMaterials.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            rows = rows.Where(material =>
                material.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ||
                (material.Brand?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (query.Status is not null)
        {
            rows = rows.Where(material => material.Status == query.Status);
        }

        var mapped = rows.Select(MapToListItem);
        mapped = query.SortBy.ToLowerInvariant() switch
        {
            "name" => query.Descending ? mapped.OrderByDescending(item => item.Name) : mapped.OrderBy(item => item.Name),
            "brand" => query.Descending ? mapped.OrderByDescending(item => item.Brand) : mapped.OrderBy(item => item.Brand),
            "price" => query.Descending ? mapped.OrderByDescending(item => item.PricePerPack) : mapped.OrderBy(item => item.PricePerPack),
            "cost" => query.Descending ? mapped.OrderByDescending(item => item.CostPerUnit) : mapped.OrderBy(item => item.CostPerUnit),
            "net" => query.Descending ? mapped.OrderByDescending(item => item.NetQuantity) : mapped.OrderBy(item => item.NetQuantity),
            _ => query.Descending ? mapped.OrderByDescending(item => item.UpdatedAt) : mapped.OrderBy(item => item.UpdatedAt)
        };

        var total = mapped.Count();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 5, 25);
        var items = mapped.Skip((page - 1) * pageSize).Take(pageSize).ToArray();

        return new RawMaterialQueryResult(items, total, page, pageSize, store.GenerateNextMaterialCode());
    }

    private RawMaterialDetail? BuildDetail(Guid id)
    {
        var material = store.FindRawMaterial(id);
        if (material is null)
        {
            return null;
        }

        var linkedRecipes = store.Recipes
            .Where(recipe => recipe.Groups.Any(group => group.Materials.Any(item => item.MaterialId == id)))
            .Select(recipe => recipe.Name);

        var bomProducts = store.BomItems
            .Where(item => item.MaterialId == id)
            .Select(item => store.Products.FirstOrDefault(product => product.Id == item.ProductId)?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Concat(linkedRecipes)
            .Distinct()
            .Take(8)
            .ToArray();

        var priceHistory = store.MaterialPrices
            .Where(item => item.MaterialId == id)
            .OrderByDescending(item => item.EffectiveAt)
            .Select(item => new RawMaterialPriceHistoryItem(
                item.EffectiveAt,
                item.PricePerPack,
                item.PricePerUnit,
                item.NetQuantity,
                item.NetUnit,
                item.Note))
            .Take(8)
            .ToArray();

        return new RawMaterialDetail(
            material,
            store.MaterialUsedInBom(id),
            store.MaterialUsedInProduction(id),
            bomProducts,
            priceHistory);
    }

    private RawMaterialListItem MapToListItem(RawMaterial material)
        => new(
            material.Id,
            material.Code ?? string.Empty,
            material.Name ?? string.Empty,
            material.Brand,
            material.BaseUnit ?? string.Empty,
            material.NetQuantity,
            material.NetUnit ?? string.Empty,
            material.PricePerPack,
            material.CostPerBaseUnit,
            material.UnitConversions?.Count ?? 0,
            material.Status,
            material.UpdatedAt,
            store.MaterialUsedInBom(material.Id),
            store.MaterialUsedInProduction(material.Id));

    private RawMaterialMutationResult? ValidateRequest(RawMaterialUpsertRequest request, Guid? existingId)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new RawMaterialMutationResult(false, "Nama material wajib diisi.");
        }

        if (!string.IsNullOrWhiteSpace(request.Code) && store.MaterialCodeExists(request.Code.Trim(), existingId))
        {
            return new RawMaterialMutationResult(false, "Kode material sudah dipakai.");
        }

        if (string.IsNullOrWhiteSpace(request.BaseUnit))
        {
            return new RawMaterialMutationResult(false, "Base unit wajib diisi.");
        }

        if (request.NetQuantity <= 0)
        {
            return new RawMaterialMutationResult(false, "Netto qty harus lebih besar dari 0.");
        }

        if (string.IsNullOrWhiteSpace(request.NetUnit))
        {
            return new RawMaterialMutationResult(false, "Netto unit wajib diisi.");
        }

        if (!MaterialUnitCatalog.AreCompatible(request.BaseUnit, request.NetUnit))
        {
            return new RawMaterialMutationResult(false, "Netto unit harus konsisten dengan base unit.");
        }

        if (request.PricePerPack <= 0)
        {
            return new RawMaterialMutationResult(false, "Harga per pack harus lebih besar dari 0.");
        }

        var conversionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.UnitConversions.Where(item => !string.IsNullOrWhiteSpace(item.UnitName)))
        {
            if (item.ConversionQuantity <= 0)
            {
                return new RawMaterialMutationResult(false, $"Konversi untuk unit {item.UnitName} harus lebih besar dari 0.");
            }

            if (!conversionNames.Add(item.UnitName.Trim()))
            {
                return new RawMaterialMutationResult(false, $"Unit konversi {item.UnitName} duplikat.");
            }
        }

        return null;
    }

    private static RawMaterialQuery Normalize(RawMaterialQuery query)
        => query with
        {
            Search = query.Search?.Trim(),
            SortBy = string.IsNullOrWhiteSpace(query.SortBy) ? "updated" : query.SortBy.Trim(),
            Page = Math.Max(1, query.Page),
            PageSize = Math.Clamp(query.PageSize, 5, 25)
        };

    private static IReadOnlyList<MaterialUnitConversion> NormalizeConversions(IEnumerable<RawMaterialUnitConversionInput> items)
        => items
            .Where(item => !string.IsNullOrWhiteSpace(item.UnitName) && item.ConversionQuantity > 0)
            .Select(item => new MaterialUnitConversion(item.UnitName.Trim(), item.ConversionQuantity))
            .ToArray();

    private RawMaterial? FindExistingMaterialForImport(RawMaterialImportPreviewRow row)
    {
        var exactMatches = store.RawMaterials
            .Where(material => GetImportIdentityKey(material, includeBrand: true) == GetImportIdentityKey(row, includeBrand: true))
            .ToArray();

        if (exactMatches.Length == 1)
        {
            return exactMatches[0];
        }

        if (exactMatches.Length > 1)
        {
            return exactMatches
                .OrderByDescending(item => item.UpdatedAt)
                .First();
        }

        var fallbackMatches = store.RawMaterials
            .Where(material =>
                (string.IsNullOrWhiteSpace(row.Brand) || string.IsNullOrWhiteSpace(material.Brand)) &&
                GetImportIdentityKey(material, includeBrand: false) == GetImportIdentityKey(row, includeBrand: false))
            .ToArray();

        return fallbackMatches.Length switch
        {
            1 => fallbackMatches[0],
            > 1 => fallbackMatches.OrderByDescending(item => item.UpdatedAt).First(),
            _ => null
        };
    }

    private static bool IsEquivalentImport(RawMaterial existing, RawMaterialImportPreviewRow row)
    {
        var existingName = NormalizeLookupText(existing.Name);
        var rowName = NormalizeLookupText(row.Name);
        var existingBrand = NormalizeLookupText(existing.Brand);
        var rowBrand = NormalizeLookupText(row.Brand);
        var existingBaseUnit = NormalizeUnitOrEmpty(existing.BaseUnit);
        var rowBaseUnit = NormalizeUnitOrEmpty(row.BaseUnit);
        var existingNetUnit = NormalizeUnitOrEmpty(existing.NetUnit);
        var rowNetUnit = NormalizeUnitOrEmpty(row.NetUnit);
        var existingNetQty = existing.NetQuantity;
        var rowNetQty = row.NetQuantity ?? 0m;
        var existingBaseQty = existing.NetQuantityInBaseUnit;
        var rowBaseQty = GetNetQuantityInBaseUnit(row);

        return existingName == rowName
            && existingBrand == rowBrand
            && existingBaseUnit == rowBaseUnit
            && existingNetUnit == rowNetUnit
            && existingNetQty == rowNetQty
            && existingBaseQty == rowBaseQty
            && existing.PricePerPack == (row.PricePerPack ?? 0m);
    }

    private static string NormalizeUnit(string unit) => MaterialUnitCatalog.NormalizeUnit(unit);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GetImportIdentityKey(RawMaterial material, bool includeBrand)
        => string.Join("|",
            NormalizeLookupText(material.Name),
            includeBrand ? NormalizeLookupText(material.Brand) : string.Empty,
            NormalizeUnitOrEmpty(material.BaseUnit),
            material.NetQuantityInBaseUnit.ToString("0.####", CultureInfo.InvariantCulture));

    private static string GetImportIdentityKey(RawMaterialImportPreviewRow row, bool includeBrand)
        => string.Join("|",
            NormalizeLookupText(row.Name),
            includeBrand ? NormalizeLookupText(row.Brand) : string.Empty,
            NormalizeUnitOrEmpty(row.BaseUnit),
            GetNetQuantityInBaseUnit(row).ToString("0.####", CultureInfo.InvariantCulture));

    private static decimal GetNetQuantityInBaseUnit(RawMaterialImportPreviewRow row)
    {
        if (row.NetQuantity is null || row.NetQuantity <= 0)
        {
            return 0m;
        }

        if (!MaterialUnitCatalog.AreCompatible(row.BaseUnit, row.NetUnit))
        {
            return 0m;
        }

        return MaterialUnitCatalog.Convert(row.NetQuantity.Value, row.NetUnit, row.BaseUnit);
    }

    private static string NormalizeLookupText(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static async Task<List<string[]>> ReadCsvAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        using var parser = new TextFieldParser(memory, Encoding.UTF8)
        {
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };
        parser.SetDelimiters(",");

        var rows = new List<string[]>();
        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(parser.ReadFields() ?? []);
        }

        return rows;
    }

    private static async Task<List<string[]>> ReadXlsxAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        using var workbook = new XLWorkbook(memory);
        var worksheet = workbook.Worksheets.First();
        var range = worksheet.RangeUsed();
        if (range is null)
        {
            return [];
        }

        return range.Rows()
            .Select(row => row.Cells().Select(cell => cell.GetValue<string>()).ToArray())
            .ToList();
    }

    private static RawMaterialImportPreviewRow MapImportRow(int rowNumber, IReadOnlyList<string> headers, IReadOnlyList<string> values)
    {
        var map = headers
            .Select((header, index) => new { Header = NormalizeHeader(header), Index = index })
            .Where(item => !string.IsNullOrWhiteSpace(item.Header))
            .ToDictionary(item => item.Header, item => item.Index, StringComparer.OrdinalIgnoreCase);

        string Read(string key)
        {
            if (!map.TryGetValue(key, out var index) || index >= values.Count)
            {
                return string.Empty;
            }

            return values[index]?.Trim() ?? string.Empty;
        }

        string ReadAny(params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = Read(key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        var name = ReadAny("nama_material", "nama_bahan", "material", "bahan");
        var brand = NormalizeOptional(ReadAny("merk", "merek", "merek_contoh", "brand"));
        var baseUnit = ReadAny("base_unit");
        var netUnit = ReadAny("net_unit");
        var packDescriptor = ReadAny("berat_per_pack", "netto", "isi_pack", "ukuran_pack");

        if (string.IsNullOrWhiteSpace(packDescriptor))
        {
            packDescriptor = ReadAny("berat_pack");
        }

        var parsedPackSize = ParsePackSize(packDescriptor);
        if (string.IsNullOrWhiteSpace(baseUnit))
        {
            baseUnit = parsedPackSize.BaseUnit;
        }

        if (string.IsNullOrWhiteSpace(netUnit))
        {
            netUnit = parsedPackSize.NetUnit;
        }

        var netQty = TryParseDecimal(ReadAny("net_qty"));
        if (netQty is null || netQty <= 0)
        {
            netQty = parsedPackSize.NetQuantity;
        }

        if (string.IsNullOrWhiteSpace(baseUnit))
        {
            baseUnit = netUnit;
        }

        var pricePerPack = TryParseDecimal(ReadAny("harga_per_pack", "estimasi_harga_(idr)", "estimasi_harga_idr", "harga", "price"));
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("Nama material wajib diisi.");
        }

        if (string.IsNullOrWhiteSpace(baseUnit))
        {
            errors.Add("Base unit wajib diisi.");
        }

        if (netQty is null || netQty <= 0)
        {
            errors.Add("Netto qty harus lebih besar dari 0.");
        }

        if (string.IsNullOrWhiteSpace(netUnit))
        {
            errors.Add("Netto unit wajib diisi.");
        }
        else if (!string.IsNullOrWhiteSpace(baseUnit) && !MaterialUnitCatalog.AreCompatible(baseUnit, netUnit))
        {
            errors.Add("Netto unit harus konsisten dengan base unit.");
        }

        if (pricePerPack is null || pricePerPack <= 0)
        {
            errors.Add("Harga per pack harus lebih besar dari 0.");
        }

        return new RawMaterialImportPreviewRow(
            rowNumber,
            name,
            brand,
            NormalizeUnitOrEmpty(baseUnit),
            netQty,
            NormalizeUnitOrEmpty(netUnit),
            pricePerPack,
            errors);
    }

    private static decimal? TryParseDecimal(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Replace("Rp", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("IDR", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Â", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant))
        {
            return invariant;
        }

        return decimal.TryParse(raw, NumberStyles.Number, IdCulture, out var local) ? local : null;
    }

    private static string NormalizeHeader(string header)
        => header.Trim().ToLowerInvariant().Replace(" ", "_");

    private static string NormalizeUnitOrEmpty(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : MaterialUnitCatalog.NormalizeUnit(value);

    private static ParsedPackSize ParsePackSize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ParsedPackSize.Empty;
        }

        var normalized = raw.Trim()
            .Replace("Â", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(",", ".", StringComparison.OrdinalIgnoreCase);

        var parentheticalMatches = Regex.Matches(normalized, @"\(([^)]*)\)");
        foreach (Match match in parentheticalMatches)
        {
            var nested = ParsePackSize(match.Groups[1].Value);
            if (nested.IsValid)
            {
                return nested;
            }
        }

        var multiplied = Regex.Match(
            normalized,
            @"(?<qty>\d+(?:\.\d+)?)\s*(?<unit>kg|gr|gram|g|liter|litre|l|ml|pcs|pc|piece|pieces|lembar|btr|butir|botol|sachet|tray|pack|pouch|cup|jar|kaleng|box|kotak|blok|sheet|roll)\s*[xX]\s*(?<mult>\d+(?:\.\d+)?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (multiplied.Success)
        {
            var multipliedQty = TryParseDecimal(multiplied.Groups["qty"].Value);
            var mult = TryParseDecimal(multiplied.Groups["mult"].Value);
            var multipliedUnit = NormalizeImportUnit(multiplied.Groups["unit"].Value);

            if (multipliedQty is > 0 && mult is > 0 && !string.IsNullOrWhiteSpace(multipliedUnit))
            {
                var multipliedBaseUnit = MaterialUnitCatalog.GetFamily(multipliedUnit) switch
                {
                    "mass" => "gr",
                    "volume" => "ml",
                    "count" => "pcs",
                    _ => multipliedUnit
                };

                return new ParsedPackSize(multipliedQty.Value * mult.Value, multipliedUnit, multipliedBaseUnit);
            }
        }

        var direct = Regex.Match(
            normalized,
            @"(?<qty>\d+(?:\.\d+)?)\s*(?<unit>kg|gr|gram|g|liter|litre|l|ml|pcs|pc|piece|pieces|lembar|btr|butir|botol|sachet|tray|pack|pouch|cup|jar|kaleng|box|kotak|blok|sheet|roll)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!direct.Success)
        {
            return ParsedPackSize.Empty;
        }

        var qty = TryParseDecimal(direct.Groups["qty"].Value);
        if (qty is null || qty <= 0)
        {
            return ParsedPackSize.Empty;
        }

        var unit = NormalizeImportUnit(direct.Groups["unit"].Value);
        if (string.IsNullOrWhiteSpace(unit))
        {
            return ParsedPackSize.Empty;
        }

        var baseUnit = MaterialUnitCatalog.GetFamily(unit) switch
        {
            "mass" => "gr",
            "volume" => "ml",
            "count" => "pcs",
            _ => unit
        };

        return new ParsedPackSize(qty.Value, unit, baseUnit);
    }

    private static string NormalizeImportUnit(string rawUnit)
    {
        var unit = rawUnit.Trim().ToLowerInvariant();
        return unit switch
        {
            "g" => "gr",
            "gram" => "gr",
            "liter" => "l",
            "litre" => "l",
            "pc" => "pcs",
            "piece" => "pcs",
            "pieces" => "pcs",
            "lembar" => "pcs",
            "btr" => "pcs",
            "butir" => "pcs",
            "botol" => "pcs",
            "sachet" => "pcs",
            "tray" => "pcs",
            "pack" => "pcs",
            "pouch" => "pcs",
            "cup" => "pcs",
            "jar" => "pcs",
            "kaleng" => "pcs",
            "box" => "pcs",
            "kotak" => "pcs",
            "blok" => "pcs",
            "sheet" => "pcs",
            "roll" => "pcs",
            _ => MaterialUnitCatalog.NormalizeUnit(unit)
        };
    }

    private static string Csv(params object?[] values)
        => string.Join(",", values.Select(value =>
        {
            var text = value switch
            {
                decimal number => number.ToString("0.####", CultureInfo.InvariantCulture),
                _ => value?.ToString() ?? string.Empty
            };

            return $"\"{text.Replace("\"", "\"\"")}\"";
        }));

    private readonly record struct ParsedPackSize(decimal? NetQuantity, string NetUnit, string BaseUnit)
    {
        public bool IsValid => NetQuantity is > 0 && !string.IsNullOrWhiteSpace(NetUnit) && !string.IsNullOrWhiteSpace(BaseUnit);

        public static ParsedPackSize Empty => new(null, string.Empty, string.Empty);
    }
}
