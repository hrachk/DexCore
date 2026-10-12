using Dex.BlazorUI;
using Dex.BlazorUI.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

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
