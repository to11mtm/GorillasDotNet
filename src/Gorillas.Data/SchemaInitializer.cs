using Gorillas.Data.Entities;
using LinqToDB;
using LinqToDB.Data;

namespace Gorillas.Data;

/// <summary>
/// Creates the schema on startup. Deliberately idempotent rather than a migration framework —
/// the schema is tiny and append-only, and this keeps deployment to "copy and run".
/// </summary>
public static class SchemaInitializer
{
    public static async Task EnsureCreatedAsync(GorillasDataConnection db, CancellationToken ct = default)
    {
        // WAL is enabled first so all schema objects are written under the same journal mode,
        // and so spectators can read the log while a match is being written to.
        await db.ExecuteAsync("PRAGMA journal_mode=WAL;");

        await db.CreateTableAsync<MatchRow>(tableOptions: TableOptions.CreateIfNotExists, token: ct);
        await db.CreateTableAsync<MatchPlayerRow>(tableOptions: TableOptions.CreateIfNotExists, token: ct);
        await db.CreateTableAsync<MatchEventRow>(tableOptions: TableOptions.CreateIfNotExists, token: ct);

        await db.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS ux_matches_code ON matches (code);");
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_matches_status ON matches (status, created_at DESC);");
        await db.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_match_players_player ON match_players (player_id);");
    }
}
