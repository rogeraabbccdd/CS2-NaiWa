using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using SteamDatabase.ValvePak;

namespace NaiWa.Nav;

/// <summary>
/// Finds the .nav file of a map, either in an official map VPK or inside a workshop
/// addon VPK (where the map VPK is nested at maps/&lt;map&gt;.vpk).
/// Workshop lookups go through a map name -> VPK index that is rebuilt automatically
/// when a map is missing or its VPK moved.
/// </summary>
public sealed partial class MapNavLocator(
    IReadOnlyList<string> officialMapDirs,
    IReadOnlyList<string> workshopRoots,
    string cachePath,
    Action<string> log)
{
    private sealed record IndexEntry(string Vpk, string Entry);

    private Dictionary<string, IndexEntry>? _index;
    private readonly Lock _lock = new();

    public sealed record Result(List<NavArea> Areas, string Source);

    public Result? Find(string map)
    {
        lock (_lock)
        {
            foreach (var dir in officialMapDirs)
            {
                var vpk = Path.Combine(dir, map + ".vpk");
                if (File.Exists(vpk) && TryRead(vpk, $"maps/{map}.nav", map) is { } areas)
                    return new Result(areas, vpk);
            }

            _index ??= LoadCache();
            if (_index.TryGetValue(map, out var cached) && TryRead(cached, map) is { } cachedAreas)
                return new Result(cachedAreas, cached.Vpk);

            Rescan();
            if (_index.TryGetValue(map, out var entry) && TryRead(entry, map) is { } freshAreas)
                return new Result(freshAreas, entry.Vpk);

            return null;
        }
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _index = null;
            try { File.Delete(cachePath); } catch (IOException) { }
        }
    }

    private List<NavArea>? TryRead(IndexEntry entry, string map) =>
        File.Exists(entry.Vpk) ? TryRead(entry.Vpk, entry.Entry, map) : null;

    /// <param name="entryPath">maps/&lt;map&gt;.nav directly, or a nested maps/&lt;map&gt;.vpk</param>
    private List<NavArea>? TryRead(string vpkPath, string entryPath, string map)
    {
        try
        {
            using var pak = new Package();
            pak.Read(vpkPath);

            var entry = pak.FindEntry(entryPath);
            if (entry == null)
                return null;

            if (entry.TypeName == "nav")
                return ReadNav(pak, entry);

            using var innerStream = pak.GetMemoryMappedStreamIfPossible(entry);
            using var inner = new Package();
            inner.SetFileName(map + ".vpk");
            inner.Read(innerStream);

            var nav = inner.FindEntry($"maps/{map}.nav");
            return nav == null ? null : ReadNav(inner, nav);
        }
        catch (Exception e)
        {
            log($"failed to read nav from {vpkPath}:{entryPath}: {e.Message}");
            return null;
        }
    }

    private static List<NavArea> ReadNav(Package pak, PackageEntry entry)
    {
        pak.ReadEntry(entry, out var data, validateCrc: false);
        using var ms = new MemoryStream(data, writable: false);
        return NavMeshReader.Read(ms);
    }

    private void Rescan()
    {
        var sw = Stopwatch.StartNew();
        var index = new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);
        var scanned = 0;

        foreach (var root in workshopRoots.Where(Directory.Exists))
        {
            foreach (var itemDir in Directory.EnumerateDirectories(root))
            {
                foreach (var vpk in AddonVpks(itemDir))
                {
                    scanned++;
                    try
                    {
                        using var pak = new Package();
                        pak.Read(vpk);
                        foreach (var type in (string[])["vpk", "nav"])
                        {
                            if (pak.Entries == null || !pak.Entries.TryGetValue(type, out var entries))
                                continue;
                            foreach (var e in entries.Where(e => e.DirectoryName == "maps"))
                                index.TryAdd(e.FileName, new IndexEntry(vpk, e.GetFullPath()));
                        }
                    }
                    catch (Exception e)
                    {
                        log($"skipping {vpk}: {e.Message}");
                    }
                }
            }
        }

        _index = index;
        log($"indexed {index.Count} maps from {scanned} workshop VPKs in {sw.ElapsedMilliseconds} ms");
        SaveCache();
    }

    /// <summary>Directory file of split VPKs, otherwise standalone VPKs, skipping the numbered chunks.</summary>
    private static IEnumerable<string> AddonVpks(string dir)
    {
        var dirVpks = Directory.GetFiles(dir, "*_dir.vpk");
        if (dirVpks.Length > 0)
            return dirVpks;
        return Directory.GetFiles(dir, "*.vpk").Where(f => !ChunkVpk().IsMatch(f));
    }

    [GeneratedRegex(@"_\d{3}\.vpk$", RegexOptions.IgnoreCase)]
    private static partial Regex ChunkVpk();

    private Dictionary<string, IndexEntry> LoadCache()
    {
        try
        {
            if (File.Exists(cachePath))
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, IndexEntry>>(File.ReadAllText(cachePath));
                if (data != null)
                    return new Dictionary<string, IndexEntry>(data, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception e)
        {
            log($"ignoring broken index cache: {e.Message}");
        }
        return new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);
    }

    private void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllText(cachePath, JsonSerializer.Serialize(_index, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            log($"failed to save index cache: {e.Message}");
        }
    }
}
