using Gorillas.Data.Entities;
using LinqToDB;
using LinqToDB.Data;

namespace Gorillas.Data;

public class GorillasDataConnection : DataConnection
{
    public GorillasDataConnection(DataOptions<GorillasDataConnection> options)
        : base(options.Options)
    {
    }

    public ITable<MatchRow> Matches => this.GetTable<MatchRow>();

    public ITable<MatchPlayerRow> MatchPlayers => this.GetTable<MatchPlayerRow>();

    public ITable<MatchEventRow> MatchEvents => this.GetTable<MatchEventRow>();
}
