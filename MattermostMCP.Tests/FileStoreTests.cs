using System.Text;
using MattermostMcp.Files;

namespace MattermostMcp.Tests;

public sealed class FileStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mm-mcp-filestore-" + Guid.NewGuid().ToString("N"), "downloads");

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_folder)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    private string[] FilesOnDisk() => Directory.Exists(_folder) ? Directory.GetFileSystemEntries(_folder) : [];

    // Break: write to a path built from the server name, create files group-readable, or mark them executable.
    [Fact]
    public async Task Saves_the_bytes_under_id_dot_extension_with_private_modes()
    {
        var store = new FileStore(_folder, 1024);

        var saved = await store.SaveAsync("abc123", "pdf", Bytes("hello"), TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_folder, "abc123.pdf"), saved.Path);
        Assert.Equal(5, saved.Bytes);
        Assert.Equal("hello", await File.ReadAllTextAsync(saved.Path, TestContext.Current.CancellationToken));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(saved.Path));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(_folder));
        }
    }

    // Break: tighten the folder only when it is created, so an old folder with wide modes stays open.
    [Fact]
    public async Task An_existing_folder_with_wide_modes_is_made_private()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_folder);
        File.SetUnixFileMode(_folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        var store = new FileStore(_folder, 1024);

        await store.SaveAsync("abc123", "txt", Bytes("x"), TestContext.Current.CancellationToken);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(_folder));
    }

    // Break: use the extension as given, so the model or the server controls part of the path.
    [Theory]
    [InlineData("PDF", "abc123.pdf")]
    [InlineData(".png", "abc123.png")]
    [InlineData("a/b", "abc123.bin")]
    [InlineData("..", "abc123.bin")]
    [InlineData("../x", "abc123.bin")]
    [InlineData("tar.gz", "abc123.bin")]
    [InlineData("", "abc123.bin")]
    [InlineData(null, "abc123.bin")]
    [InlineData("abcdefghijk", "abc123.bin")]
    [InlineData("abcdefghij", "abc123.abcdefghij")]
    public async Task The_extension_is_lower_case_and_safe_or_bin(string? extension, string expectedName)
    {
        var store = new FileStore(_folder, 1024);

        var saved = await store.SaveAsync("abc123", extension, Bytes("x"), TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_folder, expectedName), saved.Path);
    }

    // Break: accept any base name, so "../x" climbs out of the folder.
    [Theory]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("a b")]
    public async Task An_unsafe_base_name_is_refused_before_anything_is_written(string name)
    {
        var store = new FileStore(_folder, 1024);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(name, "txt", Bytes("x"), TestContext.Current.CancellationToken));

        Assert.Empty(FilesOnDisk());
    }

    // Break: trust the stream length, or forget the count while writing, so an endless body fills the disk.
    [Fact]
    public async Task A_stream_over_the_limit_is_stopped_and_leaves_nothing()
    {
        var store = new FileStore(_folder, 1024);

        await Assert.ThrowsAsync<FileTooLargeException>(() => store.SaveAsync("abc123", "bin", new EndlessStream(), TestContext.Current.CancellationToken));

        Assert.Empty(FilesOnDisk());
    }

    // Break: use "<" instead of "<=" at the limit.
    [Fact]
    public async Task A_stream_of_exactly_the_limit_is_saved()
    {
        var store = new FileStore(_folder, 1024);

        var saved = await store.SaveAsync("abc123", "bin", new MemoryStream(new byte[1024]), TestContext.Current.CancellationToken);

        Assert.Equal(1024, saved.Bytes);
        Assert.Single(FilesOnDisk());
    }

    // Break: write straight to the final name, so a failed stream leaves a half file.
    [Fact]
    public async Task A_stream_that_fails_half_way_leaves_nothing()
    {
        var store = new FileStore(_folder, 1024);

        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync("abc123", "bin", new FailingStream(), TestContext.Current.CancellationToken));

        Assert.Empty(FilesOnDisk());
    }

    // Break: write the new content to a second file, or keep the first one when the second fails.
    [Fact]
    public async Task The_same_name_twice_keeps_one_file_with_the_second_content()
    {
        var store = new FileStore(_folder, 1024);

        await store.SaveAsync("abc123", "txt", Bytes("first"), TestContext.Current.CancellationToken);
        var second = await store.SaveAsync("abc123", "txt", Bytes("second!"), TestContext.Current.CancellationToken);

        Assert.Equal("second!", await File.ReadAllTextAsync(second.Path, TestContext.Current.CancellationToken));
        Assert.Single(FilesOnDisk());
    }

    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => count;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(buffer.Length);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FailingStream : Stream
    {
        private int _reads;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _reads++ == 0 ? ValueTask.FromResult(10) : throw new IOException("connection lost");

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
