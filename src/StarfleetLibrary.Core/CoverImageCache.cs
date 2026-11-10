using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace StarfleetLibrary.Core;

/// <summary>Optional artwork must never prevent a catalog from opening, including offline.</summary>
public sealed class CoverImageCache(HttpClient client, string directory, string? importedCoverDirectory = null)
{
    private const int MaxBytes = 5 * 1024 * 1024;
    private readonly SemaphoreSlim _downloads = new(3);
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _pending = new();

    public async Task<string?> GetAsync(Book book)
    {
        // Imported artwork belongs to the app, not to a cache or the source library.
        // An explicit URL chosen in the editor overrides the imported image.
        if (!BookCover.IsWebUrl(book.CoverUrl) && importedCoverDirectory is not null)
        {
            foreach (var extension in new[] { ".jpg", ".png", ".gif" })
            {
                var imported = Path.Combine(importedCoverDirectory, book.Id.ToString("N") + extension);
                if (File.Exists(imported)) return imported;
            }
        }
        var source = BookCover.Source(book);
        if (source is null) return null;
        var key = source.AbsoluteUri;
        var request = _pending.GetOrAdd(key, _ => new Lazy<Task<string?>>(() => DownloadAsync(source)));
        try { return await request.Value.ConfigureAwait(false); }
        finally { _pending.TryRemove(new KeyValuePair<string, Lazy<Task<string?>>>(key, request)); }
    }

    private async Task<string?> DownloadAsync(Uri source)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.AbsoluteUri)));
        var stem = Path.Combine(directory, hash);
        foreach (var extension in new[] { ".jpg", ".png", ".gif" })
            if (File.Exists(stem + extension)) return stem + extension;

        await _downloads.WaitAsync().ConfigureAwait(false);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            using var response = await client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes) return null;
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream();
            var buffer = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (bytes.Length + read > MaxBytes) return null;
                bytes.Write(buffer, 0, read);
            }
            var data = bytes.ToArray();
            var extension = ImageExtension(data);
            if (extension is null) return null;
            Directory.CreateDirectory(directory);
            var path = stem + extension;
            // A terminated write must not leave an apparently cached image behind.
            var temporary = path + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, data, timeout.Token).ConfigureAwait(false);
                File.Move(temporary, path, overwrite: true);
                return path;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return null;
        }
        finally { _downloads.Release(); }
    }

    private static string? ImageExtension(byte[] data)
    {
        if (data.Length < 12) return null;
        if (data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff) return ".jpg";
        if (data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (data.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || data.AsSpan(0, 6).SequenceEqual("GIF89a"u8)) return ".gif";
        return null;
    }
}
