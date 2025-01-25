using SatRadioProxy;
using SatRadioProxy.SiriusXM;

await SiriusXMChannelCache.getChannelsAsync(CancellationToken.None);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

var app = builder.Build();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run($"http://+:{NetworkInterfaceProvider.port}");
