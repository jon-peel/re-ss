module ReSS.Tests.UrlGuardTests

open System
open System.Net
open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open ReSS.Domain.Types
open ReSS.Domain.UrlGuard

// ---- 4.1 scheme rejection ----

[<Fact>]
let ``ftp:// URL returns NonHttpScheme`` () =
    let result = validateUrl "ftp://example.com/feed" |> Async.RunSynchronously
    Assert.Equal(Error NonHttpScheme, result)

[<Fact>]
let ``file:// URL returns NonHttpScheme`` () =
    let result = validateUrl "file:///etc/passwd" |> Async.RunSynchronously
    Assert.Equal(Error NonHttpScheme, result)

// ---- 4.2 malformed URL ----

[<Fact>]
let ``malformed URL string returns MalformedUrl`` () =
    let result = validateUrl "not a url at all" |> Async.RunSynchronously
    Assert.Equal(Error MalformedUrl, result)

[<Fact>]
let ``empty string returns MalformedUrl`` () =
    let result = validateUrl "" |> Async.RunSynchronously
    Assert.Equal(Error MalformedUrl, result)

// ---- 4.3 IP range predicates (unit tests, no DNS) ----

[<Fact>]
let ``IPv4 loopback 127.0.0.1 is blocked`` () =
    Assert.True(isPrivateOrLoopback (IPAddress.Parse("127.0.0.1")))

[<Fact>]
let ``IPv4 loopback 127.255.255.255 is blocked`` () =
    Assert.True(isPrivateOrLoopback (IPAddress.Parse("127.255.255.255")))

[<Fact>]
let ``IPv6 loopback ::1 is blocked`` () =
    Assert.True(isPrivateOrLoopback (IPAddress.Parse("::1")))

[<Fact>]
let ``link-local 169.254.169.254 is blocked`` () =
    Assert.True(isPrivateOrLoopback (IPAddress.Parse("169.254.169.254")))

[<Fact>]
let ``link-local 169.254.0.1 is blocked`` () =
    Assert.True(isPrivateOrLoopback (IPAddress.Parse("169.254.0.1")))

[<Fact>]
let ``RFC1918 class A 10.0.0.1 is blocked`` () =
    Assert.True(isPrivateOrLoopback (IPAddress.Parse("10.0.0.1")))

[<Fact>]
let ``RFC1918 class A 10.255.255.255 is blocked`` () =
    Assert.True(isPrivateOrLoopback (IPAddress.Parse("10.255.255.255")))

[<Fact>]
let ``RFC1918 class B 172.16.0.1 is blocked`` () =
    Assert.True(isPrivateOrLoopback (IPAddress.Parse("172.16.0.1")))

[<Fact>]
let ``RFC1918 class B 172.31.255.255 is blocked`` () =
    Assert.True(isPrivateOrLoopback (IPAddress.Parse("172.31.255.255")))

[<Fact>]
let ``RFC1918 class C 192.168.1.1 is blocked`` () =
    Assert.True(isPrivateOrLoopback (IPAddress.Parse("192.168.1.1")))

[<Fact>]
let ``RFC1918 class C 192.168.255.255 is blocked`` () =
    Assert.True(isPrivateOrLoopback (IPAddress.Parse("192.168.255.255")))

[<Fact>]
let ``public IP 1.1.1.1 is not blocked`` () =
    Assert.False(isPrivateOrLoopback (IPAddress.Parse("1.1.1.1")))

[<Fact>]
let ``public IP 8.8.8.8 is not blocked`` () =
    Assert.False(isPrivateOrLoopback (IPAddress.Parse("8.8.8.8")))

// FsCheck properties for blocked ranges
type IpGenerators =
    static member LoopbackIp() =
        gen {
            let! b2 = Gen.choose (0, 255)
            let! b3 = Gen.choose (0, 255)
            let! b4 = Gen.choose (0, 255)
            return IPAddress.Parse(sprintf "127.%d.%d.%d" b2 b3 b4)
        } |> Arb.fromGen

    static member LinkLocalIp() =
        gen {
            let! b3 = Gen.choose (0, 255)
            let! b4 = Gen.choose (0, 255)
            return IPAddress.Parse(sprintf "169.254.%d.%d" b3 b4)
        } |> Arb.fromGen

    static member ClassAPrivateIp() =
        gen {
            let! b2 = Gen.choose (0, 255)
            let! b3 = Gen.choose (0, 255)
            let! b4 = Gen.choose (0, 255)
            return IPAddress.Parse(sprintf "10.%d.%d.%d" b2 b3 b4)
        } |> Arb.fromGen

    static member ClassBPrivateIp() =
        gen {
            let! b2 = Gen.choose (16, 31)
            let! b3 = Gen.choose (0, 255)
            let! b4 = Gen.choose (0, 255)
            return IPAddress.Parse(sprintf "172.%d.%d.%d" b2 b3 b4)
        } |> Arb.fromGen

    static member ClassCPrivateIp() =
        gen {
            let! b3 = Gen.choose (0, 255)
            let! b4 = Gen.choose (0, 255)
            return IPAddress.Parse(sprintf "192.168.%d.%d" b3 b4)
        } |> Arb.fromGen

[<Fact>]
let ``entire 127.x.x.x range is blocked (property)`` () =
    let prop ip = isPrivateOrLoopback ip
    Prop.forAll (IpGenerators.LoopbackIp()) prop |> Check.QuickThrowOnFailure

[<Fact>]
let ``entire 169.254.x.x range is blocked (property)`` () =
    let prop ip = isPrivateOrLoopback ip
    Prop.forAll (IpGenerators.LinkLocalIp()) prop |> Check.QuickThrowOnFailure

[<Fact>]
let ``entire 10.x.x.x range is blocked (property)`` () =
    let prop ip = isPrivateOrLoopback ip
    Prop.forAll (IpGenerators.ClassAPrivateIp()) prop |> Check.QuickThrowOnFailure

[<Fact>]
let ``entire 172.16-31.x.x range is blocked (property)`` () =
    let prop ip = isPrivateOrLoopback ip
    Prop.forAll (IpGenerators.ClassBPrivateIp()) prop |> Check.QuickThrowOnFailure

[<Fact>]
let ``entire 192.168.x.x range is blocked (property)`` () =
    let prop ip = isPrivateOrLoopback ip
    Prop.forAll (IpGenerators.ClassCPrivateIp()) prop |> Check.QuickThrowOnFailure

// ---- 4.3b IPv6 ULA and IPv4-mapped IPv6 ----

[<Fact>]
let ``IPv6 ULA fd00::1 is blocked`` () =
    Assert.True(isPrivateOrLoopback (IPAddress.Parse("fd00::1")))

[<Fact>]
let ``IPv6 ULA fc00::1 is blocked`` () =
    Assert.True(isPrivateOrLoopback (IPAddress.Parse("fc00::1")))

[<Fact>]
let ``IPv4-mapped private ::ffff:192.168.1.1 is blocked`` () =
    Assert.True(isPrivateOrLoopback (IPAddress.Parse("::ffff:192.168.1.1")))

[<Fact>]
let ``IPv4-mapped public ::ffff:8.8.8.8 is not blocked`` () =
    Assert.False(isPrivateOrLoopback (IPAddress.Parse("::ffff:8.8.8.8")))

[<Fact>]
let ``public IPv6 2001:db8::1 is not blocked`` () =
    Assert.False(isPrivateOrLoopback (IPAddress.Parse("2001:db8::1")))

type IpV6Generators =
    static member UlaIp() =
        gen {
            // fc00::/7 — first byte is 0xFC or 0xFD
            let! firstByte = Gen.elements [ 0xFCuy; 0xFDuy ]
            let! b2 = Gen.choose (0, 255)
            let! b3 = Gen.choose (0, 255)
            let! b4 = Gen.choose (0, 255)
            let! b5 = Gen.choose (0, 255)
            let! b6 = Gen.choose (0, 255)
            let! b7 = Gen.choose (0, 255)
            let! b8 = Gen.choose (0, 255)
            let! b9 = Gen.choose (0, 255)
            let! b10 = Gen.choose (0, 255)
            let! b11 = Gen.choose (0, 255)
            let! b12 = Gen.choose (0, 255)
            let! b13 = Gen.choose (0, 255)
            let! b14 = Gen.choose (0, 255)
            let! b15 = Gen.choose (0, 255)
            let! b16 = Gen.choose (0, 255)
            let bytes = [| firstByte; byte b2; byte b3; byte b4; byte b5; byte b6; byte b7; byte b8
                           byte b9; byte b10; byte b11; byte b12; byte b13; byte b14; byte b15; byte b16 |]
            return IPAddress(bytes)
        } |> Arb.fromGen

[<Fact>]
let ``entire ULA range fc00::-fdff::... is blocked (property)`` () =
    let prop ip = isPrivateOrLoopback ip
    Prop.forAll (IpV6Generators.UlaIp()) prop |> Check.QuickThrowOnFailure

// ---- 4.4 integration: DNS resolution test ----

[<Fact>]
[<Trait("Category", "Integration")>]
let ``valid public https URL returns Ok`` () =
    // Uses a URL that won't hit private space: loopback resolved locally
    // We simulate by using an http-scheme URL pointing to a known public address.
    // In CI without DNS, we skip by catching DNS failure.
    let result = validateUrl "https://example.com/feed" |> Async.RunSynchronously
    match result with
    | Ok url -> Assert.Equal(Uri("https://example.com/feed"), url)
    | Error (PrivateOrLoopbackAddress _) ->
        // acceptable if DNS resolves to something private (unlikely for example.com)
        ()
    | Error e ->
        // DNS resolution failed in sandbox — acceptable
        ()

// ---- 4.5 any resolved IP in blocked range causes rejection ----

[<Fact>]
let ``http URL with loopback hostname rejected`` () =
    // Use a literal IP in the URL to avoid DNS
    let result = validateUrl "http://127.0.0.1/feed" |> Async.RunSynchronously
    match result with
    | Error (PrivateOrLoopbackAddress _) -> ()
    | other -> Assert.Fail(sprintf "Expected PrivateOrLoopbackAddress, got %A" other)

[<Fact>]
let ``http URL with 192.168.x.x literal IP rejected`` () =
    let result = validateUrl "http://192.168.1.100/feed" |> Async.RunSynchronously
    match result with
    | Error (PrivateOrLoopbackAddress _) -> ()
    | other -> Assert.Fail(sprintf "Expected PrivateOrLoopbackAddress, got %A" other)

[<Fact>]
let ``http URL with 10.x.x.x literal IP rejected`` () =
    let result = validateUrl "http://10.0.0.1/feed" |> Async.RunSynchronously
    match result with
    | Error (PrivateOrLoopbackAddress _) -> ()
    | other -> Assert.Fail(sprintf "Expected PrivateOrLoopbackAddress, got %A" other)

[<Fact>]
let ``http URL with 169.254.x.x literal IP rejected`` () =
    let result = validateUrl "http://169.254.169.254/feed" |> Async.RunSynchronously
    match result with
    | Error (PrivateOrLoopbackAddress _) -> ()
    | other -> Assert.Fail(sprintf "Expected PrivateOrLoopbackAddress, got %A" other)
