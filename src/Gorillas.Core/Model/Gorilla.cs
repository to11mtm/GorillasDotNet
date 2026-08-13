using Gorillas.Core.Primitives;

namespace Gorillas.Core.Model;

/// <summary>Position is the gorilla's feet: the centre of the rooftop it stands on.</summary>
public sealed record Gorilla(int Slot, Vec2 Feet, int BuildingIndex)
{
    public double Left(GameSettings settings) => Feet.X - (settings.GorillaWidth / 2);

    public double Right(GameSettings settings) => Feet.X + (settings.GorillaWidth / 2);

    public double Top(GameSettings settings) => Feet.Y + settings.GorillaHeight;

    public bool Contains(GameSettings settings, Vec2 point) =>
        point.X >= Left(settings) &&
        point.X <= Right(settings) &&
        point.Y >= Feet.Y &&
        point.Y <= Top(settings);

    /// <summary>Bananas leave from just above and in front of the gorilla, never from inside it.</summary>
    public Vec2 ThrowOrigin(GameSettings settings, int direction) =>
        new(Feet.X + (direction * ((settings.GorillaWidth / 2) + 2)), Feet.Y + settings.GorillaHeight + 3);
}
