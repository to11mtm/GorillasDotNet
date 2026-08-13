using LinqToDB.Mapping;

namespace Gorillas.Data.Entities;

[Table("matches")]
public sealed class MatchRow
{
    [PrimaryKey]
    [Column("id", Length = 64)]
    public string Id { get; set; } = string.Empty;

    [Column("code", Length = 16)]
    public string Code { get; set; } = string.Empty;

    /// <summary>SQLite has no unsigned integers, so the seed is stored as a reinterpreted long.</summary>
    [Column("seed")]
    public long Seed { get; set; }

    [Column("settings_json")]
    public string SettingsJson { get; set; } = string.Empty;

    [Column("status", Length = 16)]
    public string Status { get; set; } = string.Empty;

    [Column("winner_slot")]
    public int? WinnerSlot { get; set; }

    [Column("last_sequence")]
    public long LastSequence { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }
}

[Table("match_players")]
public sealed class MatchPlayerRow
{
    [PrimaryKey(0)]
    [Column("match_id", Length = 64)]
    public string MatchId { get; set; } = string.Empty;

    [PrimaryKey(1)]
    [Column("slot")]
    public int Slot { get; set; }

    [Column("player_id", Length = 64)]
    public string PlayerId { get; set; } = string.Empty;

    [Column("nickname", Length = 32)]
    public string Nickname { get; set; } = string.Empty;

    [Column("is_computer")]
    public bool IsComputer { get; set; }

    [Column("joined_at")]
    public DateTime JoinedAt { get; set; }
}

[Table("match_events")]
public sealed class MatchEventRow
{
    [PrimaryKey(0)]
    [Column("match_id", Length = 64)]
    public string MatchId { get; set; } = string.Empty;

    /// <summary>1-based, gapless. Doubles as the resync cursor clients send on reconnect.</summary>
    [PrimaryKey(1)]
    [Column("sequence")]
    public long Sequence { get; set; }

    [Column("type", Length = 32)]
    public string Type { get; set; } = string.Empty;

    [Column("payload_json")]
    public string PayloadJson { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
