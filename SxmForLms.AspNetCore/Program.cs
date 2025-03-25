using SxmForLms;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

builder.Services.AddHostedService<LyrionCLI.Service>();
builder.Services.AddHostedService<FavoritesManager.Service>();
builder.Services.AddHostedService<LyrionIRHandler.Service>();
builder.Services.AddHostedService<WeatherManager.Service>();

var app = builder.Build();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Radio}/{action=Index}/{id?}");

app.Run($"http://+:{Config.port}");
