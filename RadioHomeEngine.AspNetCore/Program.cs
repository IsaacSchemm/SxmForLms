using RadioHomeEngine;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

builder.Services.AddHostedService<LyrionCLI.Service>();
builder.Services.AddHostedService<LyrionKnownPlayers.Service>();
builder.Services.AddHostedService<WeatherManager.Service>();
builder.Services.AddHostedService<LyrionIRHandler.Service>();

var app = builder.Build();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run($"http://+:{Config.port}");
