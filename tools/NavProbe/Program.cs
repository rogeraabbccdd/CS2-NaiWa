using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using NaiWa.Nav;

// Usage: NavProbe <officialMapsDir> <workshopRoot> <outDir> <map>...
var locator = new MapNavLocator([args[0]], [args[1]], Path.Combine(args[2], "vpk_index.json"), Console.WriteLine);
var rng = new Random(1);

foreach (var map in args.Skip(3))
{
    var sw = Stopwatch.StartNew();
    var result = locator.Find(map);
    if (result == null)
    {
        Console.WriteLine($"{map}: no nav");
        continue;
    }

    var picker = new SpotPicker(result.Areas, new SpotPickerOptions(), []);
    var points = new List<Vector3>();
    for (var i = 0; i < 200 && picker.TryPick(rng, _ => true, 1, out var p); i++)
        points.Add(p);
    Console.WriteLine($"{map}: {result.Areas.Count} areas, {picker.UsableAreas} usable, {sw.ElapsedMilliseconds} ms, from {result.Source}");

    // CSV for plotting: A,<usable 0/1>,x,y,x,y...  /  P,x,y
    var inv = CultureInfo.InvariantCulture;
    using var w = new StreamWriter(Path.Combine(args[2], map + ".csv"));
    foreach (var a in result.Areas)
        w.WriteLine("A," + string.Join(",", a.Corners.Select(c => $"{c.X.ToString(inv)},{c.Y.ToString(inv)}")));
    foreach (var p in points)
        w.WriteLine($"P,{p.X.ToString(inv)},{p.Y.ToString(inv)}");
}
