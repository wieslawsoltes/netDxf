using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using netDxf.Entities;

namespace netDxf.Shadow.NetDxf;

/// <summary>Reads a stored BMP preview without activating or changing an OLE object.</summary>
internal static class OleRasterPreview
{
    internal const string Property = "$oleRasterPreview";
    internal const string ReferencePrefix = "data:image/bmp;base64,";
    private const int MaximumBytes = 32 * 1024 * 1024;
    private const long MaximumPixels = 16 * 1024 * 1024;
    private static readonly ConditionalWeakTable<Ole2Frame, Cached> Cache = new();
    private sealed record Cached(ShadowValue Value);

    internal static ShadowValue Read(Ole2Frame frame) => Cache.GetValue(frame, Create).Value;

    private static Cached Create(Ole2Frame frame)
    {
        if (frame.BinaryDataLength < 512 || frame.BinaryDataLength > MaximumBytes) return new(ShadowValue.Null);
        try
        {
            var bytes = frame.GetBinaryData();
            var (offset, corners) = ReadEnvelope(bytes, frame);
            var bitmap = new CompoundFile(bytes, offset).ReadContents();
            ValidateBitmap(bitmap);
            return new(new ShadowValue.Structure("netDxf.Shadow.OleRasterPreview",
                ImmutableDictionary<string, ShadowValue>.Empty
                    .Add("Reference", ShadowValue.From(ReferencePrefix + Convert.ToBase64String(bitmap)))
                    .Add("Corners", new ShadowValue.Sequence(corners.Select(p => ShadowValue.From(p)).ToImmutableArray()))));
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException)
        {
            // Unrecognized native data remains inert and is reported as unsupported
            // by the semantic projector. No partial preview enters the snapshot.
            return new(ShadowValue.Null);
        }
    }

    private static (int Offset, Vec3[] Corners) ReadEnvelope(byte[] data, Ole2Frame frame)
    {
        Vec3[] corners;
        int offset;
        if (Signature(data))
        {
            var top = frame.UpperLeftCorner; var bottom = frame.LowerRightCorner;
            corners = [new(top.X, bottom.Y, bottom.Z), new(bottom.X, bottom.Y, bottom.Z),
                new(bottom.X, top.Y, top.Z), new(top.X, top.Y, top.Z)];
            offset = 0;
        }
        else
        {
            // Published OLE2 envelope: signature, four WCS AcGePoint3d corners,
            // extents and tilemode, followed by COleClientItem serialization.
            // https://blog.autodesk.io/dxf-files-binary-chunk-interpretation/
            // The DXF OLE2FRAME example also documents the 0x5500 signature family.
            // https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-77747CE6-82C6-4452-97ED-4CEEB38BE960.htm
            Require(data.Length >= 106 && (U16(data, 0) & 0xff7e) == 0x5500);
            var stored = new Vec3[4];
            for (int i = 0; i < stored.Length; i++)
            {
                int start = 2 + i * 24;
                stored[i] = new(Double(data, start), Double(data, start + 8), Double(data, start + 16));
            }
            corners = [stored[3], stored[2], stored[1], stored[0]];
            offset = -1;
            // Locate the compound header only within the bounded serialization
            // header. Embedded image signatures are never used to extract a preview.
            for (int i = 106; i <= Math.Min(512, data.Length - 512); i++)
                if (Signature(data.AsSpan(i)))
                {
                    Require(offset < 0); offset = i;
                }
            Require(offset >= 0);
        }
        Require(corners.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z)));
        var x = corners[1] - corners[0]; var y = corners[3] - corners[0];
        Require(double.IsFinite(x.Length) && double.IsFinite(y.Length) && x.Length > 0 && y.Length > 0);
        var opposite = corners[2] - corners[3];
        Require((opposite - x).Length <= 1e-8 * Math.Max(1, x.Length));
        Require(Vec3.Cross(x / x.Length, y / y.Length).Length > 1e-10);
        return (offset, corners);
    }

    private static void ValidateBitmap(byte[] data)
    {
        // Only uncompressed BMP/DIB variants are handed to an image decoder.
        // https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-bitmapinfoheader
        Require(data.Length >= 54 && data[0] == 'B' && data[1] == 'M' && U32(data, 2) == data.Length);
        uint header = U32(data, 14), pixels = U32(data, 10);
        Require(header is 40 or 108 or 124 && 14L + header <= data.Length);
        int width = I32(data, 18), height = I32(data, 22);
        Require(width > 0 && width <= 16384 && height != 0 && height >= -16384 && height <= 16384
            && (long)width * Math.Abs(height) <= MaximumPixels && U16(data, 26) == 1);
        int bits = U16(data, 28); uint compression = U32(data, 30);
        Require(bits is 1 or 4 or 8 or 16 or 24 or 32 && (compression == 0 || compression == 3 && bits is 16 or 32));
        uint colors = U32(data, 46);
        if (bits <= 8) { Require(colors <= (1u << bits)); if (colors == 0) colors = 1u << bits; }
        long minimum = 14L + header + (compression == 3 && header == 40 ? 12 : 0) + colors * 4L;
        long byteCount = ((width * (long)bits + 31) / 32 * 4) * Math.Abs(height);
        Require(pixels >= minimum && pixels <= data.Length && byteCount <= data.Length - pixels);
        Require(U32(data, 34) == 0 || U32(data, 34) == byteCount);
        if (compression == 3)
        {
            uint r = U32(data, 54), g = U32(data, 58), b = U32(data, 62);
            Require(Contiguous(r) && Contiguous(g) && Contiguous(b) && (r & g) == 0 && (r & b) == 0 && (g & b) == 0);
            Require(bits == 32 || (r | g | b) < (1u << bits));
        }
    }

    private static bool Contiguous(uint mask)
    {
        if (mask == 0) return false;
        while ((mask & 1) == 0) mask >>= 1;
        return (mask & (mask + 1)) == 0;
    }
    private static bool Signature(ReadOnlySpan<byte> data) => data.Length >= 8
        && data[..8].SequenceEqual(new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 });
    private static void Require(bool condition) { if (!condition) throw new InvalidDataException("Unsupported or malformed stored OLE bitmap preview."); }
    private static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    private static int I32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
    private static ulong U64(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
    private static double Double(ReadOnlySpan<byte> data, int offset) => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, 8)));

    // MS-CFB allocation rules. All chains have bounded, unique sectors and exact
    // declared lengths; directory links are followed rather than scanning payloads.
    // https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-cfb/05060311-bfce-4b12-874d-71fd4ce63aea
    private sealed class CompoundFile
    {
        private const uint Free = 0xffffffff, End = 0xfffffffe, FatSector = 0xfffffffd, DifatSector = 0xfffffffc;
        private readonly byte[] data;
        private readonly int offset, sectorSize, sectorCount;
        private readonly uint[] fat;
        private readonly HashSet<uint> allocated = new();
        private readonly List<uint> fatSectors = new(), difatSectors = new();

        internal CompoundFile(byte[] data, int offset)
        {
            this.data = data; this.offset = offset;
            var header = data.AsSpan(offset);
            Require(header.Length >= 512 && Signature(header) && U16(header, 28) == 0xfffe && U16(header, 32) == 6);
            ushort major = U16(header, 26), shift = U16(header, 30);
            Require(major == 3 && shift == 9 || major == 4 && shift == 12);
            Require(major != 3 || U32(header, 40) == 0);
            Require(U32(header, 56) == 4096);
            sectorSize = 1 << shift; Require(header.Length % sectorSize == 0);
            sectorCount = header.Length / sectorSize - 1; Require(sectorCount >= 2);
            uint fatCount = U32(header, 44), difatCount = U32(header, 72), next = U32(header, 68);
            Require(fatCount > 0 && fatCount <= sectorCount && difatCount <= sectorCount);
            for (int i = 0; i < 109; i++) AddFat(U32(header, 76 + i * 4));
            for (int i = 0; i < difatCount; i++)
            {
                Claim(next); difatSectors.Add(next); var sector = Sector(next);
                for (int j = 0; j < sectorSize / 4 - 1; j++) AddFat(U32(sector, j * 4));
                next = U32(sector, sectorSize - 4);
            }
            Require((difatCount == 0 && next is End or Free || next == End) && fatSectors.Count == fatCount);
            fat = new uint[checked(fatSectors.Count * (sectorSize / 4))]; Require(fat.Length >= sectorCount);
            for (int i = 0; i < fatSectors.Count; i++)
            {
                var sector = Sector(fatSectors[i]);
                for (int j = 0; j < sectorSize / 4; j++) fat[i * (sectorSize / 4) + j] = U32(sector, j * 4);
            }
            foreach (uint sector in fatSectors) Require(fat[sector] == FatSector);
            foreach (uint sector in difatSectors) Require(fat[sector] == DifatSector);
        }

        private void AddFat(uint sector) { if (sector == Free) return; Claim(sector); fatSectors.Add(sector); }
        private void Claim(uint sector) => Require(sector < sectorCount && allocated.Add(sector));
        private ReadOnlySpan<byte> Sector(uint sector)
        {
            Require(sector < sectorCount);
            return data.AsSpan(checked(offset + ((int)sector + 1) * sectorSize), sectorSize);
        }

        private List<uint> Chain(uint start, long? size, int unit, uint[] table, HashSet<uint> used, int limit)
        {
            Require(size is null || size >= 0 && size <= MaximumBytes);
            var result = new List<uint>(); uint current = start;
            long expected = size is { } length ? (length + unit - 1) / unit : limit;
            while (current != End)
            {
                Require(current < limit && current < table.Length && used.Add(current) && result.Count < expected);
                result.Add(current); current = table[current];
            }
            Require(size is null || result.Count == expected);
            return result;
        }

        private byte[] ReadNormal(uint start, long? size, int maximum = MaximumBytes)
        {
            var chain = Chain(start, size, sectorSize, fat, allocated, sectorCount);
            long length = size ?? (long)chain.Count * sectorSize; Require(length <= maximum);
            var result = new byte[(int)length]; int written = 0;
            foreach (uint sector in chain)
            {
                int count = Math.Min(sectorSize, result.Length - written);
                Sector(sector)[..count].CopyTo(result.AsSpan(written)); written += count;
            }
            return result;
        }

        internal byte[] ReadContents()
        {
            var header = data.AsSpan(offset);
            var directory = ReadNormal(U32(header, 48), U16(header, 26) == 4 ? U32(header, 40) * (long)sectorSize : null, 1024 * 1024);
            Require(directory.Length >= 128 && directory[66] == 5);
            var root = directory.AsSpan(0, 128);
            Require(U32(root, 68) == Free && U32(root, 72) == Free);
            ulong miniLength = U64(root, 120); Require(miniLength <= MaximumBytes && miniLength % 64 == 0);
            var miniStream = ReadNormal(U32(root, 116), (long)miniLength);
            uint miniCount = U32(header, 64); Require(miniCount <= sectorCount);
            var miniFatBytes = ReadNormal(U32(header, 60), miniCount * (long)sectorSize);
            var miniFat = new uint[miniFatBytes.Length / 4];
            for (int i = 0; i < miniFat.Length; i++) miniFat[i] = U32(miniFatBytes, i * 4);
            var miniAllocated = new HashSet<uint>();
            var visited = new HashSet<uint> { 0 };
            var pending = new Stack<(uint Id, bool RootChild)>(); pending.Push((U32(root, 76), true));
            byte[]? contents = null;
            while (pending.TryPop(out var next))
            {
                if (next.Id == Free) continue;
                Require(next.Id < directory.Length / 128 && visited.Add(next.Id));
                var entry = directory.AsSpan((int)next.Id * 128, 128);
                ushort nameLength = U16(entry, 64);
                Require(nameLength >= 2 && nameLength <= 64 && nameLength % 2 == 0 && U16(entry, nameLength - 2) == 0);
                string name = new UnicodeEncoding(false, false, true).GetString(entry[..(nameLength - 2)]);
                Require(!name.Contains('\0'));
                byte kind = entry[66]; Require(kind is 1 or 2);
                pending.Push((U32(entry, 68), next.RootChild)); pending.Push((U32(entry, 72), next.RootChild));
                if (kind == 1) { pending.Push((U32(entry, 76), false)); continue; }
                Require(U32(entry, 76) == Free);
                ulong length = U64(entry, 120); Require(length <= MaximumBytes);
                uint start = U32(entry, 116);
                bool selected = next.RootChild && string.Equals(name, "CONTENTS", StringComparison.OrdinalIgnoreCase);
                Require(!selected || contents is null);
                if (length < 4096)
                {
                    var chain = Chain(start, (long)length, 64, miniFat, miniAllocated, miniStream.Length / 64);
                    if (!selected) continue;
                    contents = new byte[(int)length]; int written = 0;
                    foreach (uint sector in chain)
                    {
                        int count = Math.Min(64, contents.Length - written);
                        miniStream.AsSpan((int)sector * 64, count).CopyTo(contents.AsSpan(written)); written += count;
                    }
                }
                else if (selected) contents = ReadNormal(start, (long)length);
                else Chain(start, (long)length, sectorSize, fat, allocated, sectorCount);
            }
            // Reachable entries must account for every allocated directory object.
            for (int i = 1; i < directory.Length / 128; i++)
                Require(directory[i * 128 + 66] == 0 || visited.Contains((uint)i));
            Require(contents is not null);
            return contents!;
        }
    }
}
