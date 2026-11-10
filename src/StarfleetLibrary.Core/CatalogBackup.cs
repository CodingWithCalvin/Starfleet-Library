using System.Text.Json;
using System.Text.Json.Serialization;

namespace StarfleetLibrary.Core;

public static class CatalogBackup
{
    public const int CurrentSchemaVersion = 2;
    public const int MaximumBytes = 10 * 1024 * 1024;

    // All serializers use generated metadata and closed generic enum converters for iOS AOT.
    private static readonly CatalogJsonContext JsonContext = CreateContext();

    public static byte[] Encode(IEnumerable<Book> books)
    {
        ArgumentNullException.ThrowIfNull(books);
        var snapshot = books.Select(book => book is null ? null! : book with { }).ToArray();
        Validate(snapshot);
        var data = JsonSerializer.SerializeToUtf8Bytes(new CatalogEnvelope
        {
            SchemaVersion = CurrentSchemaVersion,
            Books = snapshot
        }, JsonContext.CatalogEnvelope);
        if (data.Length > MaximumBytes) throw new InvalidDataException("The catalog backup exceeds the 10 MB limit.");
        return data;
    }

    public static IReadOnlyList<Book> Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaximumBytes) throw new InvalidDataException("Choose a catalog backup smaller than 10 MB.");
        CatalogEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(data, JsonContext.CatalogEnvelope)
                ?? throw new InvalidDataException("The catalog backup is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("The file is not a valid Starfleet Library JSON backup.", error);
        }
        if (envelope.SchemaVersion is not (1 or CurrentSchemaVersion))
            throw new InvalidDataException($"Catalog version {envelope.SchemaVersion} is not supported by this app.");
        if (envelope.Books is null) throw new InvalidDataException("The catalog backup has no book list.");
        Validate(envelope.Books);
        return Array.AsReadOnly(envelope.Books);
    }

    private static void Validate(IEnumerable<Book> books)
    {
        var identifiers = new HashSet<Guid>();
        foreach (var book in books)
        {
            if (book is null) throw new InvalidDataException("A catalog entry cannot be null.");
            if (!identifiers.Add(book.Id)) throw new InvalidDataException("The catalog contains duplicate book identifiers.");
            if (book.ValidationError is { } message)
                throw new InvalidDataException($"{(string.IsNullOrWhiteSpace(book.Title) ? "Untitled book" : book.Title)}: {message}");
        }
    }

    private static CatalogJsonContext CreateContext()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            MaxDepth = 32,
            RespectNullableAnnotations = true
        };
        options.Converters.Add(new JsonStringEnumConverter<TrekSeries>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<BookFormat>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<BookKind>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<OwnershipStatus>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<ReadingStatus>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return new CatalogJsonContext(options);
    }
}

internal sealed class CatalogEnvelope
{
    [JsonRequired] public int SchemaVersion { get; set; }
    [JsonRequired] public Book[] Books { get; set; } = Array.Empty<Book>();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CatalogEnvelope))]
internal partial class CatalogJsonContext : JsonSerializerContext { }
