namespace SatRadioProxy

#nowarn "20"

open System
open System.Collections.Generic
open System.IO
open System.Linq
open System.Threading.Tasks
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.HttpsPolicy
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

module Program =
    let exitCode = 0

    [<EntryPoint>]
    let main args =
        let builder = WebApplication.CreateBuilder(args)

        builder.Services
            .AddControllersWithViews()
            .AddRazorRuntimeCompilation()

        builder.Services
            .AddHttpClient()

        let app = builder.Build()

        let hostApplicationLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>()

        hostApplicationLifetime.ApplicationStarted.Register(fun () -> ignore (task {
            try
                do! NetworkInterfaceProvider.updateAddressAsync ()
                do! SiriusXMChannelProvider.refreshChannelsAsync ()
                LyrionManager.refreshFavorites ()
                SiriusXMPythonScriptManager.start ()
            with exn ->
                sprintf "%O" exn |> stderr.WriteLine
        }))

        hostApplicationLifetime.ApplicationStopped.Register(fun () ->
            SiriusXMPythonScriptManager.stop ()
        )

        app.UseStaticFiles()
        app.MapControllerRoute(name = "default", pattern = "{controller=Home}/{action=Index}/{id?}")
        app.Run("http://+:5000")

        exitCode
