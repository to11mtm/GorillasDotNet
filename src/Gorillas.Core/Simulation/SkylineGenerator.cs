using Gorillas.Core.Model;
using Gorillas.Core.Primitives;

namespace Gorillas.Core.Simulation;

/// <summary>
/// Builds the city and places the gorillas. Pure function of (settings, seed, round):
/// every client regenerates the identical skyline without it ever crossing the wire.
/// </summary>
public static class SkylineGenerator
{
    public const int PaletteSize = 3;

    public static Skyline Generate(GameSettings settings, ulong seed, int round)
    {
        var rng = DeterministicRandom.ForStream(seed, (ulong)round);
        var count = rng.NextInt(settings.MinBuildings, settings.MaxBuildings + 1);
        var averageWidth = settings.Width / count;
        var minWidth = averageWidth * 0.7;
        var maxWidth = averageWidth * 1.3;

        var buildings = new List<Building>(count);
        var left = 0.0;
        for (var i = 0; i < count; i++)
        {
            var remaining = count - i;
            var width = remaining == 1
                ? settings.Width - left
                : Math.Min(rng.NextDouble(minWidth, maxWidth), settings.Width - left - (minWidth * (remaining - 1)));

            var height = rng.NextDouble(settings.MinBuildingHeight, settings.MaxBuildingHeight);
            buildings.Add(new Building(left, width, height, rng.NextInt(0, PaletteSize)));
            left += width;
        }

        return new Skyline(buildings, []);
    }

    /// <summary>Gorillas always stand on the second and second-to-last buildings, as in the original.</summary>
    public static IReadOnlyList<Gorilla> PlaceGorillas(Skyline skyline)
    {
        var last = skyline.Buildings.Count - 1;
        var leftIndex = Math.Min(1, last);
        var rightIndex = Math.Max(last - 1, leftIndex);

        return
        [
            MakeGorilla(0, skyline, leftIndex),
            MakeGorilla(1, skyline, rightIndex),
        ];
    }

    private static Gorilla MakeGorilla(int slot, Skyline skyline, int buildingIndex)
    {
        var building = skyline.Buildings[buildingIndex];
        return new Gorilla(slot, new Vec2(building.CenterX, building.Height), buildingIndex);
    }
}
