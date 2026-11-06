open System
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Threading
open System.Threading.Tasks
open RestSharp
open Generated.Models.Entry
open Generated.Api.EntriesClient

type WireHandler() =
    inherit HttpMessageHandler()
    let mutable requests = 0
    let mutable canceled = false
    let mutable encodedPath = false
    let mutable queryPreserved = false
    let mutable headerPreserved = false
    member _.Requests = requests
    member _.Canceled = canceled
    member _.EncodedPath = encodedPath
    member _.QueryPreserved = queryPreserved
    member _.HeaderPreserved = headerPreserved
    override _.SendAsync(request, cancellationToken) = task {
        requests <- requests + 1
        let path = request.RequestUri.AbsolutePath
        if path = "/slow" then
            try do! Task.Delay(Timeout.Infinite, cancellationToken)
            with :? OperationCanceledException as error -> canceled <- true; raise error
        let response = new HttpResponseMessage(HttpStatusCode.OK)
        response.RequestMessage <- request
        if path = "/entries/missing" then
            response.StatusCode <- HttpStatusCode.NotFound
            response.Content <- new StringContent("missing")
        elif request.Headers.Authorization.Parameter <> "fresh" then
            response.StatusCode <- HttpStatusCode.Unauthorized
        elif path = "/ping" || path = "/slow" then
            response.StatusCode <- HttpStatusCode.NoContent
        else
            let entry = "{\"id\":7,\"label\":\"native\",\"score\":1.5,\"active\":true,\"tags\":[\"first\",\"second\"]}"
            let payload = if path = "/entries" then "[" + entry + "]" else entry
            response.Content <- new StringContent(payload, Encoding.UTF8, "application/json")
            if path.StartsWith("/entries/", StringComparison.Ordinal) then
                encodedPath <- path.EndsWith("a%2Fb", StringComparison.OrdinalIgnoreCase)
                queryPreserved <- Uri.UnescapeDataString(request.RequestUri.Query).Contains("query.value=x y")
                headerPreserved <- request.Headers.Contains("X-Request-ID") &&
                    (request.Headers.GetValues("X-Request-ID") |> Seq.exactlyOne) = "request-7"
        return response
    }

type RefreshHandler(inner: HttpMessageHandler) =
    inherit DelegatingHandler(inner)
    let mutable before = 0
    let mutable after = 0
    let mutable refreshes = 0
    let mutable token = "expired"
    member _.Before = before
    member _.After = after
    member _.Refreshes = refreshes
    member private _.SendCore(request, cancellationToken) = base.SendAsync(request, cancellationToken)
    override this.SendAsync(request, cancellationToken) = task {
        before <- before + 1
        request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)
        let! first = this.SendCore(request, cancellationToken)
        if first.StatusCode = HttpStatusCode.Unauthorized then
            first.Dispose()
            refreshes <- refreshes + 1
            token <- "fresh"
            request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)
            let! retried = this.SendCore(request, cancellationToken)
            after <- after + 1
            return retried
        else
            after <- after + 1
            return first
    }

let check condition message = if not condition then failwith message
let run () =
    use wire = new WireHandler()
    use refresh = new RefreshHandler(wire)
    use http = new HttpClient(refresh)
    http.BaseAddress <- Uri("https://native.invalid/")
    use client = new RestClient(http, false)
    let entry = getEntry client "a/b" (Some "x y") (Some "request-7") CancellationToken.None |> fun task -> task.GetAwaiter().GetResult()
    check (entry.Id = 7 && entry.Label = "native" && entry.Score = 1.5 && entry.Active && entry.Tags.Length = 2) "Typed nested collection/primitive parsing failed."
    check (wire.EncodedPath && wire.QueryPreserved && wire.HeaderPreserved) "Wire path/query/header names were changed."
    check (refresh.Refreshes = 1 && refresh.Before = 1 && refresh.After = 1 && wire.Requests = 2) "Injected logging/refresh pipeline was bypassed."
    let entries = listEntries client CancellationToken.None |> fun task -> task.GetAwaiter().GetResult()
    check (entries.Length = 1 && entries.Head.Id = 7) "Typed collection response failed."
    ping client CancellationToken.None |> fun task -> task.GetAwaiter().GetResult()
    let mutable rejected = false
    try getEntry client "missing" None None CancellationToken.None |> fun task -> task.GetAwaiter().GetResult() |> ignore
    with :? HttpRequestException as error -> rejected <- error.StatusCode = Nullable(HttpStatusCode.NotFound)
    check rejected "HTTP failure status was lost or deserialized as success."
    let beforeCanceledCall = wire.Requests
    use preCanceled = new CancellationTokenSource()
    preCanceled.Cancel()
    let mutable preCanceledRejected = false
    try slow client preCanceled.Token |> fun task -> task.GetAwaiter().GetResult()
    with :? OperationCanceledException -> preCanceledRejected <- true
    check (preCanceledRejected && wire.Requests = beforeCanceledCall) "An already canceled call reached the transport."
    use inFlight = new CancellationTokenSource(TimeSpan.FromMilliseconds(150.0))
    let mutable canceledInFlight = false
    try slow client inFlight.Token |> fun task -> task.GetAwaiter().GetResult()
    with :? OperationCanceledException -> canceledInFlight <- true
    check (canceledInFlight && wire.Canceled) "Cancellation did not reach the active HTTP transport."
    printfn "native F# client pipeline passed"

run ()
