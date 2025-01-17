namespace SatRadioProxy

open System.Threading.Tasks

open Microsoft.AspNetCore.Diagnostics

type StatusCodeExceptionHandler() =
    interface IExceptionHandler with
        member _.TryHandleAsync (httpContext, exn, _) = ValueTask<bool> (task {
            return
                match exn with
                | :? StatusCodeException as ex ->
                    httpContext.Response.StatusCode <- int ex.code
                    true
                | _ ->
                    false
        })
