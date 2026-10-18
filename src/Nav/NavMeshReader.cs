using System.Numerics;
using System.Text;

namespace NaiWa.Nav;

[Flags]
public enum NavAttributes : long
{
    None = 0,
    Jump = 0x2,
    NoJump = 0x8,
    Stop = 0x10,
    Run = 0x20,
    Walk = 0x40,
    Avoid = 0x80,
    Transient = 0x100,
    DontHide = 0x200,
    Stand = 0x400,
    NoHostages = 0x800,
    Stairs = 0x1000,
    NoMerge = 0x2000,
    ObstacleTop = 0x4000,
    NonZUp = 0x8000,
    CrouchHeight = 0x10000,
    NonZUpTransition = 0x20000,
    CrawlHeight = 0x40000,
}

public sealed class NavArea
{
    public uint Id { get; init; }
    public NavAttributes Attributes { get; init; }
    public byte HullIndex { get; init; }
    public bool OnMovableMesh { get; init; }
    public required Vector3[] Corners { get; init; }
    public required uint[] Neighbors { get; init; }
}

/// <summary>
/// Minimal reader for CS2 .nav files (versions 30-36). Only reads what is needed to
/// pick positions: area polygons, attributes and connections. Format reference:
/// ValveResourceFormat/NavMesh (MIT).
/// </summary>
public static class NavMeshReader
{
    private const uint Magic = 0xFEEDFACE;
    private const uint NoMovableMesh = 0xFFFFFFFF;

    public static List<NavArea> Read(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        if (r.ReadUInt32() != Magic)
            throw new InvalidDataException("not a nav file");

        var version = r.ReadUInt32();
        if (version is < 30 or > 36)
            throw new InvalidDataException($"unsupported nav version {version}");

        r.ReadUInt32(); // sub version
        r.ReadByte();   // is analyzed

        var placeCount = r.ReadUInt16();
        for (var i = 0; i < placeCount; i++)
            r.BaseStream.Seek(r.ReadUInt16(), SeekOrigin.Current);
        r.ReadByte(); // has unnamed areas

        if (version >= 36)
            SkipKV3(r); // movable mesh settings

        (Vector3[] Corners, bool Movable)[]? polygons = null;
        if (version >= 31)
            polygons = ReadPolygons(r, version);

        if (version >= 35)
        {
            var movableCount = r.ReadUInt32();
            for (var i = 0; i < movableCount; i++)
            {
                while (r.ReadByte() != 0) { } // id string
                r.BaseStream.Seek(12 * sizeof(float), SeekOrigin.Current); // 3x4 transform
            }
        }

        if (version >= 36)
            SkipKV3(r); // game data

        var areaCount = r.ReadUInt32();
        var areas = new List<NavArea>((int)areaCount);
        for (var i = 0; i < areaCount; i++)
            areas.Add(ReadArea(r, version, polygons));

        // Ladders, generation params and bot analysis data follow; not needed.
        return areas;
    }

    private static (Vector3[] Corners, bool Movable)[] ReadPolygons(BinaryReader r, uint version)
    {
        var cornerCount = r.ReadUInt32();
        var corners = new Vector3[cornerCount];
        for (var i = 0; i < cornerCount; i++)
            corners[i] = ReadVector(r);

        var polygonCount = r.ReadUInt32();
        var polygons = new (Vector3[], bool)[polygonCount];
        for (var i = 0; i < polygonCount; i++)
        {
            var n = r.ReadByte();
            var poly = new Vector3[n];
            for (var j = 0; j < n; j++)
                poly[j] = corners[r.ReadUInt32()];
            var movable = version >= 35 && r.ReadUInt32() != NoMovableMesh;
            polygons[i] = (poly, movable);
        }

        if (version >= 32 && (r.ReadUInt32() & 1) != 0)
        {
            var gravityCount = r.ReadUInt32();
            r.BaseStream.Seek(gravityCount * 3L * sizeof(float), SeekOrigin.Current);
        }

        return polygons;
    }

    private static NavArea ReadArea(BinaryReader r, uint version, (Vector3[] Corners, bool Movable)[]? polygons)
    {
        var id = r.ReadUInt32();
        var attributes = (NavAttributes)r.ReadInt64();
        var hull = r.ReadByte();

        Vector3[] corners;
        var movable = false;
        if (version >= 31)
        {
            (corners, movable) = polygons![r.ReadUInt32()];
        }
        else
        {
            corners = new Vector3[r.ReadUInt32()];
            for (var i = 0; i < corners.Length; i++)
                corners[i] = ReadVector(r);
        }

        r.ReadSingle(); // unused

        var neighbors = new List<uint>();
        for (var i = 0; i < corners.Length; i++)
        {
            var connectionCount = r.ReadUInt32();
            for (var j = 0; j < connectionCount; j++)
            {
                neighbors.Add(r.ReadUInt32()); // area id
                r.ReadUInt32();                // edge id
            }
        }

        var hidingSpotCount = r.ReadByte();
        r.BaseStream.Seek(hidingSpotCount * 17L, SeekOrigin.Current); // id, position, flags

        if (r.ReadUInt32() != 0)
            throw new InvalidDataException("nav encounter paths are not supported");

        r.BaseStream.Seek(r.ReadUInt32() * 4L, SeekOrigin.Current); // ladders above
        r.BaseStream.Seek(r.ReadUInt32() * 4L, SeekOrigin.Current); // ladders below

        return new NavArea
        {
            Id = id,
            Attributes = attributes,
            HullIndex = hull,
            OnMovableMesh = movable,
            Corners = corners,
            Neighbors = [.. neighbors],
        };
    }

    /// <summary>Skips an embedded binary KV3 document without decoding it.</summary>
    private static void SkipKV3(BinaryReader r)
    {
        var s = r.BaseStream;
        s.Position = (s.Position + 7) & ~7L;

        var magic = r.ReadUInt32();
        var version = magic & 0xFF;
        if ((magic & 0xFFFFFF00) != 0x4B563300 || version is < 2 or > 5)
            throw new InvalidDataException($"unsupported KV3 block 0x{magic:X8}");

        s.Seek(16, SeekOrigin.Current); // format guid
        var compression = r.ReadUInt32();
        r.ReadUInt16(); // dictionary id
        r.ReadUInt16(); // frame size
        s.Seek(4 * 4, SeekOrigin.Current); // bytes1, bytes4, bytes8, types
        r.ReadUInt16(); // objects
        r.ReadUInt16(); // arrays
        var sizeUncompressedTotal = r.ReadInt32();
        var sizeCompressedTotal = r.ReadInt32();
        var blockCount = r.ReadInt32();
        var binaryBlobBytes = r.ReadInt32();

        if (version >= 4)
            s.Seek(2 * 4, SeekOrigin.Current); // bytes2, block compressed sizes

        long dataSize;
        if (version >= 5)
        {
            var uncompressed1 = r.ReadInt32();
            var compressed1 = r.ReadInt32();
            var uncompressed2 = r.ReadInt32();
            var compressed2 = r.ReadInt32();
            s.Seek(8 * 4, SeekOrigin.Current); // per-buffer counts

            dataSize = compression == 0 ? uncompressed1 + uncompressed2 : compressed1 + compressed2;
        }
        else
        {
            dataSize = compression == 0 ? sizeUncompressedTotal : sizeCompressedTotal;
        }

        if (blockCount > 0)
        {
            if (compression != 0 && version >= 5)
                throw new InvalidDataException("compressed KV3 binary blobs are not supported");
            if (compression == 0)
                dataSize += binaryBlobBytes;
        }

        s.Seek(dataSize, SeekOrigin.Current);
    }

    private static Vector3 ReadVector(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
}
