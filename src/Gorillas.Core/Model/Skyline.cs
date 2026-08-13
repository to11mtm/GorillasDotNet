using Gorillas.Core.Primitives;

namespace Gorillas.Core.Model;

public sealed record Building(double Left, double Width, double Height, int ColorIndex)
{
    public double Right => Left + Width;

    public double CenterX => Left + (Width / 2);

    public bool ContainsPoint(Vec2 point) =>
        point.X >= Left && point.X <= Right && point.Y >= 0 && point.Y <= Height;
}

public readonly record struct Crater(Vec2 Center, double Radius)
{
    public bool Contains(Vec2 point) => point.DistanceTo(Center) <= Radius;
}

public sealed record Skyline(IReadOnlyList<Building> Buildings, IReadOnlyList<Crater> Craters)
{
    public static Skyline Empty { get; } = new([], []);

    public Skyline WithCrater(Crater crater) => this with { Craters = [.. Craters, crater] };

    public bool Equals(Skyline? other) =>
        other is not null &&
        StructuralEquality.SequenceEqual(Buildings, other.Buildings) &&
        StructuralEquality.SequenceEqual(Craters, other.Craters);

    public override int GetHashCode() =>
        HashCode.Combine(StructuralEquality.SequenceHash(Buildings), StructuralEquality.SequenceHash(Craters));

    /// <summary>Solid means inside a building and not carved away by an explosion.</summary>
    public bool IsSolidAt(Vec2 point)
    {
        var inBuilding = false;
        for (var i = 0; i < Buildings.Count; i++)
        {
            if (Buildings[i].ContainsPoint(point))
            {
                inBuilding = true;
                break;
            }
        }

        if (!inBuilding)
        {
            return false;
        }

        for (var i = 0; i < Craters.Count; i++)
        {
            if (Craters[i].Contains(point))
            {
                return false;
            }
        }

        return true;
    }
}
