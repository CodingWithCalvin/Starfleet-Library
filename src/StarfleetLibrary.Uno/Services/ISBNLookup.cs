using System.Text.Json;
using System.Text.RegularExpressions;
using StarfleetLibrary.Core;

namespace StarfleetLibrary.Uno.Services;

/// <summary>General ISBN metadata, not proof that a title belongs to Star Trek.</summary>
public sealed class ISBNLookup(HttpClient client)
{
    public async Task<Book> FindAsync(string isbn, CancellationToken ct = default)
    {
        var canonical = IsbnUtility.Canonical(isbn)
            ?? throw new ArgumentException("Enter a valid ISBN-10 or ISBN-13.", nameof(isbn));
        var key = $"ISBN:{canonical}";
        var url = $"https://openlibrary.org/api/books?bibkeys={Uri.EscapeDataString(key)}&format=json&jscmd=data";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.UserAgent.ParseAdd("StarfleetLibrary/1.0");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Open Library is unavailable (HTTP {(int)response.StatusCode}). Enter the edition manually or try again.", null, response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: deadline.Token);
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty(key, out var entry))
                throw new InvalidOperationException("Open Library could not find this edition. You can still enter its details manually.");
            var title = Text(entry, "title");
            if (string.IsNullOrWhiteSpace(title))
                throw new InvalidOperationException("Open Library returned no title. Enter this edition manually.");
            var date = Text(entry, "publish_date");
            var year = Regex.Match(date, @"\b(?:18|19|20|21)\d{2}\b").Value;
            var pages = entry.TryGetProperty("number_of_pages", out var pageValue) && pageValue.ValueKind == JsonValueKind.Number && pageValue.TryGetInt32(out var count)
                ? Math.Max(0, count) : 0;
            string? cover = null;
            if (entry.TryGetProperty("cover", out var covers) && covers.ValueKind == JsonValueKind.Object)
            {
                var value = Text(covers, "large");
                if (value.Length == 0) value = Text(covers, "medium");
                if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                    cover = new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri.AbsoluteUri;
            }
            ct.ThrowIfCancellationRequested();
            var reference = NumberedBooks.FindByIsbn(canonical);
            var numbering = reference is not null ? (reference.Series, reference.Number) : await ReadNumberingAsync(canonical, deadline.Token);
            ct.ThrowIfCancellationRequested();
            return new Book
            {
                Title = title,
                Authors = Names(entry, "authors"),
                Isbn = canonical,
                Publisher = Names(entry, "publishers"),
                PublicationYear = year,
                PageCount = pages,
                Series = numbering?.Item1 ?? InferSeries($"{title} {Text(entry, "subtitle")}"),
                BookNumber = numbering?.Item2.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
                CoverUrl = cover
            };
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("The ISBN lookup timed out. You can still add this edition manually.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Open Library returned unreadable metadata. Enter the edition manually or try again.", ex);
        }
    }

    private async Task<(TrekSeries, int)?> ReadNumberingAsync(string isbn, CancellationToken ct)
    {
        // Optional edition metadata must not discard a successful title/author lookup.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://openlibrary.org/isbn/{isbn}.json");
            request.Headers.UserAgent.ParseAdd("StarfleetLibrary/1.0");
            using var response = await client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("series", out var values)) return null;
            var series = values.ValueKind == JsonValueKind.String ? new[] { values.GetString()! } :
                values.ValueKind == JsonValueKind.Array ? values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).ToArray() : [];
            return NumberedBooks.FromSeriesMetadata(series);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException) { return null; }
    }

    private static string Text(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString()?.Trim() ?? "" : "";

    private static string Names(JsonElement value, string property) =>
        value.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array
            ? string.Join(", ", list.EnumerateArray().Select(item => Text(item, "name")).Where(name => name.Length > 0)) : "";

    private static TrekSeries InferSeries(string value)
    {
        var title = value.ToLowerInvariant();
        if (title.Contains("next generation")) return TrekSeries.NextGeneration;
        if (title.Contains("deep space nine") || title.Contains("deep space 9")) return TrekSeries.DeepSpaceNine;
        if (title.Contains("voyager")) return TrekSeries.Voyager;
        if (title.Contains("discovery")) return TrekSeries.Discovery;
        if (title.Contains("strange new worlds")) return TrekSeries.StrangeNewWorlds;
        if (title.Contains("picard")) return TrekSeries.Picard;
        if (title.Contains("enterprise")) return TrekSeries.Enterprise;
        if (title.Contains("original series")) return TrekSeries.OriginalSeries;
        return TrekSeries.Other;
    }
}

