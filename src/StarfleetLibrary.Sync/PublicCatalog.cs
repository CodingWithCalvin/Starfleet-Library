using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using StarfleetLibrary.Core;

namespace StarfleetLibrary.Sync;

// Explicit projection: never add notes, reading history, ratings, local paths or keys here.
public sealed record PublicBook(Guid Id, string Title, string Authors, TrekSeries Series,
    string BookNumber, string Isbn, string Publisher, string PublicationYear, string Edition,
    BookFormat Format, BookKind Kind, OwnershipStatus Ownership, int PageCount)
{
    public static PublicBook From(Book book) => new(book.Id, book.Title, book.Authors, book.Series,
        book.BookNumber, book.Isbn, book.Publisher, book.PublicationYear, book.Edition,
        book.Format, book.Kind, book.Ownership, book.PageCount);
}
public sealed record PublicEdition(Guid Id, long Revision, PublicBook? Book, string? Cover);
public sealed record PublicRevision(Guid Id, long Revision);
public sealed record PublicPublication(bool Enabled, PublicRevision[] Entries);

public sealed class PublicCatalogPublisher(HttpClient transport, VaultCrypto crypto)
{
    private Dictionary<Guid,long>? revisions;
    private bool enabled;
    private readonly HashSet<string> uploadedCovers = [];

    public async Task PublishAsync(IEnumerable<SyncEntry> entries, CancellationToken ct)
    {
        if (revisions is null)
        {
            using var response = await transport.GetAsync("v1/publication",ct);
            // Older services have no public-catalog feature. Private forks opt out by default.
            if (response.StatusCode == HttpStatusCode.NotFound) { revisions=[]; return; }
            response.EnsureSuccessStatusCode();
            var publication=await response.Content.ReadFromJsonAsync(PublicJson.Context.PublicPublication,ct)
                ?? throw new InvalidDataException("Missing publication configuration.");
            enabled=publication.Enabled;
            revisions=publication.Entries.ToDictionary(entry=>entry.Id,entry=>entry.Revision);
        }
        if (!enabled) return;
        foreach (var entry in entries)
        {
            if (revisions.GetValueOrDefault(entry.Id)>=entry.Revision) continue;
            if (entry.Book is not null && entry.Cover is {} hash && !uploadedCovers.Contains(hash))
            {
                var id=crypto.CoverId(hash);
                var envelope=await transport.GetByteArrayAsync("v1/covers/"+id,ct);
                var bytes=crypto.Decrypt(envelope,"cover/"+id);
                if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Public cover checksum mismatch.");
                using var coverResponse=await transport.PutAsync("v1/public/covers/"+hash,new ByteArrayContent(bytes),ct);
                coverResponse.EnsureSuccessStatusCode();
                uploadedCovers.Add(hash);
            }
            var edition=new PublicEdition(entry.Id,entry.Revision,entry.Book is {} book?PublicBook.From(book):null,entry.Book is null?null:entry.Cover);
            using var result=await transport.PostAsync("v1/public/entries",JsonContent.Create(edition,PublicJson.Context.PublicEdition),ct);
            // Another device may have committed a newer revision during publication.
            if (result.StatusCode==HttpStatusCode.Conflict) continue;
            result.EnsureSuccessStatusCode();
            revisions[entry.Id]=entry.Revision;
        }
    }
}

public static class PublicJson
{
    public static PublicJsonContext Context {get;} = new(new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
}
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PublicPublication))]
[JsonSerializable(typeof(PublicEdition))]
public partial class PublicJsonContext : JsonSerializerContext { }
