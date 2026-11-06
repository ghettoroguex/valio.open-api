using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using MudBlazor.Services;
using ValIO.Application.DependencyInjection;
using ValIO.Localization.DependencyInjection;
using ValIO.Output.DependencyInjection;
using ValIO.Plugins.Bootstrap;
using ValIO.Web.Components;
using ValIO.Web.Endpoints;
using ValIO.Web.Services;
using ValIO.Web.State;

var builder = WebApplication.CreateBuilder(args);

// Blazor + UI
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();

// HTTP and Core
builder.Services.AddHttpClient();
builder.Services.AddValIOCore();
builder.Services.AddValIOLocalization();
builder.Services.AddValIOOutputs();
builder.Services.AddValIOApplication();
builder.Services.AddAllGeneratorPlugins();

// Web-specific
builder.Services.AddScoped<GenerationFormState>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddSingleton<ISafeSchemaFetcher, SafeSchemaFetcher>();

// Localization for Web UI
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");

var supportedCultureNames = new[] { "en", "ru", "de", "zh-CN" };
var supportedCultures = supportedCultureNames.Select(c => new CultureInfo(c)).ToArray();
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.DefaultRequestCulture = new RequestCulture("en");
    o.SupportedCultures = supportedCultures;
    o.SupportedUICultures = supportedCultures;
});

builder.Services.AddHealthChecks();

// Trust forwarded request information only from explicitly configured proxies.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    var proxies = builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
    foreach (var proxy in proxies.Where(value => !string.IsNullOrWhiteSpace(value)))
    {
        options.KnownProxies.Add(IPAddress.Parse(proxy));
    }
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRequestLocalization();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// =========================================================================
// Public REST API
// =========================================================================
var api = app.MapGroup("/api").WithTags("ValIO");

api.MapGet("/languages", LanguagesEndpoint.Handle).WithName("ListLanguages");
api.MapGet("/options/{language}", OptionsEndpoint.Handle).WithName("DescribeOptions");
api.MapPost("/validate", ValidateEndpoint.HandleAsync).DisableAntiforgery().WithName("ValidateSchema");
api.MapPost("/generate", GenerateEndpoint.HandleAsync).DisableAntiforgery().WithName("GenerateCode");
api.MapGet("/spec", ApiSpecEndpoint.Handle).WithName("ApiSpec");

// =========================================================================
// Infra: health, culture cookie
// =========================================================================
app.MapHealthChecks("/healthz").AllowAnonymous();
app.MapGet("/culture/set", CultureEndpoint.SetCulture).WithName("SetCulture");

app.Run();

/// <summary>Exposes the web application entry point to integration test hosts.</summary>
public partial class Program { }
