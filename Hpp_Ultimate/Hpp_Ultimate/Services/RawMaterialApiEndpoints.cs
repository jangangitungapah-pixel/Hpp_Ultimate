using System.Text;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public static class RawMaterialApiEndpoints
{
    public static IEndpointRouteBuilder MapRawMaterialApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/bahan-baku")
            .RequireAuthenticatedSession();

        group.MapGet("", async (
            string? search,
            MaterialStatus? status,
            string? sortBy,
            bool? desc,
            int? page,
            int? pageSize,
            RawMaterialCatalogService service,
            CancellationToken cancellationToken) =>
        {
            var query = new RawMaterialQuery(search, status, sortBy ?? "updated", desc ?? true, page ?? 1, pageSize ?? 10);
            return Results.Ok(await service.QueryAsync(query, cancellationToken));
        });

        group.MapGet("/{id:guid}", async (Guid id, RawMaterialCatalogService service, CancellationToken cancellationToken) =>
        {
            var detail = await service.GetDetailAsync(id, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        group.MapPost("", async (RawMaterialUpsertRequest request, RawMaterialCatalogService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        group.MapPut("/{id:guid}", async (Guid id, RawMaterialUpsertRequest request, RawMaterialCatalogService service, CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(id, request, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        group.MapDelete("/{id:guid}", async (Guid id, RawMaterialCatalogService service, CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(id, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        group.MapPost("/import-preview", async (IFormFile file, RawMaterialCatalogService service, CancellationToken cancellationToken) =>
        {
            if (file.Length == 0)
            {
                return Results.BadRequest(new { message = "File import kosong." });
            }

            await using var stream = file.OpenReadStream();
            var result = await service.PreviewImportAsync(file.FileName, stream, cancellationToken);
            return Results.Ok(result);
        })
        .DisableAntiforgery();

        group.MapPost("/import-commit", async (RawMaterialImportCommitRequest request, RawMaterialCatalogService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CommitImportAsync(request, cancellationToken);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        endpoints.MapGet("/api/bahan-baku-next-code", async (RawMaterialCatalogService service, CancellationToken cancellationToken) =>
            Results.Ok(new { code = await service.GenerateNextCodeAsync(cancellationToken) }))
            .RequireAuthenticatedSession();

        endpoints.MapGet("/api/bahan-baku-template", () =>
            Results.File(Encoding.UTF8.GetBytes(RawMaterialCatalogService.GetImportTemplateCsv()), "text/csv", "template-katalog-material.csv"))
            .RequireAuthenticatedSession();

        endpoints.MapGet("/api/bahan-baku-export", async (RawMaterialCatalogService service, CancellationToken cancellationToken) =>
        {
            var csv = await service.ExportCsvAsync(cancellationToken);
            return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv", $"katalog-material-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        })
        .RequireAuthenticatedSession();

        return endpoints;
    }
}
