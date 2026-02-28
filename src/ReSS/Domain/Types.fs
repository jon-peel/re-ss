module ReSS.Domain.Types

open System
open System.ServiceModel.Syndication

[<Measure>] type articles
[<Measure>] type day

type Clock = unit -> DateOnly

type FeedParams = {
    SourceUrl : Uri
    PerDay    : int<articles/day>
    StartDate : DateOnly
}

type DecodeError =
    | InvalidBase64
    | MalformedSegments
    | UnsupportedVersion of string
    | InvalidUrl
    | InvalidPerDay
    | InvalidDate

type UrlGuardError =
    | MalformedUrl
    | NonHttpScheme
    | PrivateOrLoopbackAddress of string

type FetchError =
    | UnreachableUrl
    | HttpError of int
    | NotXml
    | ParseFailure of string

type DripResult =
    | ShowItems of int<articles>
    | RedirectToSource

module Result =
    /// Lifts a BCL TryParse-style (bool * 'a) tuple into a Result.
    let ofTryParse error (success, value) =
        if success then Ok value else Error error

    /// Turns a boolean condition into a Result, failing with the given error if false.
    let require error condition =
        if condition then Ok () else Error error
