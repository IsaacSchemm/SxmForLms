using SatRadioProxy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

builder.Services.AddHostedService<SiriusXMLyrionFavoritesService>();

var app = builder.Build();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Radio}/{action=Index}/{id?}");

app.Run($"http://+:{NetworkInterfaceProvider.port}");
