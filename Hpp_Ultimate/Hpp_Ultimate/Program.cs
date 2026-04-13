using Hpp_Ultimate.Components;
using Hpp_Ultimate.Services;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(sp =>
{
    var environment = sp.GetRequiredService<IHostEnvironment>();
    var configuration = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<SeededBusinessDataStore>>();
    var sqlitePath = Path.Combine(environment.ContentRootPath, "App_Data", "hpp-ultimate.db");
    var postgresConnectionString = configuration.GetConnectionString("Postgres")
        ?? configuration["DATABASE_URL"];
    var options = SeededBusinessDataStoreOptions.Create(sqlitePath, postgresConnectionString);
    return new SeededBusinessDataStore(options, logger);
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddScoped<RawMaterialCatalogService>();
builder.Services.AddHttpClient<GeminiMaterialAuditService>();
builder.Services.AddScoped<WarehouseService>();
builder.Services.AddScoped<ProductionService>();
builder.Services.AddHttpClient<GeminiProductionRecommendationService>();
builder.Services.AddScoped<RecipeCatalogService>();
builder.Services.AddHttpClient<GeminiRecipeDraftService>();
builder.Services.AddHttpClient<GeminiRecipeWeightEstimatorService>();
builder.Services.AddScoped<HppCalculatorService>();
builder.Services.AddScoped<ShoppingService>();
builder.Services.AddHttpClient<GeminiReceiptParserService>();
builder.Services.AddScoped<SalesService>();
builder.Services.AddHttpClient<GeminiPosOrderParserService>();
builder.Services.AddScoped<BookkeepingService>();
builder.Services.AddHttpClient<GeminiBookkeepingAssistantService>();
builder.Services.AddScoped<DataOpsService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<WorkspaceAccessService>();
builder.Services.AddScoped<AuditTrailService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRawMaterialApi();
app.MapSettingsApi();
app.MapAuthApi();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
