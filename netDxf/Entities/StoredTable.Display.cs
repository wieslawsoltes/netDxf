// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using netDxf.Blocks;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace netDxf.Entities
{
    public sealed partial class StoredTable
    {
        /// <summary>Gets the actual registered display block, or null when its source name was unresolved.</summary>
        /// <remarks>The returned block is an ordinary mutable document object, not a frozen geometry snapshot.</remarks>
        public Block DisplayBlock { get { return this.displayBlock; } }

        /// <summary>Atomically selects an existing source-document block as the qualified TABLE's display.</summary>
        /// <param name="block">An already registered flat block of LINE, SOLID and MTEXT entities, without attributes or a layout.</param>
        /// <remarks>
        /// Updates both the public block name and group-343 BLOCK_RECORD reference, and clears stale proxy
        /// graphics. Source-bound resource references and earlier payload snapshots remain intact. Selecting
        /// the current identity is a no-op, including after a rename, and does not clear its proxy graphics.
        /// No block is registered or erased and no handles are allocated. BuildDisplayBlock and ordinary
        /// document block registration are separate preceding operations, not rolled back by this method.
        /// This replaces a display representation only: inline cell values, TABLECONTENT, TABLEGEOMETRY,
        /// row sizes, formatting caches and native regeneration policy remain unchanged. It is not a native
        /// RecomputeTableBlock implementation. Unknown/private TABLE schemas reject rather than guessing
        /// where their display references are stored. Concurrent-thread mutation is not supported.
        /// </remarks>
        public void ReplaceDisplayBlock(Block block)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));
            this.Validate(this.source);
            if (!ReferenceEquals(this.source.GetObjectByHandle(this.Handle), this))
                throw new InvalidOperationException("The source TABLE must remain registered.");
            IReadOnlyList<string> errors = this.source.Objects.Validate();
            if (errors.Count != 0)
                throw new InvalidOperationException("Cannot replace a display in an invalid source graph: " + string.Join("; ", errors));
            if (!ReferenceEquals(block.Record.Owner, this.source.Blocks) ||
                !this.source.Blocks.TryGetValue(block.Name, out Block registered) || !ReferenceEquals(registered, block) ||
                !ReferenceEquals(this.source.GetObjectByHandle(block.Record.Handle), block.Record) ||
                !ReferenceEquals(this.source.GetObjectByHandle(block.Handle), block))
                throw new ArgumentException("The display must be the actual registered block in the source document.", nameof(block));
            if (this.Grid == null || !this.payload.Where(tag => tag.Code == 100).Select(tag => (string)tag.Value)
                .SequenceEqual(new[] { "AcDbBlockReference", "AcDbTable" }) || this.payload.Any(tag => tag.Code == 102))
                throw new NotSupportedException("Only the qualified two-subclass TABLE display references can be replaced.");
            int boundary = this.payload.ToList().FindIndex(1, tag => tag.Code == 100);
            var names = this.payload.Take(boundary).Where(tag => tag.Code == 2).ToArray();
            var pointers = this.payload.Skip(boundary + 1).TakeWhile(tag => tag.Code != 171).Where(tag => tag.Code == 343).ToArray();
            if (names.Length != 1 || pointers.Length != 1 || this.displayBlock == null ||
                !this.handles.TryGetValue(CanonicalHandle((string)pointers[0].Value), out DxfObject previous) ||
                !ReferenceEquals(previous, this.displayBlock.Record))
                throw new NotSupportedException("The TABLE display name and BLOCK_RECORD reference must identify the same source block.");
            if (ReferenceEquals(block, this.displayBlock)) return;
            // Flat generated primitives cannot introduce a recursive INSERT/TABLE block graph.
            if (block.IsXRef || block.Record.Layout != null || block.AttributeDefinitions.Count != 0 ||
                block.Entities.Any(entity => !(entity is Line) && !(entity is Solid) && !(entity is MText)))
                throw new NotSupportedException("Display replacement requires a local flat LINE/SOLID/MTEXT block without attributes or layout ownership.");

            DxfStoredTableContent.CheckEditableText(block.Name, nameof(block));
            var encoded = new StringBuilder();
            foreach (char value in block.Name)
            {
                bool escape = value == '\\' || this.SourceVersion < DxfVersion.AutoCad2007 && value > 127;
                if (encoded.Length > DxfStoredTableContent.MaximumEditedStringLength - (escape ? 7 : 1))
                    throw new ArgumentOutOfRangeException(nameof(block), "The encoded block name exceeds the stored edit limit.");
                if (escape) encoded.Append("\\U+").Append(((int)value).ToString("X4", CultureInfo.InvariantCulture));
                else encoded.Append(value);
            }
            var name = new DxfTag(2, encoded.ToString());
            var pointer = new DxfTag(343, block.Record.Handle);
            var packet = this.payload.Select(tag => ReferenceEquals(tag, names[0]) ? name :
                ReferenceEquals(tag, pointers[0]) ? pointer : tag).ToList().AsReadOnly();
            var nextHandles = new Dictionary<string, DxfObject>(StringComparer.OrdinalIgnoreCase);
            var nextReferences = new List<DxfObject>();
            foreach (var tag in packet)
            {
                if (!IsSemanticReference(tag)) continue;
                string handle = CanonicalHandle((string)tag.Value);
                DxfObject target;
                if (ReferenceEquals(tag, pointer)) target = block.Record;
                else if (!this.handles.TryGetValue(handle, out target)) continue;
                nextHandles[handle] = target; nextReferences.Add(target);
            }
            nextReferences.Add(block.Record); // Independently retained name-based reference.
            foreach (var tag in packet)
                if (this.namedStyles.TryGetValue(tag, out Tuple<netDxf.Tables.TextStyle, string> binding))
                    nextReferences.Add(binding.Item1);
            // All allocations and checks have finished. No user callbacks run during these swaps.
            this.payload = packet; this.handles = nextHandles; this.references = nextReferences;
            this.displayBlock = block; this.displayName = block.Name; this.initialDisplayBlockName = block.Name;
            this.ClearProxyGraphics();
        }
    }
}
