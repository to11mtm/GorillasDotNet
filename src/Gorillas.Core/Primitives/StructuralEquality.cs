namespace Gorillas.Core.Primitives;

/// <summary>
/// Record equality compares collection members by reference, which would make two
/// identically-replayed states look different. Game state must compare by value.
/// </summary>
public static class StructuralEquality
{
    public static bool SequenceEqual<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < left.Count; i++)
        {
            if (!comparer.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static int SequenceHash<T>(IReadOnlyList<T>? items)
    {
        if (items is null)
        {
            return 0;
        }

        var hash = new HashCode();
        hash.Add(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            hash.Add(items[i]);
        }

        return hash.ToHashCode();
    }
}
