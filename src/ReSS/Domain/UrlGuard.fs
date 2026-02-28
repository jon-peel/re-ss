module ReSS.Domain.UrlGuard

open System
open System.Net
open System.Net.Sockets
open ReSS.Domain.Types

// ---- IP range helpers (internal, exposed for testing) ----

let private ipToUInt32 (ip: IPAddress) =
    let b = ip.GetAddressBytes()
    if b.Length = 4 then
        (uint32 b.[0] <<< 24) ||| (uint32 b.[1] <<< 16) ||| (uint32 b.[2] <<< 8) ||| uint32 b.[3]
    else 0u

let private inRange (ip: IPAddress) (network: string) prefix =
    if ip.AddressFamily <> AddressFamily.InterNetwork then false
    else
        let ipNum  = ipToUInt32 ip
        let netNum = ipToUInt32 (IPAddress.Parse(network))
        let mask   = if prefix = 0 then 0u else 0xFFFFFFFFu <<< (32 - prefix)
        (ipNum &&& mask) = (netNum &&& mask)

let rec isPrivateOrLoopback (ip: IPAddress) : bool =
    match ip.AddressFamily with
    | AddressFamily.InterNetworkV6 ->
        ip.Equals(IPAddress.IPv6Loopback) ||
        // fe80::/10 — link-local: first 10 bits = 1111111010
        (ip.GetAddressBytes().[0] = 0xfeuy && (ip.GetAddressBytes().[1] &&& 0xC0uy) = 0x80uy) ||
        // fc00::/7 — Unique Local (ULA): first byte is 0xFC or 0xFD
        (ip.GetAddressBytes().[0] &&& 0xFEuy = 0xFCuy) ||
        // ::ffff:0:0/96 — IPv4-mapped: extract the embedded IPv4 and re-check
        (ip.IsIPv4MappedToIPv6 && isPrivateOrLoopback (ip.MapToIPv4()))
    | AddressFamily.InterNetwork ->
        inRange ip "127.0.0.0"   8  ||   // loopback
        inRange ip "169.254.0.0" 16 ||   // link-local
        inRange ip "10.0.0.0"    8  ||   // RFC1918 class A
        inRange ip "172.16.0.0"  12 ||   // RFC1918 class B
        inRange ip "192.168.0.0" 16      // RFC1918 class C
    | _ -> false

// ---- public API ----

let validateUrl (rawUrl: string) : Async<Result<Uri, UrlGuardError>> =
    async {
        let mutable uri: Uri = null
        if not (Uri.TryCreate(rawUrl, UriKind.Absolute, &uri)) then
            return Error MalformedUrl
        elif uri.Scheme <> "http" && uri.Scheme <> "https" then
            return Error NonHttpScheme
        else
            // If it's a literal IP, check it directly. Otherwise resolve via DNS.
            let host = uri.Host
            // If DNS resolution fails, ips will be empty and the URL will pass the guard.
            // The subsequent fetch will fail with UnreachableUrl in that case.
            // This is an accepted trade-off for the current use case.
            let! ips =
                match IPAddress.TryParse(host) with
                | true, ip -> async { return [| ip |] }
                | false, _ ->
                    async {
                        try
                            return! Dns.GetHostAddressesAsync(host) |> Async.AwaitTask
                        with _ ->
                            return [||]
                    }
            let blocked =
                ips |> Array.tryFind isPrivateOrLoopback
            match blocked with
            | Some ip -> return Error (PrivateOrLoopbackAddress (ip.ToString()))
            | None    -> return Ok uri
    }
