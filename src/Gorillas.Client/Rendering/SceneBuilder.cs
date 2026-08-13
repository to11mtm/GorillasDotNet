using Gorillas.Core;
using Gorillas.Core.Model;
using Gorillas.Core.Primitives;
using Gorillas.Core.Simulation;

namespace Gorillas.Client.Rendering;

public sealed record SceneBuilding(
    double X,
    double Y,
    double W,
    double H,
    int ColorIndex,
    int WindowCols,
    int WindowRows,
    IReadOnlyList<int> LitWindows);

public sealed record SceneCrater(double X, double Y, double R);

public sealed record SceneGorilla(int Slot, double X, double Y, double W, double H, string Name, bool Active, bool Defeated);

public sealed record SceneBanner(string Text, string Tone);

public sealed record GameScene(
    double Width,
    double Height,
    string Theme,
    IReadOnlyList<SceneBuilding> Buildings,
    IReadOnlyList<SceneCrater> Craters,
    IReadOnlyList<SceneGorilla> Gorillas,
    double Wind,
    double MaxWind,
    bool SunShocked,
    SceneBanner? Banner);

public sealed record ThrowAnimation(
    int Slot,
    double StepSeconds,
    IReadOnlyList<double[]> Points,
    double ImpactX,
    double ImpactY,
    double ImpactRadius,
    bool IsHit);

/// <summary>
/// Turns immutable game state into a flat payload the JS renderer can draw. Window layouts are
/// derived from the match seed so every client and every replay shows the identical city.
/// </summary>
public static class SceneBuilder
{
    private const double WindowSpacingX = 8;
    private const double WindowSpacingY = 10;

    public static GameScene Build(GameState state, string theme = "retro", SceneBanner? banner = null, int? defeatedSlot = null)
    {
        var buildings = new List<SceneBuilding>(state.Skyline.Buildings.Count);
        for (var i = 0; i < state.Skyline.Buildings.Count; i++)
        {
            buildings.Add(BuildBuilding(state, i));
        }

        var gorillas = state.Gorillas
            .Select(g => new SceneGorilla(
                g.Slot,
                g.Feet.X,
                g.Feet.Y,
                state.Settings.GorillaWidth,
                state.Settings.GorillaHeight,
                state.PlayerInSlot(g.Slot)?.Nickname ?? $"Player {g.Slot + 1}",
                state.Phase == GamePhase.Aiming && g.Slot == state.ActiveSlot,
                defeatedSlot == g.Slot))
            .ToList();

        var craters = state.Skyline.Craters
            .Select(c => new SceneCrater(c.Center.X, c.Center.Y, c.Radius))
            .ToList();

        return new GameScene(
            state.Settings.Width,
            state.Settings.Height,
            theme,
            buildings,
            craters,
            gorillas,
            state.Wind,
            state.Settings.MaxWind,
            defeatedSlot is not null,
            banner);
    }

    public static ThrowAnimation BuildThrow(GameState state, int slot, double angle, double velocity)
    {
        var trajectory = BananaSimulator.Simulate(
            state.Skyline, state.Gorillas, state.Settings, slot, angle, velocity, state.Wind);

        var radius = trajectory.Impact.Kind is ImpactKind.Building or ImpactKind.Gorilla
            ? state.Settings.ExplosionRadius
            : 0;

        return new ThrowAnimation(
            slot,
            state.Settings.TimeStep,
            [.. trajectory.Points.Select(p => new[] { p.X, p.Y })],
            trajectory.Impact.Position.X,
            trajectory.Impact.Position.Y,
            radius,
            trajectory.Impact.IsHit);
    }

    private static SceneBuilding BuildBuilding(GameState state, int index)
    {
        var building = state.Skyline.Buildings[index];
        var rng = DeterministicRandom.ForStream(state.Seed, (ulong)((state.RoundNumber * 1000) + index + 1));

        var cols = Math.Max(1, (int)((building.Width - 4) / WindowSpacingX));
        var rows = Math.Max(1, (int)((building.Height - 6) / WindowSpacingY));
        var lit = new List<int>();

        for (var i = 0; i < cols * rows; i++)
        {
            if (rng.NextDouble() < 0.45)
            {
                lit.Add(i);
            }
        }

        return new SceneBuilding(building.Left, 0, building.Width, building.Height, building.ColorIndex, cols, rows, lit);
    }
}
