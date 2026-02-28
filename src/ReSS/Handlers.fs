module ReSS.Handlers

open System
open System.Diagnostics
open System.Collections.Generic
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.Logging
open Giraffe
open ReSS.Domain.Types
open ReSS.Domain.UrlCodec
open ReSS.Domain.UrlGuard
open ReSS.Domain.DripCalculator
open ReSS.Domain.FeedFetcher
open ReSS.Domain.FeedBuilder
open ReSS.Views
open ReSS.Telemetry

// ---- helpers ----

/// Wrap a synchronous computation in a child Activity span.
/// Marks the span as Error (with the supplied message) when `isError` is true.
let inline private withSpan
    (name: string)
    (isError: 'a -> bool)
    (errorMsg: 'a -> string)
    (body: unit -> 'a)
    : 'a =
    use activity = activitySource.StartActivity(name)
    let result = body ()
    if activity <> null && isError result then
        activity.SetStatus(ActivityStatusCode.Error, errorMsg result) |> ignore
    result

/// Wrap an Async computation in a child Activity span.
let inline private withSpanAsync
    (name: string)
    (isError: 'a -> bool)
    (errorMsg: 'a -> string)
    (body: unit -> Async<'a>)
    : Async<'a> =
    async {
        use activity = activitySource.StartActivity(name)
        let! result = body ()
        if activity <> null && isError result then
            activity.SetStatus(ActivityStatusCode.Error, errorMsg result) |> ignore
        return result
    }

let private isResultError r = match r with | Error _ -> true | Ok _ -> false
let private resultErrMsg r  = match r with | Error e -> sprintf "%A" e | Ok _ -> ""

// ---- GET / ----

let getIndexHandler : HttpHandler =
    htmlView (formView Empty)

// ---- POST / ----

let postIndexHandler : HttpHandler =
    fun next ctx ->
        task {
            let! form = ctx.Request.ReadFormAsync()

            let rawUrl    = (form.["sourceUrl"].ToString()).Trim()
            let rawPerDay = (form.["perDay"].ToString()).Trim()
            let rawDate   = (form.["startDate"].ToString()).Trim()

            // Validate fields
            let mutable errors = Map.empty<string, string>

            if String.IsNullOrWhiteSpace(rawUrl) then
                errors <- errors |> Map.add "sourceUrl" "Please enter a feed URL."

            let perDay =
                match Int32.TryParse(rawPerDay) with
                | true, n when n > 0 -> Some (n * 1<articles/day>)
                | _ ->
                    errors <- errors |> Map.add "perDay" "Please enter a positive number."
                    None

            let startDate =
                if String.IsNullOrWhiteSpace(rawDate) then
                    Some (DateOnly.FromDateTime(DateTime.Today))
                else
                    match DateOnly.TryParse(rawDate) with
                    | true, d -> Some d
                    | _ ->
                        errors <- errors |> Map.add "startDate" "Please enter a valid date."
                        None

            if not errors.IsEmpty then
                return! htmlView (formView (FormError (rawUrl, rawPerDay, errors))) next ctx
            else
                let client = ctx.RequestServices.GetRequiredService<Net.Http.HttpClient>()
                let cache  = ctx.RequestServices.GetRequiredService<IMemoryCache>()
                let clock  = ctx.RequestServices.GetRequiredService<Clock>()

                // --- 6.1 child span: URL guard ---
                let! guardResult =
                    withSpanAsync "ress.url_guard" isResultError resultErrMsg
                        (fun () -> validateUrl rawUrl)
                    |> Async.StartAsTask

                match guardResult with
                | Error guardErr ->
                    let msg =
                        match guardErr with
                        | MalformedUrl -> "The URL is not valid."
                        | NonHttpScheme -> "Only http:// and https:// URLs are allowed."
                        | PrivateOrLoopbackAddress _ -> "That URL resolves to a private or loopback address."
                    let errs = Map.ofList ["form", msg]
                    return! htmlView (formView (FormError (rawUrl, rawPerDay, errs))) next ctx
                | Ok uri ->
                    let logger = ctx.RequestServices.GetService<ILogger<obj>>()
                    let loggerOpt = if logger = null then None else Some (logger :> ILogger)

                    // --- 6.2 child span: feed fetch ---
                    let! fetchResult =
                        withSpanAsync "ress.feed_fetch" isResultError resultErrMsg
                            (fun () -> fetchFeed client cache uri loggerOpt)
                        |> Async.StartAsTask

                    match fetchResult with
                    | Error fetchErr ->
                        let msg =
                            match fetchErr with
                            | UnreachableUrl -> "Could not reach that URL."
                            | HttpError code -> sprintf "The feed URL returned HTTP %d." code
                            | NotXml -> "The URL does not point to an RSS feed."
                            | ParseFailure m -> sprintf "Could not parse the RSS feed: %s" m
                        let errs = Map.ofList ["form", msg]
                        return! htmlView (formView (FormError (rawUrl, rawPerDay, errs))) next ctx
                    | Ok feed ->
                        let total = feed.Items |> Seq.length
                        let pd    = perDay.Value
                        let sd    = startDate.Value

                        // --- 6.3 child span: drip calculate ---
                        let dripResult =
                            withSpan "ress.drip_calculate" (fun _ -> false) (fun _ -> "")
                                (fun () -> calculate clock sd pd (total * 1<articles>))

                        let unlocked =
                            match dripResult with
                            | ShowItems n -> int n
                            | RedirectToSource -> total

                        let blob         = encode { SourceUrl = uri; PerDay = pd; StartDate = sd }
                        let baseUrl      = sprintf "%s://%s" ctx.Request.Scheme ctx.Request.Host.Value
                        let generatedUrl = sprintf "%s/feed/%s" baseUrl blob

                        // --- 4.1 metric: feed URL created (FR-20) ---
                        feedUrlsCreated.Add(1)

                        return! htmlView (formView (Success (generatedUrl, unlocked, total))) next ctx
        }

// ---- GET /feed/{blob} ----

let getFeedHandler (blob: string) : HttpHandler =
    fun next ctx ->
        task {
            // --- 5.1 metric: feed request (FR-21) ---
            feedRequests.Add(1)

            // --- 5.3 child span: URL decode ---
            let decodeResult =
                withSpan "ress.url_decode" isResultError resultErrMsg
                    (fun () -> decode blob)

            match decodeResult with
            | Error decodeErr ->
                let logger = ctx.RequestServices.GetService<ILogger<obj>>()
                if logger <> null then
                    (logger :> ILogger).LogWarning("Decode error for blob {blob}: {error}", blob, decodeErr)
                return! RequestErrors.badRequest (text "Invalid feed URL.") next ctx
            | Ok fp ->
                // --- 5.2 metric: per-source-URL request (FR-22) ---
                feedSourceUrlRequests.Add(1, KeyValuePair<string, obj>("source_url", fp.SourceUrl.AbsoluteUri))

                // --- 5.4 child span: URL guard ---
                let! guardResult =
                    withSpanAsync "ress.url_guard" isResultError resultErrMsg
                        (fun () -> validateUrl fp.SourceUrl.AbsoluteUri)
                    |> Async.StartAsTask

                match guardResult with
                | Error guardErr ->
                    let logger = ctx.RequestServices.GetService<ILogger<obj>>()
                    if logger <> null then
                        (logger :> ILogger).LogWarning("UrlGuard rejected {url}: {error}", fp.SourceUrl, guardErr)
                    return! RequestErrors.badRequest (text "Feed URL is not allowed.") next ctx
                | Ok _ ->
                    let client = ctx.RequestServices.GetRequiredService<Net.Http.HttpClient>()
                    let cache  = ctx.RequestServices.GetRequiredService<IMemoryCache>()
                    let clock  = ctx.RequestServices.GetRequiredService<Clock>()
                    let logger = ctx.RequestServices.GetService<ILogger<obj>>()
                    let loggerOpt = if logger = null then None else Some (logger :> ILogger)

                    // --- 5.5 child span: feed fetch ---
                    let! fetchResult =
                        withSpanAsync "ress.feed_fetch" isResultError resultErrMsg
                            (fun () -> fetchFeed client cache fp.SourceUrl loggerOpt)
                        |> Async.StartAsTask

                    match fetchResult with
                    | Error _ ->
                        return! ServerErrors.badGateway (text "Could not fetch upstream feed.") next ctx
                    | Ok feed ->
                        let allItems = feed.Items |> Seq.toList
                        let total    = allItems.Length

                        // --- 5.6 child span: drip calculate ---
                        let dripResult =
                            withSpan "ress.drip_calculate" (fun _ -> false) (fun _ -> "")
                                (fun () -> calculate clock fp.StartDate fp.PerDay (total * 1<articles>))

                        match dripResult with
                        | RedirectToSource ->
                            return! redirectTo true fp.SourceUrl.AbsoluteUri next ctx
                        | ShowItems n ->
                            let slice = allItems |> List.sortBy (fun i -> i.PublishDate) |> List.truncate (int n)

                            // --- 5.7 child span: feed build ---
                            let xml =
                                withSpan "ress.feed_build" (fun _ -> false) (fun _ -> "")
                                    (fun () -> buildFeed feed slice (int n) total)

                            return! (setHttpHeader "Content-Type" "application/rss+xml; charset=utf-8"
                                     >=> setBodyFromString xml) next ctx
        }
