using Gorillas.Data;
using LinqToDB;
using Microsoft.Data.Sqlite;

namespace Gorillas.Data.Tests;

/// <summary>
/// A throwaway SQLite file per test. A real file (not :memory:) is used deliberately so the
/// tests exercise the same journal mode, locking and type mapping as production.
/// </summary>
public sealed class TempDatabase : IAsyncDisposable
{
    private readonly string _path;

    private TempDatabase(string path, GorillasDataConnection connection)
    {
        _path = path;
        Connection = connection;
        Store = new MatchStore(connection);
    }

    public GorillasDataConnection Connection { get; }

    public IMatchStore Store { get; }

    public string ConnectionString => $"Data Source={_path}";

    public static async Task<TempDatabase> CreateAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gorillas-test-{Guid.NewGuid():n}.db");
        var connection = Connect(path);
        await SchemaInitializer.EnsureCreatedAsync(connection);
        return new TempDatabase(path, connection);
    }

    /// <summary>Opens a second connection to the same file, to simulate a competing writer.</summary>
    public GorillasDataConnection OpenSecondConnection() => Connect(_path);

    private static GorillasDataConnection Connect(string path) =>
        new(new DataOptions<GorillasDataConnection>(new DataOptions().UseSQLite($"Data Source={path}")));

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
        SqliteConnection.ClearAllPools();

        foreach (var file in new[] { _path, $"{_path}-wal", $"{_path}-shm" })
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // A pooled handle may still be closing; the temp directory will be cleaned up anyway.
            }
        }
    }
}
