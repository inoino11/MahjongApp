using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MahjongApp;
using MudBlazor.Services;
using Blazored.LocalStorage;
using MudBlazor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomCenter;
    config.SnackbarConfiguration.PreventDuplicates = false;
    config.SnackbarConfiguration.NewestOnTop = true;
    config.SnackbarConfiguration.ShowCloseIcon = false;         // ×ボタン撤廃
    config.SnackbarConfiguration.VisibleStateDuration = 1500;   // 生存時間
    config.SnackbarConfiguration.HideTransitionDuration = 200;
    config.SnackbarConfiguration.ShowTransitionDuration = 200;
});

builder.Services.AddBlazoredLocalStorage();
builder.Services.AddScoped<MahjongApp.Services.DatabaseService>();
builder.Services.AddScoped<MahjongApp.Services.StatsCacheService>();
builder.Services.AddScoped<MahjongApp.Services.SessionStateService>();
builder.Services.AddScoped<MahjongApp.Services.MahjongCalculatorService>();

await builder.Build().RunAsync();