using SatRadioProxy;
using SatRadioProxy.Lyrion;
using SatRadioProxy.SiriusXM;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

var app = builder.Build();

var hostApplicationLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

hostApplicationLifetime.ApplicationStarted.Register(async () =>
{
    try
    {
        await SiriusXMClientManager.refresh_channels();
        LyrionFavoritesManager.refresh_favorites();
        await NetworkInterfaceProvider.updateAddress();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
    }
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run("http://+:5000");
