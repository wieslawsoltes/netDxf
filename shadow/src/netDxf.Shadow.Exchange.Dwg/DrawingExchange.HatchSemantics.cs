using System.Collections.Immutable;
using System.Globalization;
using ACadSharp;
using CodecHatch = ACadSharp.Entities.Hatch;
using NativeHatch = netDxf.Entities.Hatch;

namespace netDxf.Shadow.Exchange.Dwg;

public sealed partial class DrawingExchange
{
    private readonly record struct HatchIdentity(string Block, ulong Handle);
    private sealed record HatchFacts(int Style, bool Solid, bool Associative, int PatternType,
        bool Double, string Pattern, double Angle, double Scale, double Elevation,
        double NormalX, double NormalY, double NormalZ, int Paths, ImmutableArray<Vector2> Seeds);

    // ACadSharp 3.8.0's DXF readHatch ignores group 75 and treats the initial
    // elevation XY as a seed. Read the original bytes with netDxf and transfer
    // ONLY those omitted/misparsed fields by verified owner + handle identity.
    // No inference from entity order, filenames or default values is permitted.
    // Upstream: v3.8.0 DxfSectionReaderBase.readHatch (codes 10/20, 75 and 98).
    private void ReconcileDxfHatches(byte[] bytes, CadDocument decoded, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(bytes, writable: false);
        var native = DxfDocument.Load(stream) ?? throw new InvalidDataException("netDxf refused the source DXF transport.");
        Equivalent(Inventory(native, token), Inventory(decoded, token));
        var expected = CaptureHatches(native, token);
        var targets = new Dictionary<HatchIdentity, CodecHatch>();
        foreach (var block in decoded.BlockRecords)
        foreach (var hatch in block.Entities.OfType<CodecHatch>())
        {
            token.ThrowIfCancellationRequested();
            if (!targets.TryAdd(Key(block.Name, hatch.Handle), hatch))
                throw new InvalidDataException("Duplicate codec hatch identity.");
        }
        if (expected.Count != targets.Count)
            throw new InvalidDataException("Source DXF hatch identity inventory changed during decoding.");
        foreach (var (key, facts) in expected)
        {
            token.ThrowIfCancellationRequested();
            if (!targets.TryGetValue(key, out var hatch))
                throw new InvalidDataException("Cannot bind decoded HATCH to its source owner and handle.");
            hatch.Style = (ACadSharp.Entities.HatchStyleType)facts.Style;
            hatch.SeedPoints.Clear();
            foreach (var seed in facts.Seeds)
            {
                token.ThrowIfCancellationRequested();
                hatch.SeedPoints.Add(new(seed.X, seed.Y));
            }
        }
        // Do not repair any additional divergence. Refuse it before output.
        RequireHatchFacts(expected, CaptureHatches(decoded, token), token);
    }

    private Dictionary<HatchIdentity, HatchFacts> CaptureHatches(CadDocument document, CancellationToken token)
    {
        var result = new Dictionary<HatchIdentity, HatchFacts>(); int seeds = 0;
        foreach (var block in document.BlockRecords)
        foreach (var hatch in block.Entities.OfType<CodecHatch>())
        {
            token.ThrowIfCancellationRequested();
            var facts = new HatchFacts((int)hatch.Style, hatch.IsSolid, hatch.IsAssociative,
                (int)hatch.PatternType, hatch.IsDouble, hatch.Pattern.Name,
                hatch.PatternAngle, hatch.PatternScale, hatch.Elevation,
                hatch.Normal.X, hatch.Normal.Y, hatch.Normal.Z, hatch.Paths.Count,
                Seeds(hatch.SeedPoints.Select(p => new Vector2(p.X, p.Y)), ref seeds, token));
            Validate(facts);
            if (!result.TryAdd(Key(block.Name, hatch.Handle), facts))
                throw new InvalidDataException("Duplicate codec hatch identity.");
        }
        return result;
    }

    private Dictionary<HatchIdentity, HatchFacts> CaptureHatches(DxfDocument document, CancellationToken token)
    {
        var result = new Dictionary<HatchIdentity, HatchFacts>(); int seeds = 0;
        foreach (var block in document.Blocks)
        foreach (var hatch in block.Entities.OfType<NativeHatch>())
        {
            token.ThrowIfCancellationRequested();
            if (!ulong.TryParse(hatch.Handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var handle))
                throw new InvalidDataException("HATCH exchange requires a valid source handle.");
            var p = hatch.Pattern;
            var facts = new HatchFacts((int)p.Style, p.Fill == netDxf.Entities.HatchFillType.SolidFill,
                hatch.Associative, (int)p.Type, p.IsDouble, p.Name, p.Angle * Math.PI / 180, p.Scale,
                hatch.Elevation, hatch.Normal.X, hatch.Normal.Y, hatch.Normal.Z, hatch.BoundaryPaths.Count,
                Seeds(hatch.SeedPoints, ref seeds, token));
            Validate(facts);
            if (!result.TryAdd(Key(block.Name, handle), facts))
                throw new InvalidDataException("Duplicate native hatch identity.");
        }
        return result;
    }

    private ImmutableArray<Vector2> Seeds(IEnumerable<Vector2> source, ref int count, CancellationToken token)
    {
        var result = ImmutableArray.CreateBuilder<Vector2>();
        foreach (var point in source)
        {
            token.ThrowIfCancellationRequested();
            if (++count > limits.MaximumHatchSeedPoints) throw new InvalidDataException("Hatch seed-point budget exceeded.");
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                throw new InvalidDataException("Hatch seed points must be finite.");
            result.Add(point);
        }
        return result.ToImmutable();
    }

    private static HatchIdentity Key(string block, ulong handle)
    {
        if (handle == 0 || string.IsNullOrWhiteSpace(block))
            throw new InvalidDataException("HATCH exchange requires nonzero handles and explicit owning blocks.");
        return new(block.ToUpperInvariant(), handle);
    }

    private static void Validate(HatchFacts facts)
    {
        if (facts.Style is < 0 or > 2 || facts.PatternType is < 0 or > 2 ||
            string.IsNullOrEmpty(facts.Pattern) || facts.Paths < 0 ||
            !double.IsFinite(facts.Elevation) || !double.IsFinite(facts.NormalX) ||
            !double.IsFinite(facts.NormalY) || !double.IsFinite(facts.NormalZ) ||
            !facts.Solid && (!double.IsFinite(facts.Angle) || !double.IsFinite(facts.Scale) || facts.Scale <= 0))
            throw new InvalidDataException("Invalid hatch semantics in drawing exchange.");
    }

    private static void RequireHatchFacts(Dictionary<HatchIdentity, HatchFacts> expected,
        Dictionary<HatchIdentity, HatchFacts> actual, CancellationToken token)
    {
        if (expected.Count != actual.Count) throw new InvalidDataException("Drawing exchange changed the hatch identity inventory.");
        foreach (var (key, a) in expected)
        {
            token.ThrowIfCancellationRequested();
            if (!actual.TryGetValue(key, out var b)) throw new InvalidDataException("Drawing exchange changed a hatch owner or source handle.");
            if (a.Style != b.Style || a.Solid != b.Solid || a.Associative != b.Associative || a.Paths != b.Paths ||
                !Near(a.Elevation, b.Elevation) || !Near(a.NormalX,b.NormalX) || !Near(a.NormalY,b.NormalY) || !Near(a.NormalZ,b.NormalZ) ||
                !a.Solid && (a.PatternType != b.PatternType || a.Double != b.Double ||
                    !string.Equals(a.Pattern,b.Pattern,StringComparison.OrdinalIgnoreCase) || !Near(a.Scale,b.Scale) ||
                    !(Math.Abs(Math.IEEERemainder(a.Angle-b.Angle,Math.PI*2)) <= 1e-12)) || a.Seeds.Length != b.Seeds.Length)
                throw new InvalidDataException($"Drawing exchange changed HATCH {key.Handle:X} flags, plane, pattern or seed-point count.");
            for (int i = 0; i < a.Seeds.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                if (!Near(a.Seeds[i].X,b.Seeds[i].X) || !Near(a.Seeds[i].Y,b.Seeds[i].Y))
                    throw new InvalidDataException($"Drawing exchange changed HATCH {key.Handle:X} seed-point coordinates or order.");
            }
        }
        static bool Near(double a, double b) => double.IsFinite(a) && double.IsFinite(b) &&
            Math.Abs(a-b) <= 16 * Math.Max(Spacing(a),Spacing(b));
        static double Spacing(double value) { value=Math.Abs(value); return value-Math.BitDecrement(value); }
    }
}
