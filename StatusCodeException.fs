namespace SatRadioProxy

open System.Net

exception StatusCodeException of code: HttpStatusCode
