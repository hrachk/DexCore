using Dex.BlazorUI;
using Dex.BlazorUI.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Local static files
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// API Gateway client (orders)
builder.Services.AddHttpClient<OrderApiService>(client =>
{
    client.BaseAddress = new Uri("http://localhost:5080");
    client.Timeout = TimeSpan.FromSeconds(8);
});

builder.Services.AddSingleton<LiveMarketDataService>(sp =>
{
    var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
    http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "DexCore-Terminal/1.0");
    var log = sp.GetRequiredService<ILogger<LiveMarketDataService>>();
    return new LiveMarketDataService(http, log);
});
builder.Services.AddSingleton<TradingPortfolioService>();
builder.Services.AddScoped<MetamaskInteropService>();

await builder.Build().RunAsync();
