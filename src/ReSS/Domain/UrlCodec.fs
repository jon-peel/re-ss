module ReSS.Domain.UrlCodec

open System
open System.Web
open FSharpPlus
open ReSS.Domain.Types

// ---- helpers ----

let private toBase64Url (bytes: byte[]) =
    Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

let private fromBase64Url (s: string) : Result<byte[], unit> =
    let padded = s.Replace('-', '+').Replace('_', '/')
    let pad = (4 - padded.Length % 4) % 4
    let padded = padded + String('=', pad)
    try Ok (Convert.FromBase64String(padded))
    with _ -> Error ()

// ---- public API ----

let encode (p: FeedParams) : string =
    let payload =
        sprintf "v1::%s::%d::%s"
            (HttpUtility.UrlEncode(p.SourceUrl.AbsoluteUri))
            (int p.PerDay)
            (p.StartDate.ToString("yyyy-MM-dd"))
    toBase64Url (Text.Encoding.UTF8.GetBytes(payload))

let decode (blob: string) : Result<FeedParams, DecodeError> =
    monad {
        let! bytes     = fromBase64Url blob |> Result.mapError (fun () -> InvalidBase64)
        let text       = Text.Encoding.UTF8.GetString(bytes)
        let parts      = text.Split("::")
        let! _         = Result.require MalformedSegments (parts.Length = 4)
        let! _         = Result.require (UnsupportedVersion parts.[0]) (parts.[0] = "v1")
        let  rawUrl    = HttpUtility.UrlDecode(parts.[1])
        let! url       = Uri.TryCreate(rawUrl, UriKind.Absolute) |> Result.ofTryParse InvalidUrl
        let! perDay    = Int32.TryParse(parts.[2]) |> Result.ofTryParse InvalidPerDay
        let! _         = Result.require InvalidPerDay (perDay > 0)
        let! startDate =
            DateOnly.TryParseExact(parts.[3], "yyyy-MM-dd", null, Globalization.DateTimeStyles.None)
            |> Result.ofTryParse InvalidDate
        return { SourceUrl = url; PerDay = perDay * 1<articles/day>; StartDate = startDate }
    }