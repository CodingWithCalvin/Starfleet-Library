using System.Net;
using System.Text.Json;
using StarfleetLibrary.Core;
using StarfleetLibrary.Uno.Services;

internal static class LookupChecks
{
    public static async Task Run()
    {
        var handler = new MetadataHandler("9780671520960");
        using var client = new HttpClient(handler);
        var found = await new ISBNLookup(client).FindAsync("0671520962");
        Assert(found.Series == TrekSeries.Voyager && found.BookNumber == "2", "Reference did not correct the series and number");
        Assert(handler.Calls == 1, "Known reference made unnecessary edition request");
        handler.Isbn = "9780306406157";
        handler.Edition = "{\"series\":[\"Star Trek: The Next Generation #7\"]}";
        found = await new ISBNLookup(client).FindAsync(handler.Isbn);
        Assert(found.Series == TrekSeries.NextGeneration && found.BookNumber == "7", "Explicit series metadata was ignored");
        foreach (var metadata in new[] { "{}", "[]", "{\"series\":[\"Homecoming Book 1\"]}", "{\"series\":[\"Voyager #2\",\"Voyager #3\"]}", "{\"series\":42}", "broken" })
        {
            handler.Edition = metadata;
            found = await new ISBNLookup(client).FindAsync(handler.Isbn);
            Assert(found.Title == "Homecoming Book 1" && found.BookNumber == "" && found.Authors == "Example Author", "Optional metadata damaged title/author lookup or guessed a number");
        }
        handler.EditionStatus = HttpStatusCode.ServiceUnavailable;
        found = await new ISBNLookup(client).FindAsync(handler.Isbn);
        Assert(found.BookNumber == "" && found.Title.Length > 0, "Optional HTTP failure discarded metadata");
        handler.CancelEdition = true;
        found = await new ISBNLookup(client).FindAsync(handler.Isbn);
        Assert(found.BookNumber == "" && found.Title.Length > 0, "Optional timeout discarded metadata");
        using var cancel = new CancellationTokenSource();
        handler.CallerCancellation = cancel;
        try { await new ISBNLookup(client).FindAsync(handler.Isbn, cancel.Token); throw new Exception("Caller cancellation was swallowed"); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class MetadataHandler(string isbn) : HttpMessageHandler
    {
        public string Isbn = isbn;
        public int Calls;
        public string Edition = "{}";
        public HttpStatusCode EditionStatus = HttpStatusCode.OK;
        public bool CancelEdition;
        public CancellationTokenSource? CallerCancellation;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (request.RequestUri!.AbsolutePath == "/api/books")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, object> { [$"ISBN:{Isbn}"] = new { title = "Homecoming Book 1", authors = new[] { new { name = "Example Author" } }, publish_date = "1995" } })) });
            Assert(request.RequestUri.AbsolutePath == $"/isbn/{Isbn}.json", "Wrong edition endpoint");
            CallerCancellation?.Cancel();
            if (CancelEdition) throw new OperationCanceledException();
            return Task.FromResult(new HttpResponseMessage(EditionStatus) { Content = new StringContent(Edition) });
        }
    }
}
