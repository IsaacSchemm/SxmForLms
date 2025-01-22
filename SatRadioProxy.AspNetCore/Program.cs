using SatRadioProxy;
using SatRadioProxy.Lyrion;
using SatRadioProxy.SiriusXM;
using SatRadioProxy.Streaming;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

builder.Services.AddSingleton<Proxy>();

var app = builder.Build();

var hostApplicationLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

hostApplicationLifetime.ApplicationStarted.Register(async () =>
{
    try
    {
        await NetworkInterfaceProvider.updateAddressAsync();
        await SiriusXMChannelProvider.refreshChannelsAsync();
        LyrionFavoritesManager.refreshFavorites();
        SiriusXMPythonScriptManager.start();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
    }
});

hostApplicationLifetime.ApplicationStopped.Register(() =>
{
    try
    {
        SiriusXMPythonScriptManager.stop();
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
