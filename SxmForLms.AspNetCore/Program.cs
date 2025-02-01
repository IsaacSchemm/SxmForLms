using SxmForLms;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

builder.Services.AddHostedService<LyrionCLI.Service>();
builder.Services.AddHostedService<SiriusXMFavorites.Service>();

var app = builder.Build();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Radio}/{action=Index}/{id?}");

app.Run($"http://+:{Config.port}");
