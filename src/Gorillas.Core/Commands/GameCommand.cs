using Gorillas.Core.Ai;
using Gorillas.Core.Model;

namespace Gorillas.Core.Commands;

public abstract record GameCommand;

public sealed record CreateGame(string GameId, string GameCode, GameSettings Settings) : GameCommand;

public sealed record JoinGame(string PlayerId, string Nickname, bool IsComputer = false, AiDifficulty? Difficulty = null) : GameCommand;

public sealed record ThrowBanana(int Slot, double AngleDegrees, double Velocity) : GameCommand;

public sealed record Forfeit(int Slot) : GameCommand;

public sealed record StartNextRound : GameCommand;
