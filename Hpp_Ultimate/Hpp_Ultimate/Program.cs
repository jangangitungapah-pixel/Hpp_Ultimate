using Hpp_Ultimate.Client.Pages;
using Hpp_Ultimate.Components;
using Hpp_Ultimate.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(sp =>
{
    var environment = sp.GetRequiredService<IHostEnvironment>();
    var databasePath = Path.Combine(environment.ContentRootPath, "App_Data", "hpp-ultimate.db");
    return new SeededBusinessDataStore(databasePath);
});
builder.Services.AddSingleton<IBusinessDataStore>(sp => sp.GetRequiredService<SeededBusinessDataStore>());
builder.Services.AddScoped<DashboardState>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<ProductCatalogService>();
builder.Services.AddScoped<RawMaterialCatalogService>();
builder.Services.AddScoped<BomCatalogService>();
builder.Services.AddScoped<ProductionCostService>();
builder.Services.AddScoped<HppCalculatorService>();
builder.Services.AddScoped<SellingPriceService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<ProductionHistoryService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<AuthService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapDashboardApi();
app.MapProductApi();
app.MapRawMaterialApi();
app.MapBomApi();
app.MapProductionCostApi();
app.MapHppCalculatorApi();
app.MapSellingPriceApi();
app.MapReportApi();
app.MapProductionHistoryApi();
app.MapSettingsApi();
app.MapAuthApi();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Hpp_Ultimate.Client._Imports).Assembly);

app.Run();
