// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;
using netDxf.Units;

namespace netDxf.IO
{
    public static partial class DxfR12Codec
    {
        internal static DxfDocument CreateSelectionDocument(DxfRawDocument snapshot, DxfVersion version,
            CancellationToken cancellation)
        {
            IReadOnlyList<EntityObject> roots = ReadEntities(snapshot);
            cancellation.ThrowIfCancellationRequested();
            var document = new DxfDocument(version);
            document.DrawingVariables.InsUnits = DrawingUnits.Unitless;
            SeedSelectionResources(snapshot, document, cancellation);

            // Discover by canonical name without recursive block walking. ReadEntities already
            // rejects cycles and ambiguous names; retain a defensive topological check here.
            var blocks = new Dictionary<string, Block>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<Block>();
            foreach (Insert root in roots.OfType<Insert>()) pending.Enqueue(root.Block);
            while (pending.Count != 0)
            {
                cancellation.ThrowIfCancellationRequested();
                Block block = pending.Dequeue();
                if (blocks.ContainsKey(block.Name)) continue;
                blocks.Add(block.Name, block);
                foreach (Insert child in block.Entities.OfType<Insert>()) pending.Enqueue(child.Block);
            }
            var unresolved = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var parents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in blocks.Keys) { unresolved.Add(name, 0); parents.Add(name, new List<string>()); }
            foreach (Block block in blocks.Values)
                foreach (Insert child in block.Entities.OfType<Insert>())
                { unresolved[block.Name]++; parents[child.Block.Name].Add(block.Name); }
            var ready = new Queue<string>();
            foreach (var entry in unresolved) if (entry.Value == 0) ready.Enqueue(entry.Key);
            int added = 0;
            while (ready.Count != 0)
            {
                cancellation.ThrowIfCancellationRequested();
                string name = ready.Dequeue();
                document.Blocks.Add(blocks[name]); added++;
                foreach (string parent in parents[name]) if (--unresolved[parent] == 0) ready.Enqueue(parent);
            }
            if (added != blocks.Count) throw new InvalidDataException("The normalized selection contains a recursive block graph.");
            foreach (EntityObject entity in roots)
            { cancellation.ThrowIfCancellationRequested(); document.Entities.Add(entity); }
            return document;
        }

        private static void SeedSelectionResources(DxfRawDocument snapshot, DxfDocument document,
            CancellationToken cancellation)
        {
            // A fresh DxfDocument contains Layer 0, Standard and Continuous. Canonical adoption
            // alone would retain those defaults and silently discard selected resource settings.
            var patterns = ReadLinetypes(snapshot);
            foreach (Linetype source in patterns.Values)
            {
                cancellation.ThrowIfCancellationRequested();
                if (string.Equals(source.Name, "BYLAYER", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(source.Name, "BYBLOCK", StringComparison.OrdinalIgnoreCase)) continue;
                if (!document.Linetypes.Contains(source.Name)) document.Linetypes.Add(source);
                else
                {
                    Linetype target = document.Linetypes[source.Name];
                    target.Description = source.Description;
                    target.Segments.Clear();
                    foreach (LinetypeSegment segment in source.Segments)
                        target.Segments.Add((LinetypeSegment)segment.Clone());
                }
            }
            foreach (DxfRawRecord record in snapshot.Sections.Where(s => s.Name == "TABLES").SelectMany(s => s.Records))
            {
                cancellation.ThrowIfCancellationRequested();
                if (record.Name == "LAYER")
                {
                    Layer source = ReadLayer(record, patterns);
                    source.Linetype = document.Linetypes[source.Linetype.Name];
                    if (!document.Layers.Contains(source.Name)) document.Layers.Add(source);
                    else
                    {
                        Layer target = document.Layers[source.Name];
                        target.Color = source.Color; target.Linetype = source.Linetype;
                        target.IsVisible = source.IsVisible; target.IsFrozen = source.IsFrozen;
                        target.IsFrozenInNewViewports = source.IsFrozenInNewViewports; target.IsLocked = source.IsLocked;
                    }
                }
                else if (record.Name == "STYLE")
                {
                    TextStyle source = ReadTextStyle(record);
                    if (!document.TextStyles.Contains(source.Name)) document.TextStyles.Add(source);
                    else
                    {
                        TextStyle target = document.TextStyles[source.Name];
                        target.Height = source.Height; target.WidthFactor = source.WidthFactor;
                        target.ObliqueAngle = source.ObliqueAngle; target.IsVertical = source.IsVertical;
                        target.TextGenerationFlags = source.TextGenerationFlags;
                        target.SetStoredFontFiles(source.FontFile, source.BigFont);
                    }
                }
            }
        }
    }
}
