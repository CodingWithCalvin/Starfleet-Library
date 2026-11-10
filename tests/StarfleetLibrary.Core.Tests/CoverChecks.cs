using System.Net;
using System.Text;
using StarfleetLibrary.Core;

static class CoverChecks
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
    private static readonly Book Book = new() { CoverUrl = "https://example.invalid/cover" };

    public static void Sources()
    {
        Check.Equal("https://example.invalid/cover", BookCover.Source(Book with { Isbn = "0306406152" })!.AbsoluteUri);
        Check.Equal("https://covers.openlibrary.org/b/isbn/9780306406157-M.jpg?default=false",
            BookCover.Source(new Book { Isbn = "0-306-40615-2" })!.AbsoluteUri);
        Check.Equal<Uri?>(null, BookCover.Source(new Book { Isbn = "invalid", CoverUrl = "file:///private/image.jpg" }));
        Check.Equal<Uri?>(null, BookCover.Source(new Book()));
        Check.True(BookCover.IsWebUrl(" https://example.invalid/cover.png "));
        Check.False(BookCover.IsWebUrl("data:image/png;base64,aGVsbG8="));
        Check.False(BookCover.IsWebUrl("relative.jpg"));
    }

    public static async Task Cache()
    {
        await InDirectory(async directory =>
        {
            var requests = 0;
            using var client = new HttpClient(new Handler(async () =>
            {
                Interlocked.Increment(ref requests);
                await Task.Delay(40);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
            }));
            var cache = new CoverImageCache(client, directory);
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => cache.GetAsync(Book)));
            Check.Equal(1, requests);
            Check.True(results.All(path => path == results[0] && path is not null));
            Check.Equal(Png, await File.ReadAllBytesAsync(results[0]!), sequence: true);
            using var offline = new HttpClient(new Handler(() => throw new HttpRequestException("Offline")));
            Check.Equal(results[0], await new CoverImageCache(offline, directory).GetAsync(Book));
            Check.Equal(0, Directory.GetFiles(directory, "*.tmp").Length);
        });
    }

    public static async Task Imported()
    {
        await InDirectory(async directory =>
        {
            var importedDirectory = Path.Combine(directory, "imported");
            Directory.CreateDirectory(importedDirectory);
            var book = Book with { CoverUrl = null, Isbn = "0306406152" };
            var localPath = Path.Combine(importedDirectory, book.Id.ToString("N") + ".png");
            await File.WriteAllBytesAsync(localPath, Png);
            var requests = 0;
            using var client = new HttpClient(new Handler(() =>
            {
                requests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) });
            }));
            var cache = new CoverImageCache(client, Path.Combine(directory, "cache"), importedDirectory);
            Check.Equal(localPath, await cache.GetAsync(book));
            Check.Equal(0, requests);
            var custom = await cache.GetAsync(book with { CoverUrl = Book.CoverUrl });
            Check.True(custom is not null && custom != localPath);
            Check.Equal(1, requests);
            Check.Equal(localPath, await cache.GetAsync(book));
            Check.Equal<string?>(null, await cache.GetAsync(new Book()));
            Check.Equal(Png, await File.ReadAllBytesAsync(localPath), sequence: true);
        });
    }

    public static async Task Failures()
    {
        await InDirectory(async directory =>
        {
            foreach (var response in new[]
            {
                new HttpResponseMessage(HttpStatusCode.NotFound),
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("<html>Not an image</html>")) },
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[5 * 1024 * 1024 + 1]) }
            })
            {
                using var client = new HttpClient(new Handler(() => Task.FromResult(response)));
                Check.Equal<string?>(null, await new CoverImageCache(client, directory).GetAsync(Book));
            }
            foreach (var error in new Exception[] { new HttpRequestException("Offline"), new OperationCanceledException("Timeout") })
            {
                using var client = new HttpClient(new Handler(() => throw error));
                Check.Equal<string?>(null, await new CoverImageCache(client, directory).GetAsync(Book));
            }
            Check.Equal(0, Directory.GetFiles(directory).Length);
        });
    }

    private static async Task InDirectory(Func<string, Task> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "starfleet-cover-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try { await action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class Handler(Func<Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send();
    }
}
