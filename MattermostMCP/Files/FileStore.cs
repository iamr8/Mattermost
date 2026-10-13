using System.Buffers;
using System.Text.RegularExpressions;

namespace MattermostMcp.Files;

public sealed record SavedFile(string Path, long Bytes);

public sealed class FileTooLargeException : Exception;

/// <summary>
/// Saves a stream into one folder the server owns. The caller gives a base name and an extension; both are
/// checked here, so a name from outside can never choose the path. It knows nothing about Mattermost.
/// </summary>
public sealed partial class FileStore(string folder, long maxBytes)
{
    private const UnixFileMode FolderMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>
    /// Writes to a temporary name, counting the bytes, then moves it to <c>name.extension</c>. A failed or too big
    /// stream leaves nothing behind.
    /// </summary>
    public async Task<SavedFile> SaveAsync(string name, string? extension, Stream content, CancellationToken cancellationToken)
    {
        if (!BaseName().IsMatch(name))
        {
            throw new ArgumentException("unsafe file name", nameof(name));
        }

        Directory.CreateDirectory(folder);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(folder, FolderMode);
        }

        var finalPath = Path.Combine(folder, $"{name}.{SafeExtension(extension)}");
        var tempPath = Path.Combine(folder, $".{name}.{Guid.NewGuid():N}.part");
        var moved = false;
        try
        {
            long total = 0;
            var options = new FileStreamOptions { Mode = System.IO.FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = FileMode;
            }

            await using (var output = new FileStream(tempPath, options))
            {
                var buffer = ArrayPool<byte>.Shared.Rent(81920);
                try
                {
                    int read;
                    while ((read = await content.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
                    {
                        total += read;
                        if (total > maxBytes)
                        {
                            throw new FileTooLargeException();
                        }

                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            File.Move(tempPath, finalPath, overwrite: true);
            moved = true;
            return new SavedFile(finalPath, total);
        }
        finally
        {
            if (!moved)
            {
                File.Delete(tempPath);
            }
        }
    }

    // ".png" and "PDF" are fine. Anything with a slash, a second dot or more than 10 characters becomes "bin".
    private static string SafeExtension(string? extension)
    {
        var value = (extension ?? "").TrimStart('.').ToLowerInvariant();
        return Extension().IsMatch(value) ? value : "bin";
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex BaseName();

    [GeneratedRegex("^[a-z0-9]{1,10}$")]
    private static partial Regex Extension();
}
