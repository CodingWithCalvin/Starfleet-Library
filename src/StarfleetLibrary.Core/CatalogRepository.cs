namespace StarfleetLibrary.Core;

/// <summary>Validated whole-catalog storage with a same-directory atomic commit.</summary>
public sealed class CatalogRepository
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public string FilePath { get; }

    public CatalogRepository(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FilePath = Path.GetFullPath(path);
    }

    public async Task<IReadOnlyList<Book>> LoadAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > CatalogBackup.MaximumBytes)
                throw new InvalidDataException("The saved catalog exceeds the 10 MB limit.");
            var bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
            return CatalogBackup.Decode(bytes);
        }
        catch (FileNotFoundException) { return Array.Empty<Book>(); }
        catch (DirectoryNotFoundException error)
        {
            // ENOTDIR is also reported as DirectoryNotFoundException. A file where a
            // parent directory should be is a storage failure, not an empty catalog.
            for (var ancestor = Path.GetDirectoryName(FilePath); !string.IsNullOrEmpty(ancestor); ancestor = Path.GetDirectoryName(ancestor))
                if (File.Exists(ancestor)) throw new IOException("A catalog directory path is occupied by a file.", error);
            return Array.Empty<Book>();
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(IEnumerable<Book> books, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Validate and serialize the complete snapshot before touching the active file.
        var data = CatalogBackup.Encode(books);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        string? stagedPath = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            stagedPath = Path.Combine(directory, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 8192, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(data, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            // A same-filesystem rename replaces the active catalog only after the write succeeds.
            File.Move(stagedPath, FilePath, overwrite: true);
            stagedPath = null;
        }
        finally
        {
            if (stagedPath is not null)
            {
                try { File.Delete(stagedPath); }
                catch (IOException) { /* A leftover staging file never replaces the active catalog. */ }
                catch (UnauthorizedAccessException) { }
            }
            gate.Release();
        }
    }
}
