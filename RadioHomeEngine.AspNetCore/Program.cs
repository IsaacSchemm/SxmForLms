using RadioHomeEngine;
using RadioHomeEngine.TemporaryMountPoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

builder.Services.AddHostedService<LyrionCLIService>();
builder.Services.AddHostedService<WeatherService>();
builder.Services.AddHostedService<LyrionPlayerDetectionService>();

var app = builder.Build();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run($"http://+:{Config.port}");

EstablishedMountPoint.UnmountAll();
