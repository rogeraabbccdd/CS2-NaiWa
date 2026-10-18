using System.Numerics;
using System.Text.Json;

namespace NaiWa;

public sealed class MapSpot
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }

    public Vector3 Position => new(X, Y, Z);
}

public sealed class MapBan
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; }

    public bool Covers(Vector3 p) => Vector3.Distance(new Vector3(X, Y, Z), p) <= Radius;
}

/// <summary>Per-map admin edits, saved as maps/&lt;map&gt;.json in the plugin config folder.</summary>
public sealed class MapSpots
{
    public bool Disabled { get; set; }
    public List<MapSpot> Spots { get; set; } = [];
    public List<MapBan> Bans { get; set; } = [];

    public static MapSpots Load(string dir, string map)
    {
        var path = PathFor(dir, map);
        if (!File.Exists(path))
            return new MapSpots();
        return JsonSerializer.Deserialize<MapSpots>(File.ReadAllText(path)) ?? new MapSpots();
    }

    public void Save(string dir, string map)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(PathFor(dir, map), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string PathFor(string dir, string map) => Path.Combine(dir, map + ".json");
}
