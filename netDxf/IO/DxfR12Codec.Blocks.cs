// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Units;
using Attribute = netDxf.Entities.Attribute;

namespace netDxf.IO
{
    public static partial class DxfR12Codec
    {
        private static string BlockName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 31)
                throw new NotSupportedException("R12 block names require 1 through 31 ASCII characters.");
            if (name[0] == '*') ResourceName(name.Substring(1));
            else ResourceName(name);
            if (name.StartsWith("*Model_Space", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("*Paper_Space", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("$Model_Space", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("$Paper_Space", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Layout block references require layout-aware interchange.");
            return name;
        }

        // Kahn's algorithm avoids recursive traversal. Shared subgraphs are visited
        // once; a very deep acyclic drawing cannot exhaust the managed stack.
        private static void RequireAcyclicBlocks(Dictionary<string, List<string>> graph)
        {
            var incoming = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in graph.Keys) incoming.Add(name, 0);
            foreach (List<string> children in graph.Values)
                foreach (string child in children) incoming[child]++;
            var ready = new Queue<string>();
            foreach (var entry in incoming) if (entry.Value == 0) ready.Enqueue(entry.Key);
            int visited = 0;
            while (ready.Count != 0)
            {
                string name = ready.Dequeue(); visited++;
                foreach (string child in graph[name]) if (--incoming[child] == 0) ready.Enqueue(child);
            }
            if (visited != graph.Count)
                throw new NotSupportedException("Recursive block references require a different interchange policy.");
        }

        private static void BlockRecordMetadata(BlockRecord record)
        {
            if (record.XData.Count == 0) { Metadata(record); return; }
            // BLOCK_RECORD did not exist in R12. The one exact version-1 unitless
            // DesignCenter packet is already represented by the required Unitless
            // block setting. Permit that typed projection, not arbitrary ACAD data.
            if (record.ExtensionDictionary != null || record.PersistentReactors.Count != 0
                || record.XData.Count != 1 || !record.XData.TryGetValue("ACAD", out XData data))
                throw new NotSupportedException("Block-record metadata requires an explicit R12 projection.");
            IList<XDataRecord> values = data.XDataRecord;
            if (record.Units != DrawingUnits.Unitless || values.Count != 5
                || values[0].Code != XDataCode.String || (string)values[0].Value != "DesignCenter Data"
                || values[1].Code != XDataCode.ControlString || (string)values[1].Value != "{"
                || values[2].Code != XDataCode.Int16 || (short)values[2].Value != 1
                || values[3].Code != XDataCode.Int16 || (short)values[3].Value != 0
                || values[4].Code != XDataCode.ControlString || (string)values[4].Value != "}")
                throw new NotSupportedException("Only the exact neutral DesignCenter unit packet is projected to R12.");
        }

        private sealed class BlockPacket
        {
            internal Block Source;
            internal string Name, Layer;
            internal Vector3 Origin;
            internal short Flags;
            internal EntityObject[] Entities;
            internal AttributeDefinition[] Attributes;
        }

        private sealed partial class PrimitiveWriter
        {
            private readonly Dictionary<string, BlockPacket> blocks = new Dictionary<string, BlockPacket>(StringComparer.OrdinalIgnoreCase);
            private readonly List<BlockPacket> orderedBlocks = new List<BlockPacket>();
            private readonly Dictionary<string, List<string>> blockGraph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            private string activeBlock;

            private BlockPacket RegisterBlock(Block block)
            {
                if (block == null || block.GetType() != typeof(Block))
                    throw new NotSupportedException("A plain block definition is required.");
                string name = BlockName(block.Name);
                if (this.blocks.TryGetValue(name, out BlockPacket previous))
                {
                    if (!ReferenceEquals(previous.Source, block))
                        throw new InvalidOperationException("Different same-named block objects need an explicit collision policy.");
                    return previous;
                }
                Metadata(block); BlockRecordMetadata(block.Record); Metadata(block.End);
                if (block.Record.Layout != null || block.Record.Units != DrawingUnits.Unitless
                    || !block.Record.AllowExploding || block.Record.ScaleUniformly || block.Description.Length != 0
                    || block.XrefFile.Length != 0 || ((int)block.Flags & ~3) != 0)
                    throw new NotSupportedException("The block contains settings or attributes outside the R12 block profile.");
                if (((block.Flags & BlockTypeFlags.AnonymousBlock) != 0) != (name[0] == '*'))
                    throw new NotSupportedException("The block name and anonymous flag disagree.");
                var packet = new BlockPacket { Source = block, Name = name,
                    Layer = this.RegisterLayer(block.Layer).Name, Origin = FinitePoint(block.Origin),
                    Flags = (short)(((int)block.Flags & 1) | (block.AttributeDefinitions.Values.Any(a => (a.Flags & AttributeFlags.Constant) == 0) ? 2 : 0)),
                    Entities = block.Entities.ToArray(), Attributes = block.AttributeDefinitions.Values.ToArray() };
                this.blocks.Add(name, packet); this.orderedBlocks.Add(packet); this.blockGraph.Add(name, new List<string>());
                return packet;
            }

            private void InsertEntity(Insert insert, Vector3 normal)
            {
                Attribute[] attributes = insert.Attributes.ToArray();
                EndSequence end = insert.ExistingSequenceEnd;
                if (attributes.Length != 0 || end != null) this.Tag(66, (short)1);
                BlockPacket block = this.RegisterBlock(insert.Block);
                if (this.activeBlock != null) this.blockGraph[this.activeBlock].Add(block.Name);
                this.Tag(2, block.Name); this.Point(10, ToObject(insert.Position, normal));
                Vector3 scale = FinitePoint(insert.Scale);
                this.Tag(41, scale.X); this.Tag(42, scale.Y); this.Tag(43, scale.Z);
                this.Tag(50, Finite(insert.Rotation));
                this.Tag(70, insert.ColumnCount); this.Tag(71, insert.RowCount);
                this.Tag(44, Finite(insert.ColumnSpacing)); this.Tag(45, Finite(insert.RowSpacing));
                this.Point(210, normal);
                this.Attributes(insert, attributes, end);
            }

            private List<DxfTag> WriteBlocks()
            {
                var result = new List<DxfTag>();
                List<DxfTag> rootBody = this.body;
                this.body = result;
                try
                {
                    // Registering nested INSERTs appends work rather than recursing.
                    for (int index = 0; index < this.orderedBlocks.Count; index++)
                    {
                        BlockPacket block = this.orderedBlocks[index]; this.activeBlock = block.Name;
                        this.Tag(0, "BLOCK"); this.Tag(5, (this.nextHandle++).ToString("X", CultureInfo.InvariantCulture));
                        this.Tag(8, block.Layer); this.Tag(2, block.Name); this.Tag(70, block.Flags);
                        this.Point(10, block.Origin); this.Tag(3, block.Name); this.Tag(1, "");
                        foreach (AttributeDefinition definition in block.Attributes) this.Definition(definition);
                        foreach (EntityObject entity in block.Entities) this.Entity(entity);
                        this.Tag(0, "ENDBLK"); this.Tag(5, (this.nextHandle++).ToString("X", CultureInfo.InvariantCulture));
                        this.Tag(8, block.Layer);
                    }
                    RequireAcyclicBlocks(this.blockGraph);
                    return result;
                }
                finally { this.body = rootBody; this.activeBlock = null; }
            }
        }

        private sealed class BlockReadPacket
        {
            internal DxfRawRecord Header, End;
            internal readonly List<DxfRawRecord> Records = new List<DxfRawRecord>();
            internal Block Block;
        }

        private sealed partial class BlockReader
        {
            private readonly DxfRawDocument document;
            private readonly Dictionary<string, Linetype> patterns;
            private readonly Dictionary<string, Layer> layers;
            private readonly Dictionary<string, TextStyle> styles;
            private readonly HashSet<string> handles;
            private Dictionary<string, BlockReadPacket> index;
            private readonly List<BlockReadPacket> selected = new List<BlockReadPacket>();
            private readonly Dictionary<string, List<string>> graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            internal BlockReader(DxfRawDocument document, Dictionary<string, Linetype> patterns,
                Dictionary<string, Layer> layers, Dictionary<string, TextStyle> styles, HashSet<string> handles)
            { this.document = document; this.patterns = patterns; this.layers = layers; this.styles = styles; this.handles = handles; }

            private void IndexBlocks()
            {
                if (this.index != null) return;
                this.index = new Dictionary<string, BlockReadPacket>(StringComparer.OrdinalIgnoreCase);
                bool found = false;
                foreach (DxfRawSection section in this.document.Sections)
                {
                    if (!string.Equals(section.Name, "BLOCKS", StringComparison.OrdinalIgnoreCase)) continue;
                    if (found) throw new FormatException("Duplicate BLOCKS sections are ambiguous.");
                    found = true;
                    BlockReadPacket current = null;
                    foreach (DxfRawRecord record in section.Records)
                    {
                        if (string.Equals(record.Name, "BLOCK", StringComparison.OrdinalIgnoreCase))
                        {
                            if (current != null) throw new FormatException("Nested BLOCK declarations are invalid.");
                            var names = record.Tags.Where(t => t.Code == 2).ToArray();
                            if (names.Length != 1) throw new FormatException("A BLOCK requires one unambiguous name.");
                            string name = (string)names[0].Value;
                            if (this.index.ContainsKey(name)) throw new FormatException("Duplicate block names are ambiguous.");
                            current = new BlockReadPacket { Header = record };
                            this.index.Add(name, current);
                        }
                        else if (string.Equals(record.Name, "ENDBLK", StringComparison.OrdinalIgnoreCase))
                        {
                            if (current == null) throw new FormatException("ENDBLK has no BLOCK.");
                            current.End = record; current = null;
                        }
                        else
                        {
                            if (current == null) throw new FormatException("Block content is outside a BLOCK/ENDBLK pair.");
                            current.Records.Add(record);
                        }
                    }
                    if (current != null) throw new FormatException("Unterminated BLOCK definition.");
                }
            }

            private string Identity(Fields fields)
            {
                string value = fields.Identity();
                if (value != null && !this.handles.Add(value)) throw new FormatException("Duplicate R12 graph identities are ambiguous.");
                return value;
            }

            private Layer Layer(string name)
            {
                name = ResourceName(name);
                if (!this.layers.TryGetValue(name, out Layer layer))
                { layer = new Layer(name); this.layers.Add(name, layer); }
                return layer;
            }

            private Block Resolve(string name)
            {
                name = BlockName(name); this.IndexBlocks();
                if (!this.index.TryGetValue(name, out BlockReadPacket packet))
                    throw new FormatException("INSERT references an undefined BLOCK: " + name);
                if (packet.Block != null) return packet.Block;
                var fields = new Fields(packet.Header);
                string handle = this.Identity(fields), storedName = BlockName(fields.Text(2, null, true));
                string repeatedName = fields.Text(3, storedName);
                if (!string.Equals(storedName, repeatedName, StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("BLOCK names in groups 2 and 3 disagree.");
                short flags = fields.Integer(70, 0);
                if ((flags & ~3) != 0 || fields.Integer(67, 0) != 0 || fields.Text(1, "").Length != 0)
                    throw new NotSupportedException("External, attribute or layout blocks require a different profile.");
                if (((flags & 1) != 0) != (storedName[0] == '*'))
                    throw new FormatException("The BLOCK name and anonymous flag disagree.");
                Vector3 origin = fields.Vector(10, Vector3.Zero, true);
                Layer layer = this.Layer(fields.Text(8, "0")); fields.Finish();
                var end = new Fields(packet.End);
                string endHandle = this.Identity(end);
                if (end.Integer(67, 0) != 0 || !string.Equals(end.Text(8, "0"), layer.Name, StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("An ENDBLK with a distinct layer or layout is outside the typed profile.");
                end.Finish();
                var block = new Block(storedName, null, null, false)
                    { Origin = origin, Layer = layer, Flags = (BlockTypeFlags)flags, Handle = handle };
                block.Record.Units = DrawingUnits.Unitless;
                block.End.Handle = endHandle;
                packet.Block = block; this.selected.Add(packet); this.graph.Add(storedName, new List<string>());
                return block;
            }

            internal Insert ReadInsert(Fields fields, Vector3 normal, double thickness,
                IReadOnlyList<DxfRawRecord> records, ref int position, string layerName)
            {
                short followed = fields.Integer(66, 0);
                if (thickness != 0 || followed < 0 || followed > 1)
                    throw new NotSupportedException("INSERT thickness or sequence flag is outside the R12 profile.");
                EndSequence end = null;
                List<Attribute> attributes = followed == 0 ? new List<Attribute>() : this.ReadAttributes(records, ref position, out end);
                Block block = this.Resolve(fields.Text(2, null, true));
                // Do not manufacture attributes from definitions or transform source placement.
                var insert = new Insert(attributes) { Block = block, Normal = normal,
                    Position = ToWorld(fields.Vector(10, Vector3.Zero, true), normal),
                    Rotation = fields.Number(50, 0), ColumnCount = fields.Integer(70, 1), RowCount = fields.Integer(71, 1),
                    ColumnSpacing = fields.Number(44, 0), RowSpacing = fields.Number(45, 0) };
                insert.RestoreScale(new Vector3(fields.Number(41, 1), fields.Number(42, 1), fields.Number(43, 1)));
                insert.RestoreSequenceEnd(end);
                this.attributeOwners.Add(insert);
                return insert;
            }

            internal void Complete()
            {
                for (int index = 0; index < this.selected.Count; index++)
                {
                    BlockReadPacket packet = this.selected[index];
                    List<EntityObject> entities = ReadEntityRecords(packet.Records, this.patterns, this.layers, this.styles, this.handles, this, packet.Block);
                    foreach (EntityObject entity in entities)
                    {
                        packet.Block.Entities.Add(entity);
                        if (entity is Insert insert) this.graph[packet.Block.Name].Add(insert.Block.Name);
                    }
                }
                RequireAcyclicBlocks(this.graph);
                this.BindAttributes();
            }
        }
    }
}
