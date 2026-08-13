using Gorillas.Client.Rendering;
using Gorillas.Core;
using Gorillas.Core.Model;

namespace Gorillas.Client.Game;

/// <summary>
/// What the board needs from a match, whether it is played on one keyboard or across the
/// network. Both implementations hold impact events back until the flight animation finishes,
/// so the UI never reveals the outcome early.
/// </summary>
public interface IGameSession
{
    GameState State { get; }

    GameSettings Settings { get; }

    string? GameCode { get; }

    int? MySlot { get; }

    bool IsSpectator { get; }

    int? DefeatedSlot { get; }

    string? LastError { get; }

    /// <summary>True when this client may act right now.</summary>
    bool CanAct { get; }

    event Action? Changed;

    event Action<ThrowAnimation>? ThrowStarted;

    Task ThrowAsync(double angleDegrees, double velocity);

    /// <summary>Called by the board once the banana has landed on screen.</summary>
    void CompleteThrow();

    Task NextRoundAsync();
}
