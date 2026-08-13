namespace Gorillas.Core.Model;

/// <summary>
/// Tunable rules of a match. Stored on <c>GameCreated</c> so a replay uses the exact
/// values the match was played with, even if the defaults change later.
/// </summary>
public sealed record GameSettings
{
    public static GameSettings Default { get; } = new();

    /// <summary>Virtual play-field width. Rendering upscales from this.</summary>
    public double Width { get; init; } = 320;

    public double Height { get; init; } = 200;

    /// <summary>Downward acceleration in world units per second squared.</summary>
    public double Gravity { get; init; } = 30;

    /// <summary>Maximum absolute horizontal wind acceleration.</summary>
    public double MaxWind { get; init; } = 8;

    public int RoundsToWin { get; init; } = 3;

    public double BananaRadius { get; init; } = 2;

    public double ExplosionRadius { get; init; } = 12;

    public double GorillaWidth { get; init; } = 14;

    public double GorillaHeight { get; init; } = 14;

    /// <summary>Fixed integration step. Never derived from frame time — determinism depends on it.</summary>
    public double TimeStep { get; init; } = 1.0 / 120.0;

    public double MaxFlightSeconds { get; init; } = 30;

    /// <summary>How far past the edges a banana may travel before it is considered lost.</summary>
    public double OutOfBoundsMargin { get; init; } = 40;

    public int MinBuildings { get; init; } = 7;

    public int MaxBuildings { get; init; } = 11;

    public double MinBuildingHeight { get; init; } = 24;

    public double MaxBuildingHeight { get; init; } = 130;

    public double MaxVelocity { get; init; } = 200;
}
