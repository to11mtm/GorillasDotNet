using System.Text.Json.Serialization;
using Gorillas.Core.Ai;
using Gorillas.Core.Model;
using Gorillas.Core.Primitives;
using Gorillas.Core.Simulation;

namespace Gorillas.Core.Events;

/// <summary>
/// The single source of truth for a match. State is always the fold of this log, which is what
/// makes reconnect catch-up, spectating and post-match replay the same mechanism.
/// </summary>
/// <remarks>
/// The discriminators are a persisted wire format: once a match is stored, renaming one
/// makes old event logs unreadable. Add new derived types, never rename existing ones.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$event")]
[JsonDerivedType(typeof(GameCreated), "gameCreated")]
[JsonDerivedType(typeof(PlayerJoined), "playerJoined")]
[JsonDerivedType(typeof(PlayerLeft), "playerLeft")]
[JsonDerivedType(typeof(RoundStarted), "roundStarted")]
[JsonDerivedType(typeof(BananaThrown), "bananaThrown")]
[JsonDerivedType(typeof(BananaImpacted), "bananaImpacted")]
[JsonDerivedType(typeof(TurnAdvanced), "turnAdvanced")]
[JsonDerivedType(typeof(RoundEnded), "roundEnded")]
[JsonDerivedType(typeof(MatchEnded), "matchEnded")]
public abstract record GameEvent;

public sealed record GameCreated(string GameId, string GameCode, ulong Seed, GameSettings Settings) : GameEvent;

public sealed record PlayerJoined(int Slot, string PlayerId, string Nickname, bool IsComputer) : GameEvent
{
    /// <summary>
    /// Added after the first matches were recorded. Older logs have no value here, so it is
    /// nullable and callers fall back to <see cref="AiDifficulty.Normal"/>.
    /// </summary>
    public AiDifficulty? Difficulty { get; init; }
}

public sealed record PlayerLeft(int Slot) : GameEvent;

public sealed record RoundStarted(int RoundNumber, double Wind, int StartingSlot) : GameEvent;

public sealed record BananaThrown(int Slot, double AngleDegrees, double Velocity) : GameEvent;

public sealed record BananaImpacted(ImpactKind Kind, Vec2 Position, int? VictimSlot, double CraterRadius) : GameEvent;

public sealed record TurnAdvanced(int Slot) : GameEvent;

public sealed record RoundEnded(int RoundNumber, int? WinnerSlot) : GameEvent;

public sealed record MatchEnded(int WinnerSlot, MatchEndReason Reason) : GameEvent;

public enum MatchEndReason
{
    ScoreReached,
    Forfeit,
}
