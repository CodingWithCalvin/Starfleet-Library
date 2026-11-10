using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StarfleetLibrary.Core;

namespace StarfleetLibrary.Sync;

public sealed record SyncEntry(Guid Id, long Revision, Book? Book, string? Cover);
public sealed record SyncWrite(Guid Id, long ExpectedRevision, Book? Book, string? Cover);
public sealed record SyncConflict(Guid Id, Book? Local, SyncEntry Remote);
public sealed record SyncResult(Book[] Books, SyncEntry[] Baseline, SyncConflict[] Conflicts);
public sealed record DeviceState(Book[] Books, SyncEntry[] Baseline, DateTimeOffset? LastSync);

// A complete snapshot is small for a personal library. Revisions are per edition,
// so independent edits merge and deletions remain visible to offline devices.
public sealed class SyncClient(HttpClient http)
{
    public const string HostedUrl = "https://starfleet-library-calvin.speedykazoo2.chatgpt.site/";

    public async Task<SyncResult> SynchronizeAsync(IReadOnlyList<Book> books, IReadOnlyList<SyncEntry> baseline,
        IReadOnlyDictionary<Guid, string>? covers = null, CancellationToken cancellationToken = default)
    {
        var remote = await GetAsync(cancellationToken);
        var local = books.ToDictionary(b => b.Id);
        var previous = baseline.ToDictionary(b => b.Id);
        var current = remote.ToDictionary(b => b.Id);
        var merged = new Dictionary<Guid, Book>(local);
        var nextBase = new Dictionary<Guid, SyncEntry>(previous);
        var conflicts = new List<SyncConflict>();

        foreach (var id in local.Keys.Union(previous.Keys).Union(current.Keys).ToArray())
        {
            local.TryGetValue(id, out var mine);
            previous.TryGetValue(id, out var before);
            current.TryGetValue(id, out var theirs);
            // Missing server state after a previously synced revision is a reset,
            // not a deletion. Never propagate it into the local library.
            if (theirs is null && before is not null)
                throw new InvalidDataException("The hosted catalog has been reset. Your local books are preserved; reconnect before syncing.");
            var cover = covers?.GetValueOrDefault(id) ?? before?.Cover;
            var localChanged = mine != before?.Book || (mine is not null && cover != before?.Cover);
            var remoteChanged = theirs?.Revision != before?.Revision;
            if (!localChanged)
            {
                Accept(theirs);
                continue;
            }
            if (mine == theirs?.Book && (cover is null || cover == theirs?.Cover))
            {
                Accept(theirs);
                continue;
            }
            if (remoteChanged && before is not null || remoteChanged && theirs is not null)
            {
                conflicts.Add(new(id, mine, theirs!));
                continue;
            }
            var request = new HttpRequestMessage(HttpMethod.Post, "api/entries");
            request.Headers.Add("X-Starfleet-Client", "1");
            request.Content = JsonContent.Create(new SyncWrite(id, before?.Revision ?? 0, mine, mine is null ? null : cover), SyncJson.Context.SyncWrite);
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var changed = await response.Content.ReadFromJsonAsync(SyncJson.Context.SyncEntry, cancellationToken)
                    ?? throw new InvalidDataException("Missing conflict details.");
                conflicts.Add(new(id, mine, changed));
                continue;
            }
            response.EnsureSuccessStatusCode();
            Accept(await response.Content.ReadFromJsonAsync(SyncJson.Context.SyncEntry, cancellationToken)
                ?? throw new InvalidDataException("Missing sync acknowledgement."));
        }
        return new(merged.Values.ToArray(), nextBase.Values.ToArray(), conflicts.ToArray());

        void Accept(SyncEntry? entry)
        {
            if (entry is null) return;
            if (entry.Book is { } book) merged[entry.Id] = book;
            else merged.Remove(entry.Id);
            nextBase[entry.Id] = entry;
        }
    }

    public async Task<SyncEntry[]> GetAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync("api/entries", ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync(SyncJson.Context.SyncEntryArray, ct)
            ?? throw new InvalidDataException("The service did not return a catalog.");
        _ = CatalogBackup.Encode(result.Where(x => x.Book is not null).Select(x => x.Book!));
        if (result.Any(x => x.Id == Guid.Empty || x.Revision < 1 || x.Book is not null && x.Book.Id != x.Id) || result.Select(x => x.Id).Distinct().Count() != result.Length)
            throw new InvalidDataException("The service returned invalid revisions.");
        return result;
    }

    public async Task UploadCoverAsync(string hash, byte[] bytes, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "api/covers/" + hash) { Content = new ByteArrayContent(bytes) };
        request.Headers.Add("X-Starfleet-Client", "1");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}

public static class SyncJson
{
    public static SyncJsonContext Context { get; } = new(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true });
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SyncEntry[]))]
[JsonSerializable(typeof(SyncWrite))]
[JsonSerializable(typeof(DeviceState))]
public partial class SyncJsonContext : JsonSerializerContext { }
