using System.Collections.Immutable;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;

namespace netDxf.Shadow.Symbols;

public sealed record DrawingBlockEntry(string Name, ObjectId Block, string Description, string Units, Vec3 BasePoint,
    int Entities, int Attributes, bool ModelSpace);

/// <summary>Immutable DXF-backed block library. Contains no retained mutable source objects.</summary>
public sealed record DrawingBlockLibrary(ShadowSnapshot Snapshot, ImmutableArray<DrawingBlockEntry> Entries)
{
    public const int MaximumBytes = 8 * 1024 * 1024;

    public static DrawingBlockLibrary Read(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumBytes) throw new ArgumentException("Block-library drawings require 1 byte to 8 MiB.");
        token.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        var document = DxfDocument.Load(stream) ?? throw new ArgumentException("The block library is not a readable DXF drawing.");
        token.ThrowIfCancellationRequested();
        var snapshot = new ShadowSession(document, new(MaximumObjects: 100_000, MaximumCollectionItems: 1_000_000,
            MaximumValueDepth: 64, FailOnUnreadableProperty: true)).Current;
        var model = snapshot.Layout("Model")["AssociatedBlock"].Id;
        var entries = snapshot.Document["Blocks"].Items
            .Where(block => block.Id == model || !block["Name"].Text.StartsWith('*') && !block["IsXRef"].Boolean() && !block["Record"]["Layout"].Exists)
            .Select(block => new DrawingBlockEntry(block["Name"].Text, block.Id!.Value, block["Description"].Text,
                block.Id == model ? snapshot.Document["DrawingVariables"]["InsUnits"].Text : block["Record"]["Units"].Text,
                block.Id == model ? snapshot.Document["DrawingVariables"]["InsBase"].Vector() : block["Origin"].Vector(),
                block["Entities"].Items.Count(), block["AttributeDefinitions"].Items.Count(), block.Id == model))
            .OrderByDescending(entry => entry.ModelSpace).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToImmutableArray();
        token.ThrowIfCancellationRequested(); return new(snapshot, entries);
    }

    public BlockLibraryPlan Plan(ShadowSnapshot destination, string sourceName, string name,
        BlockLibraryConflict blockConflict = BlockLibraryConflict.Reject,
        BlockLibraryResourceConflict resourceConflict = BlockLibraryResourceConflict.Reuse, CancellationToken token = default)
    {
        var entry = Entries.FirstOrDefault(entry => entry.Name.Equals(sourceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Choose a model-space drawing or named definition from the library.");
        return DraftingBlockLibrary.Import(destination, Snapshot, new(entry.Block, name, blockConflict, resourceConflict), cancellationToken: token);
    }

    /// <summary>Exports a named definition and its dependencies as an independent DXF library.
    /// A model-space reference makes the exported definition visible in ordinary DXF viewers.</summary>
    public static byte[] Export(ShadowSnapshot source, ObjectId definition, bool binary = false, CancellationToken token = default)
    {
        var block = DraftingBlocks.Definition(source, definition);
        var document = new DxfDocument(DxfVersion.AutoCad2018); var destination = new ShadowSession(document);
        var plan = DraftingBlockLibrary.Import(destination.Current, source,
            new(definition, block["Name"].Text, ResourceConflict: BlockLibraryResourceConflict.Rename, PreserveLayerZero: true), cancellationToken: token);
        plan.Edit.Execute(new EditorSession(destination));
        var native = document.Blocks[block["Name"].Text];
        document.DrawingVariables.InsUnits = native.Record.Units;
        document.DrawingVariables.InsBase = native.Origin;
        document.Entities.Add(new Insert(native, native.Origin));
        using var stream = new MemoryStream(); token.ThrowIfCancellationRequested();
        document.SaveWithDimensionPictures(stream, binary);
        if (stream.Length > MaximumBytes) throw new InvalidOperationException("Exported block library exceeds 8 MiB.");
        return stream.ToArray();
    }
}
