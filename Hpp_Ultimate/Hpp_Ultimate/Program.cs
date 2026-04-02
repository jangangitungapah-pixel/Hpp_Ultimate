using Hpp_Ultimate.Components;
using Hpp_Ultimate.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(sp =>
{
    var environment = sp.GetRequiredService<IHostEnvironment>();
    var databasePath = Path.Combine(environment.ContentRootPath, "App_Data", "hpp-ultimate.db");
    return new SeededBusinessDataStore(databasePath);
});
builder.Services.AddScoped<RawMaterialCatalogService>();
builder.Services.AddScoped<RecipeCatalogService>();
builder.Services.AddScoped<HppCalculatorService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<AuthService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
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
