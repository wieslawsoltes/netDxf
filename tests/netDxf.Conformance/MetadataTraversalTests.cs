using System.Globalization;
using System.Reflection;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly MethodInfo MetadataLookupMethod = typeof(DxfDocument).GetMethod(
        "StoredTableHandleTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo MetadataObjectsMethod = typeof(DxfDocument).GetMethod(
        "RetainedMetadataObjects", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo MetadataBlockEndProperty = typeof(Block).GetProperty(
        "End", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static void RegisterMetadataTraversalTests()
    {
        foreach (var version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                var v = version; var b = binary;
                Run($"metadata-traversal/identity-and-reactors/{v}/{b}", () => MetadataTraversalRoundTrip(v, b));
            }
        Run("metadata-traversal/live-member-removal", MetadataTraversalLiveChanges);
        Run("metadata-traversal/lookup-allocation", MetadataTraversalLookupAllocation);
    }

    private static DxfObject? MetadataLookup(DxfDocument document, string? handle)
        => (DxfObject?)MetadataLookupMethod.Invoke(document, new object?[] { handle });

    private static List<DxfObject> MetadataObjects(DxfDocument document)
        => (List<DxfObject>)MetadataObjectsMethod.Invoke(document, null)!;

    private static DxfObject MetadataBlockEnd(Block block)
        => (DxfObject)MetadataBlockEndProperty.GetValue(block)!;

    private static DxfDocument MetadataTraversalDocument(DxfVersion version, int fillers, bool reactors)
    {
        var document = new DxfDocument(version);
        var block = new Block("SYNTHETIC_METADATA_BLOCK");
        block.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX));
        block.AttributeDefinitions.Add(new AttributeDefinition("FIRST") { Value = "One", Height = 1 });
        block.AttributeDefinitions.Add(new AttributeDefinition("SECOND") { Value = "Two", Height = 1 });
        var insert = new Insert(block);
        document.Entities.Add(insert);
        var layout = document.Layouts.Add(new Layout("SYNTHETIC_METADATA_LAYOUT"));
        var registry = document.ApplicationRegistries.Add(new ApplicationRegistry("SYNTHETIC_METADATA"));
        DxfObject[] carriers = [MetadataBlockEnd(block), insert.Attributes[0], insert.Attributes[1], layout.Viewport];
        foreach (var carrier in carriers)
            carrier.XData.Add(new XData(registry) { XDataRecord = { new XDataRecord(XDataCode.String, "owned metadata") } });
        for (var index = 0; index < fillers; index++)
        {
            var line = new Line(new Vector3(index, 1, 0), new Vector3(index + 0.5, 2, 0));
            document.Entities.Add(line);
            if (reactors && index < 16)
            {
                line.PersistentReactors.Add(insert.Attributes[index % 2]);
                line.PersistentReactors.Add(insert.Attributes[(index + 1) % 2]);
            }
        }
        return document;
    }

    private static void MetadataTraversalRoundTrip(DxfVersion version, bool binary)
    {
        var document = MetadataTraversalDocument(version, 80, true);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var objects = MetadataObjects(document);
            Equal(objects.Count, objects.Distinct(ReferenceEqualityComparer.Instance).Count(), "Metadata identity duplicated");
            Equal(4, document.ApplicationRegistries.GetReferences("SYNTHETIC_METADATA").Sum(reference => reference.Uses), "Owned metadata carrier lost");
            foreach (var item in objects)
            {
                if (string.IsNullOrEmpty(item.Handle)) continue;
                Check(ReferenceEquals(item, MetadataLookup(document, item.Handle)), "Exact retained metadata identity changed");
                ulong identity = ulong.Parse(item.Handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                string alias = identity.ToString("x16", CultureInfo.InvariantCulture);
                Check(ReferenceEquals(item, MetadataLookup(document, alias)), "Numeric metadata handle alias changed");
            }
            Check(ReferenceEquals(document.GetObjectByHandle("0"), MetadataLookup(document, "0")),
                "Registered zero-handle lookup changed");
            Check(MetadataLookup(document, null) == null
                && MetadataLookup(document, "XYZ") == null && MetadataLookup(document, "FFFFFFFFFFFFFFFF") == null,
                "Missing metadata lookup fabricated an object");
            var expected = document.Entities.Lines.Where(line => line.PersistentReactors.Count != 0)
                .ToDictionary(line => line.Handle, line => line.PersistentReactors.Select(item => item.Handle).ToArray());
            Equal(0, document.Objects.Validate().Count, "Synthetic metadata graph is invalid");
            using var stream = new MemoryStream();
            Check(document.Save(stream, binary ^ (cycle % 2 != 0)), "Synthetic metadata document save failed");
            stream.Position = 0;
            document = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Synthetic metadata import failed");
            foreach (var pair in expected)
            {
                var line = (Line)document.GetObjectByHandle(pair.Key);
                Check(pair.Value.SequenceEqual(line.PersistentReactors.Select(item => item.Handle)), "Reactor order or identity changed");
                foreach (var reactor in line.PersistentReactors)
                    Check(ReferenceEquals(reactor, MetadataLookup(document, reactor.Handle)), "Reactor target was copied or detached");
            }
        }
    }

    private static void MetadataTraversalLiveChanges()
    {
        var document = MetadataTraversalDocument(DxfVersion.AutoCad2018, 3, false);
        var insert = document.Entities.Inserts.Single();
        var previous = insert.Attributes[0];
        string previousHandle = previous.Handle;
        _ = MetadataObjects(document);
        insert.Block.AttributeDefinitions.Remove("FIRST");
        insert.Sync();
        Check(MetadataLookup(document, previousHandle) == null, "Lookup retained a removed attribute");
        Equal(3, document.ApplicationRegistries.GetReferences("SYNTHETIC_METADATA").Sum(reference => reference.Uses), "Removed attribute retained metadata use");
        var layout = document.Layouts["SYNTHETIC_METADATA_LAYOUT"];
        var viewport = layout.Viewport;
        string viewportHandle = viewport.Handle;
        Check(document.Layouts.Remove(layout), "Synthetic layout removal failed");
        Check(!MetadataObjects(document).Contains(viewport) && MetadataLookup(document, viewportHandle) == null, "Lookup retained a removed layout viewport");
        Equal(2, document.ApplicationRegistries.GetReferences("SYNTHETIC_METADATA").Sum(reference => reference.Uses), "Removed layout retained metadata use");
    }

    private static void MetadataTraversalLookupAllocation()
    {
        var document = MetadataTraversalDocument(DxfVersion.AutoCad2018, 2000, false);
        var target = MetadataBlockEnd(document.Blocks["SYNTHETIC_METADATA_BLOCK"]);
        var lookup = (Func<DxfDocument, string, DxfObject?>)MetadataLookupMethod.CreateDelegate(typeof(Func<DxfDocument, string, DxfObject?>));
        Check(document.GetObjectByHandle(target.Handle) == null, "Synthetic target must exercise retained-member lookup");
        for (var i = 0; i < 8; i++) Check(ReferenceEquals(target, lookup(document, target.Handle)), "Warm metadata lookup failed");
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) Check(ReferenceEquals(target, lookup(document, target.Handle)), "Repeated metadata lookup changed identity");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        // A graph-sized temporary list/hash set for every lookup would allocate
        // tens of megabytes here. Leave ample room for runtime enumerators.
        Check(allocated < 1_000_000, "Retained-member lookup allocates the complete document graph");
    }
}
