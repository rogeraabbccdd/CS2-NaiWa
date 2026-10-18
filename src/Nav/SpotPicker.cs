using System.Numerics;

namespace NaiWa.Nav;

public sealed class SpotPickerOptions
{
    public float MinAreaSize { get; init; } = 2304;
    public float MaxAreaHeightDelta { get; init; } = 12;
    public float EdgeShrink { get; init; } = 0.6f;
}

/// <summary>
/// Picks random floor positions from nav areas. Areas are filtered by attributes, size and
/// flatness, then limited to those reachable from the player spawns, and sampled weighted
/// by surface so every square unit of usable floor is equally likely.
/// </summary>
public sealed class SpotPicker
{
    private const NavAttributes Rejected =
        NavAttributes.Jump | NavAttributes.Avoid | NavAttributes.Transient | NavAttributes.Stairs |
        NavAttributes.ObstacleTop | NavAttributes.NonZUp | NavAttributes.NonZUpTransition |
        NavAttributes.CrouchHeight | NavAttributes.CrawlHeight;

    private readonly NavArea[] _areas;
    private readonly double[] _cumulative;
    private readonly float _shrink;

    public int TotalAreas { get; }
    public int UsableAreas => _areas.Length;
    public bool ConnectivityFiltered { get; }

    public SpotPicker(IReadOnlyList<NavArea> areas, SpotPickerOptions options, IReadOnlyList<Vector3> spawns)
    {
        TotalAreas = areas.Count;
        _shrink = Math.Clamp(options.EdgeShrink, 0.05f, 1f);

        var hull = areas.Any(a => a.HullIndex == 0)
            ? (byte)0
            : areas.GroupBy(a => a.HullIndex).MaxBy(g => g.Count())?.Key ?? 0;
        var walkable = areas.Where(a => a.HullIndex == hull && !a.OnMovableMesh && a.Corners.Length >= 3).ToList();

        var reachable = Reachable(walkable, spawns);
        ConnectivityFiltered = reachable != null;

        _areas = walkable
            .Where(a => reachable == null || reachable.Contains(a.Id))
            .Where(a => (a.Attributes & Rejected) == 0)
            .Where(a => HeightDelta(a) <= options.MaxAreaHeightDelta)
            .Where(a => Area2D(a.Corners) >= options.MinAreaSize)
            .ToArray();

        _cumulative = new double[_areas.Length];
        double sum = 0;
        for (var i = 0; i < _areas.Length; i++)
            _cumulative[i] = sum += Area2D(_areas[i].Corners);
    }

    /// <summary>Tries random positions until <paramref name="accept"/> agrees.</summary>
    public bool TryPick(Random rng, Func<Vector3, bool> accept, int attempts, out Vector3 position)
    {
        position = default;
        if (_areas.Length == 0)
            return false;

        for (var i = 0; i < attempts; i++)
        {
            var area = _areas[WeightedIndex(rng)];
            var candidate = RandomPointIn(area.Corners, rng);
            if (accept(candidate))
            {
                position = candidate;
                return true;
            }
        }
        return false;
    }

    private int WeightedIndex(Random rng)
    {
        var target = rng.NextDouble() * _cumulative[^1];
        var i = Array.BinarySearch(_cumulative, target);
        return Math.Min(i < 0 ? ~i : i, _cumulative.Length - 1);
    }

    /// <summary>Uniform point in a convex polygon, pulled toward the centroid to stay clear of walls.</summary>
    private Vector3 RandomPointIn(Vector3[] c, Random rng)
    {
        var total = 0.0;
        var weights = new double[c.Length - 2];
        for (var i = 1; i < c.Length - 1; i++)
            weights[i - 1] = total += TriangleArea2D(c[0], c[i], c[i + 1]);

        var target = rng.NextDouble() * total;
        var t = Array.FindIndex(weights, w => w >= target);
        if (t < 0) t = weights.Length - 1;
        Vector3 a = c[0], b = c[t + 1], d = c[t + 2];

        var u = rng.NextSingle();
        var v = rng.NextSingle();
        if (u + v > 1) { u = 1 - u; v = 1 - v; }
        var point = a + (b - a) * u + (d - a) * v;

        var centroid = Vector3.Zero;
        foreach (var p in c) centroid += p;
        centroid /= c.Length;
        return centroid + (point - centroid) * _shrink;
    }

    private static HashSet<uint>? Reachable(List<NavArea> areas, IReadOnlyList<Vector3> spawns)
    {
        if (spawns.Count == 0 || areas.Count == 0)
            return null;

        var byId = areas.ToDictionary(a => a.Id);
        var adjacency = areas.ToDictionary(a => a.Id, _ => new List<uint>());
        foreach (var a in areas)
        {
            foreach (var n in a.Neighbors)
            {
                if (!byId.ContainsKey(n)) continue;
                adjacency[a.Id].Add(n);
                adjacency[n].Add(a.Id); // connections are one-way (drops); treat as two-way
            }
        }

        var seen = new HashSet<uint>();
        var queue = new Queue<uint>();
        foreach (var spawn in spawns)
        {
            var seed = AreaAt(areas, spawn);
            if (seed != null && seen.Add(seed.Id))
                queue.Enqueue(seed.Id);
        }
        if (queue.Count == 0)
            return null;

        while (queue.Count > 0)
            foreach (var n in adjacency[queue.Dequeue()])
                if (seen.Add(n))
                    queue.Enqueue(n);

        return seen;
    }

    private static NavArea? AreaAt(List<NavArea> areas, Vector3 p)
    {
        NavArea? best = null;
        var bestScore = float.MaxValue;
        foreach (var a in areas)
        {
            var z = a.Corners.Average(c => c.Z);
            var dz = Math.Abs(p.Z - z);
            float score;
            if (Contains2D(a.Corners, p) && dz < 72)
            {
                score = dz;
            }
            else
            {
                var centroid = new Vector3(a.Corners.Average(c => c.X), a.Corners.Average(c => c.Y), z);
                var dist = Vector3.Distance(centroid, p);
                if (dist > 256) continue;
                score = 1000 + dist;
            }
            if (score < bestScore)
            {
                bestScore = score;
                best = a;
            }
        }
        return best;
    }

    private static bool Contains2D(Vector3[] poly, Vector3 p)
    {
        var inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        }
        return inside;
    }

    private static float HeightDelta(NavArea a) => a.Corners.Max(c => c.Z) - a.Corners.Min(c => c.Z);

    private static double Area2D(Vector3[] c)
    {
        double sum = 0;
        for (int i = 0, j = c.Length - 1; i < c.Length; j = i++)
            sum += (double)c[j].X * c[i].Y - (double)c[i].X * c[j].Y;
        return Math.Abs(sum) / 2;
    }

    private static double TriangleArea2D(Vector3 a, Vector3 b, Vector3 c) =>
        Math.Abs((double)(b.X - a.X) * (c.Y - a.Y) - (double)(c.X - a.X) * (b.Y - a.Y)) / 2;
}
