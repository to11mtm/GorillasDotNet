using Akka.Configuration;
using Akka.Persistence.Sql;
using Gorillas.Actors;
using Microsoft.Data.Sqlite;

namespace Gorillas.Actors.Tests;

/// <summary>
/// An isolated Akka.Persistence.Sql journal + snapshot store on a throwaway SQLite file.
/// </summary>
public sealed class TempJournal : IDisposable
{
    private TempJournal(string path)
    {
        DatabasePath = path;
        ConnectionString = $"Data Source={path}";
    }

    public string DatabasePath { get; }

    public string ConnectionString { get; }

    /// <summary>
    /// HOCON treats backslashes as escape sequences, so a Windows path such as
    /// <c>C:\Users\...</c> fails to parse ("Unknown escape code: U") unless escaped.
    /// </summary>
    private string HoconConnectionString => ConnectionString.Replace("\\", "\\\\");

    public static TempJournal Create() =>
        new(Path.Combine(Path.GetTempPath(), $"gorillas-journal-{Guid.NewGuid():n}.db"));

    /// <summary>
    /// The plugin's own defaults must be layered underneath, otherwise
    /// <c>akka.persistence.journal.sql</c> has no <c>class</c> and every persist silently
    /// never completes.
    /// </summary>
    public Config Config => ConfigurationFactory.ParseString($$"""
        akka.persistence {
          journal {
            plugin = "akka.persistence.journal.sql"
            sql {
              connection-string = "{{HoconConnectionString}}"
              provider-name = "SQLite.MS"
              auto-initialize = true
            }
          }
          snapshot-store {
            plugin = "akka.persistence.snapshot-store.sql"
            sql {
              connection-string = "{{HoconConnectionString}}"
              provider-name = "SQLite.MS"
              auto-initialize = true
            }
          }
        }
        akka.loglevel = WARNING
        """)
        .WithFallback(GorillasSerialization.Config)
        .WithFallback(SqlPersistence.DefaultConfiguration);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var file in new[] { DatabasePath, $"{DatabasePath}-wal", $"{DatabasePath}-shm" })
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Handle still closing; the temp directory gets cleaned up regardless.
            }
        }
    }
}
