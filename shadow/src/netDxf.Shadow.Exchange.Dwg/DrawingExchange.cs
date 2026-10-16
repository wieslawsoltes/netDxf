using System.Collections.Immutable;
using System.Text;
using ACadSharp;
using ACadSharp.IO;

namespace netDxf.Shadow.Exchange.Dwg;

public enum DrawingEncoding { Dxf, Dwg }
public sealed record DrawingExchangeLimits(int MaximumInputBytes = 20 * 1024 * 1024,
    int MaximumOutputBytes = 64 * 1024 * 1024, int MaximumEntities = 100_000,
    int MaximumBlocks = 4096, int MaximumDiagnostics = 128, int MaximumHatchSeedPoints = 100_000);
public sealed record DrawingExchangeDiagnostic(string Stage, string Kind, string Message);
public sealed record DrawingExchangeResult(ImmutableArray<byte> Content, DrawingEncoding Encoding,
    string SourceVersion, string OutputVersion, ImmutableArray<DrawingExchangeDiagnostic> Diagnostics,
    int EntityCount, int NamedBlockCount);
public sealed class DrawingExchangeException(string message, ImmutableArray<DrawingExchangeDiagnostic> diagnostics,
    Exception? inner = null) : InvalidOperationException(message, inner)
{
    public ImmutableArray<DrawingExchangeDiagnostic> Diagnostics { get; } = diagnostics;
}

/// <summary>A stream-only, pinned codec adapter. Conversion is staged before source adoption.
/// Unknown/unsupported codec notifications are errors, never silently discarded geometry.
/// The structural check is not a proof of complete proprietary-format or byte fidelity.</summary>
public sealed partial class DrawingExchange
{
    public const string Provider = "ACadSharp 3.8.0";
    // AC1014 is deliberately not advertised: the pinned provider's shared-INSERT
    // round trip failed with a missing block handle. Never publish that unqualified
    // profile or silently substitute another version. Read admission is independent.
    public static ImmutableArray<string> WriteVersions { get; } = ["AC1015", "AC1018", "AC1024", "AC1027", "AC1032"];
    private readonly DrawingExchangeLimits limits;
    public DrawingExchange(DrawingExchangeLimits? limits = null)
    {
        this.limits = limits ?? new();
        if (this.limits.MaximumInputBytes is < 128 or > 256 * 1024 * 1024 ||
            this.limits.MaximumOutputBytes is < 128 or > 256 * 1024 * 1024 ||
            this.limits.MaximumEntities is < 1 or > 1_000_000 || this.limits.MaximumBlocks is < 1 or > 100_000 ||
            this.limits.MaximumDiagnostics is < 1 or > 4096 || this.limits.MaximumHatchSeedPoints is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(limits));
    }
    public static bool IsDwg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 6 || bytes[0] != 'A' || bytes[1] != 'C') return false;
        for (var i = 2; i < 6; i++) if (bytes[i] < '0' || bytes[i] > '9') return false;
        return true;
    }

    public DrawingExchangeResult Convert(ReadOnlyMemory<byte> input, DrawingEncoding source, DrawingEncoding target,
        string outputDwgVersion = "AC1032", CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(source) || !Enum.IsDefined(target) || source == target) throw new ArgumentException("Choose distinct supported source and target encodings.");
        if (input.Length < 6 || input.Length > limits.MaximumInputBytes) throw new ArgumentException("Drawing input byte budget exceeded or truncated input.");
        if ((source == DrawingEncoding.Dwg) != IsDwg(input.Span)) throw new ArgumentException("Drawing signature does not match the selected encoding.");
        if (target == DrawingEncoding.Dwg && !WriteVersions.Contains(outputDwgVersion)) throw new ArgumentException("Unsupported DWG output version; choose a declared write profile.");
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = ImmutableArray.CreateBuilder<DrawingExchangeDiagnostic>(); var stage = "read-source";
        void Notify(object _, NotificationEventArgs notification)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (diagnostics.Count >= limits.MaximumDiagnostics) throw new DrawingExchangeException("Codec diagnostic budget exceeded.", diagnostics.ToImmutable());
            var message = notification.Message ?? "Codec notification";
            if (message.Length > 4096) message = message[..4096];
            diagnostics.Add(new(stage, notification.NotificationType.ToString(), message));
            if (notification.NotificationType != NotificationType.None)
                throw new DrawingExchangeException($"{Provider} refused {stage}: {message}", diagnostics.ToImmutable(), notification.Exception);
        }
        try
        {
            var document = Read(input.ToArray(), source, Notify, cancellationToken);
            var sourceVersion = document.Header.Version.ToString();
            var original = Inventory(document, cancellationToken);
            var originalHatches = CaptureHatches(document, cancellationToken);
            if (target == DrawingEncoding.Dwg) document.Header.Version = Enum.Parse<ACadVersion>(outputDwgVersion);
            else document.Header.Version = ACadVersion.AC1032;
            stage = "write-target";
            var encoded = Write(document, target, Notify, cancellationToken);
            stage = "read-target";
            var reread = Read(encoded, target, Notify, cancellationToken);
            var outputVersion = reread.Header.Version.ToString();
            Equivalent(original, Inventory(reread, cancellationToken));
            RequireHatchFacts(originalHatches, CaptureHatches(reread, cancellationToken), cancellationToken);
            // Use modern DXF for the native validation transport regardless of the
            // chosen DWG profile. This never changes the already encoded payload.
            stage = "validate-netdxf";
            reread.Header.Version = ACadVersion.AC1032;
            var dxf = target == DrawingEncoding.Dxf ? encoded : Write(reread, DrawingEncoding.Dxf, Notify, cancellationToken);
            using (var stream = new MemoryStream(dxf, writable: false))
            {
                var native = DxfDocument.Load(stream) ?? throw new InvalidDataException("netDxf refused the converted DXF.");
                Equivalent(original, Inventory(native, cancellationToken));
                RequireHatchFacts(originalHatches, CaptureHatches(native, cancellationToken), cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(encoded.ToImmutableArray(), target, sourceVersion, outputVersion, diagnostics.ToImmutable(),
                original.Where(p => p.Key.Type != "#DEFINITION").Sum(p => p.Value), original.Keys.Select(k => k.Block).Distinct(StringComparer.OrdinalIgnoreCase).Count(n => !n.StartsWith('*')));
        }
        catch (OperationCanceledException) { throw; }
        catch (DrawingExchangeException) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { throw new DrawingExchangeException($"Drawing conversion failed during {stage}: {error.GetBaseException().Message}", diagnostics.ToImmutable(), error); }
    }

    private CadDocument Read(byte[] bytes, DrawingEncoding encoding, NotificationEventHandler notify, CancellationToken token)
    {
        using var stream = new BudgetStream(bytes, Math.Max(limits.MaximumInputBytes, limits.MaximumOutputBytes), token);
        if (encoding == DrawingEncoding.Dwg)
        {
            using var reader = new DwgReader(stream, notify);
            reader.Configuration = new DwgReaderConfiguration { Failsafe = false, CrcCheck = true, KeepUnknownEntities = true, KeepUnknownNonGraphicalObjects = true };
            return reader.Read();
        }
        using var dxf = new DxfReader(stream, notify);
        dxf.Configuration = new DxfReaderConfiguration { Failsafe = false, KeepUnknownEntities = true, KeepUnknownNonGraphicalObjects = true };
        var decoded = dxf.Read();
        ReconcileDxfHatches(bytes, decoded, token);
        return decoded;
    }
    private byte[] Write(CadDocument document, DrawingEncoding encoding, NotificationEventHandler notify, CancellationToken token)
    {
        using var stream = new BudgetStream(limits.MaximumOutputBytes, token);
        if (encoding == DrawingEncoding.Dwg) { using var writer = new DwgWriter(stream, document); writer.OnNotification += notify; writer.Write(); }
        else { using var writer = new DxfWriter(stream, document, false); writer.OnNotification += notify; writer.Write(); }
        token.ThrowIfCancellationRequested(); return stream.ToArray();
    }
    // Opaque solids, images/underlays and proxies require separate preservation contracts.
    private static readonly HashSet<string> EntityTypes = new(StringComparer.OrdinalIgnoreCase)
    { "LINE", "ARC", "CIRCLE", "ELLIPSE", "LWPOLYLINE", "POLYLINE", "POINT", "SPLINE", "TEXT", "MTEXT",
      "INSERT", "ATTDEF", "ATTRIB", "HATCH", "DIMENSION", "SOLID", "TRACE", "3DFACE", "VIEWPORT", "LEADER", "MLINE", "RAY", "XLINE", "TOLERANCE" };
    private Dictionary<(string Block, string Type), int> Inventory(CadDocument document, CancellationToken token)
    {
        var result = new Dictionary<(string Block, string Type), int>(); int blocks = 0, count = 0;
        foreach (var block in document.BlockRecords)
        {
            token.ThrowIfCancellationRequested(); if (++blocks > limits.MaximumBlocks) throw new ArgumentException("Block budget exceeded.");
            foreach (var entity in block.Entities)
            {
                token.ThrowIfCancellationRequested(); if (++count > limits.MaximumEntities) throw new ArgumentException("Entity budget exceeded.");
                if (!EntityTypes.Contains(entity.ObjectName) || entity.GetType().Name.StartsWith("Unknown", StringComparison.Ordinal))
                    throw new NotSupportedException($"DWG exchange has no preservation contract for {entity.ObjectName}.");
                Increment(result, block.Name, entity.ObjectName);
            }
            if (!block.Name.StartsWith('*')) Increment(result, block.Name, "#DEFINITION");
        }
        return result;
    }
    private Dictionary<(string Block, string Type), int> Inventory(DxfDocument document, CancellationToken token)
    {
        var result = new Dictionary<(string Block, string Type), int>(); int blocks = 0, count = 0;
        foreach (var block in document.Blocks)
        {
            token.ThrowIfCancellationRequested(); if (++blocks > limits.MaximumBlocks) throw new ArgumentException("Block budget exceeded.");
            // ATTDEF derives from DxfObject rather than EntityObject. netDxf also
            // stores the paper-space VIEWPORT with id 1 on Layout, outside Entities.
            IEnumerable<DxfObject> objects = block.Entities.Cast<DxfObject>().Concat(block.AttributeDefinitions.Values);
            if (block.Record.Layout?.Viewport is { } paperViewport) objects = objects.Append(paperViewport);
            var seen = new HashSet<DxfObject>(ReferenceEqualityComparer.Instance);
            foreach (var entity in objects)
            {
                if (!seen.Add(entity)) continue;
                token.ThrowIfCancellationRequested(); if (++count > limits.MaximumEntities) throw new ArgumentException("Entity budget exceeded.");
                Increment(result, block.Name, entity.CodeName);
            }
            if (!block.Name.StartsWith('*')) Increment(result, block.Name, "#DEFINITION");
        }
        return result;
    }
    private static void Increment(Dictionary<(string Block, string Type), int> inventory, string block, string type)
    {
        var key = (block.ToUpperInvariant(), type.ToUpperInvariant()); inventory.TryGetValue(key, out var value); inventory[key] = value + 1;
    }
    private static void Equivalent(Dictionary<(string Block, string Type), int> before, Dictionary<(string Block, string Type), int> after)
    {
        var changed = before.Keys.Concat(after.Keys).Distinct().Where(key => before.GetValueOrDefault(key) != after.GetValueOrDefault(key))
            .Take(8).Select(key => $"{key.Block}/{key.Type}: {before.GetValueOrDefault(key)} -> {after.GetValueOrDefault(key)}").ToArray();
        if (changed.Length == 0) return;
        var detail = string.Join("; ", changed);
        if (detail.Length > 4096) detail = detail[..4096];
        throw new InvalidDataException("Converted drawing changed the per-block entity inventory or named definitions; no partial document was adopted. " + detail);
    }

    /// <summary>Bounds stream I/O and polls cancellation. Decompression is not a hard process-memory sandbox.</summary>
    private sealed class BudgetStream : MemoryStream
    {
        private readonly int maximum; private readonly CancellationToken token;
        internal BudgetStream(int maximum, CancellationToken token) { this.maximum = maximum; this.token = token; }
        internal BudgetStream(byte[] input, int maximum, CancellationToken token) : base(input, writable: false) { this.maximum = maximum; this.token = token; }
        private void CheckWrite(long end) { token.ThrowIfCancellationRequested(); if (end < 0 || end > maximum) throw new IOException("Drawing output byte budget exceeded."); }
        public override int Read(byte[] buffer, int offset, int count) { token.ThrowIfCancellationRequested(); return base.Read(buffer, offset, count); }
        public override int Read(Span<byte> buffer) { token.ThrowIfCancellationRequested(); return base.Read(buffer); }
        public override int ReadByte() { token.ThrowIfCancellationRequested(); return base.ReadByte(); }
        public override long Seek(long offset, SeekOrigin origin) { token.ThrowIfCancellationRequested(); return base.Seek(offset, origin); }
        public override void Write(byte[] buffer, int offset, int count) { CheckWrite(checked(Position + count)); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { CheckWrite(checked(Position + buffer.Length)); base.Write(buffer); }
        public override void WriteByte(byte value) { CheckWrite(checked(Position + 1)); base.WriteByte(value); }
        public override void SetLength(long value) { CheckWrite(value); base.SetLength(value); }
    }
}
