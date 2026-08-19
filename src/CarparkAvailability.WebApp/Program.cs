using CarparkAvailability.WebApp.Components;
using CarparkAvailability.WebApp;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpClient<ParkingApiClient>(client =>
{
    client.BaseAddress = new Uri("https+http://apiapp");
});

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();

app.Run();
