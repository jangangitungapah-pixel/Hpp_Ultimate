using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualBasic.FileIO;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class RawMaterialCatalogService(IMemoryCache cache, SeededBusinessDataStore store)
{
    private static readonly CultureInfo IdCulture = new("id-ID");

    public static IReadOnlyList<MaterialUnitOption> BaseUnits => MaterialUnitCatalog.BaseUnits;

    public static IReadOnlyList<MaterialUnitOption> GetCompatibleUnits(string baseUnit)
        => MaterialUnitCatalog.GetCompatibleUnits(baseUnit);

    public async Task<RawMaterialQueryResult> QueryAsync(RawMaterialQuery query, CancellationToken cancellationToken = default)
    {
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
        => Task.FromResult(BuildDetail(id));

    public Task<string> GenerateNextCodeAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(store.GenerateNextMaterialCode());

    public Task<RawMaterialMutationResult> CreateAsync(RawMaterialUpsertRequest request, CancellationToken cancellationToken = default)
    {
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
        return Task.FromResult(new RawMaterialMutationResult(true, "Material berhasil ditambahkan.", material));
    }

    public Task<RawMaterialMutationResult> UpdateAsync(Guid id, RawMaterialUpsertRequest request, CancellationToken cancellationToken = default)
    {
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
        return Task.FromResult(new RawMaterialMutationResult(true, "Material berhasil diperbarui.", updated));
    }

    public Task<RawMaterialMutationResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
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
            return Task.FromResult(new RawMaterialMutationResult(true, "Material dipindahkan ke status nonaktif karena sudah dipakai modul lain.", updated, usedInBom, usedInProduction));
        }

        store.RemoveRawMaterial(id);
        return Task.FromResult(new RawMaterialMutationResult(true, "Material berhasil dihapus.", existing, WasDeleted: true));
    }

    public async Task<RawMaterialImportPreviewResult> PreviewImportAsync(string fileName, Stream fileStream, CancellationToken cancellationToken = default)
    {
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
        if (!request.SkipInvalidRows && request.Rows.Any(item => !item.IsValid))
        {
            var blockingErrors = request.Rows
                .Where(item => !item.IsValid)
                .Take(5)
                .Select(item => $"Baris {item.RowNumber}: {string.Join(" ", item.Errors)}")
                .ToArray();

            return new RawMaterialImportResult(false, "Import dibatalkan karena masih ada row yang invalid.", 0, request.Rows.Count(item => !item.IsValid), blockingErrors);
        }

        var errors = new List<string>();
        var imported = 0;
        var skipped = 0;

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
            }, cancellationToken);

            if (createResult.Success)
            {
                imported++;
                continue;
            }

            skipped++;
            errors.Add($"Baris {row.RowNumber}: {createResult.Message}");
        }

        var success = request.SkipInvalidRows || errors.Count == 0;
        var message = imported == 0
            ? "Tidak ada material yang berhasil diimport."
            : $"{imported} material berhasil diimport.";

        return new RawMaterialImportResult(success, message, imported, skipped, errors);
    }

    public async Task<string> ExportCsvAsync(CancellationToken cancellationToken = default)
    {
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

        var bomProducts = store.BomItems
            .Where(item => item.MaterialId == id)
            .Select(item => store.Products.FirstOrDefault(product => product.Id == item.ProductId)?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
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
            material.Code,
            material.Name,
            material.Brand,
            material.BaseUnit,
            material.NetQuantity,
            material.NetUnit,
            material.PricePerPack,
            material.CostPerBaseUnit,
            material.UnitConversions.Count,
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

    private static string NormalizeUnit(string unit) => MaterialUnitCatalog.NormalizeUnit(unit);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

        var name = Read("nama_material");
        var brand = NormalizeOptional(Read("merk"));
        var baseUnit = Read("base_unit");
        var netUnit = Read("net_unit");

        if (string.IsNullOrWhiteSpace(baseUnit))
        {
            baseUnit = netUnit;
        }

        var netQty = TryParseDecimal(Read("net_qty"));
        var pricePerPack = TryParseDecimal(Read("harga_per_pack"));
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
}
