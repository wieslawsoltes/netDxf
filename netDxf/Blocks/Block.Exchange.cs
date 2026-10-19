// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;

namespace netDxf.Blocks
{
    public partial class Block
    {
        /// <summary>Gets the constructor-owned ENDBLK record and its database metadata.</summary>
        public DxfObject EndRecord { get { return this.end; } }

        /// <summary>Creates a detached anonymous definition with an explicit stored name.</summary>
        /// <param name="name">An asterisk, an uppercase ASCII letter, and one or more digits.</param>
        /// <returns>An anonymous block with no document or layout ownership.</returns>
        /// <remarks>The caller chooses a name unique in the destination drawing. This is
        /// for reconstruction of stored definitions; it does not create dynamic block actions.</remarks>
        public static Block CreateAnonymous(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (name.Length < 3 || name.Length > 255 || name[0] != '*' || name[1] < 'A' || name[1] > 'Z')
                throw new ArgumentException("Anonymous names require an asterisk, uppercase letter and digits.", nameof(name));
            for (int index = 2; index < name.Length; index++)
                if (name[index] < '0' || name[index] > '9') throw new ArgumentException("Anonymous name suffixes must contain ASCII digits.", nameof(name));
            return new Block(name, null, null, false) { flags = BlockTypeFlags.AnonymousBlock };
        }

        /// <summary>Creates a named block from the model space of a DXF stream.</summary>
        /// <param name="stream">Readable, seekable DXF input, owned by the caller.</param>
        /// <param name="name">Name of the new block; streams have no file name to infer it from.</param>
        /// <returns>The imported block, or null when the DXF reader cannot load the input.</returns>
        /// <remarks>Uses the document reader's version and Debug/Release error behavior.
        /// The stream remains open. Copied hatches are nonassociative, as in Create.</remarks>
        public static Block Load(Stream stream, string name)
        {
            return Load(stream, name, new List<string>());
        }

        /// <summary>Creates a named block from a DXF stream with resource support folders.</summary>
        /// <param name="stream">Readable, seekable DXF input, owned by the caller.</param>
        /// <param name="name">Name of the new block.</param>
        /// <param name="supportFolders">Folders used by the document reader to resolve resources.</param>
        /// <returns>The imported block, or null when the DXF reader cannot load the input.</returns>
        /// <remarks>Uses the document reader's version and Debug/Release error behavior.
        /// The stream remains open. Only model-space contents are imported.</remarks>
        public static Block Load(Stream stream, string name, IEnumerable<string> supportFolders)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead || !stream.CanSeek)
                throw new ArgumentException("Block DXF input must be readable and seekable.", nameof(stream));
            if (string.IsNullOrWhiteSpace(name) || !IsValidName(name.Trim()))
                throw new ArgumentException("A valid block name is required.", nameof(name));
            if (supportFolders == null) throw new ArgumentNullException(nameof(supportFolders));
            DxfDocument document = DxfDocument.Load(stream, supportFolders);
            return document == null ? null : Create(document, name);
        }

        /// <summary>Creates an independent drawing containing this block's contents in model space.</summary>
        /// <param name="version">DXF database version for the new drawing.</param>
        /// <returns>A new, detached drawing that the caller may inspect, edit or save.</returns>
        /// <remarks>
        /// The block origin and units become INSBASE and INSUNITS. Attribute definitions,
        /// nested INSERTs and their referenced resources are copied. The source is not modified.
        /// This is the same drawing-content format as Save, not an archive of the named root
        /// definition: its name, description, block-record flags and block-level extended data
        /// are not model-space properties. Copied hatches are nonassociative, as in Hatch.Clone.
        /// </remarks>
        public DxfDocument ToDocument(DxfVersion version)
        {
            DxfOpaqueEntity.RejectBlockGeometry(this);
            DxfDocument document = new DxfDocument(version);
            document.DrawingVariables.InsBase = this.origin;
            document.DrawingVariables.InsUnits = this.Record.Units;
            Block model = document.Layouts[Layout.ModelSpaceName].AssociatedBlock;
            foreach (AttributeDefinition definition in this.attributes.Values)
                model.AttributeDefinitions.Add((AttributeDefinition)definition.Clone());

            var copies = new Dictionary<EntityObject, EntityObject>();
            foreach (EntityObject entity in this.entities)
            {
                EntityObject copy = (EntityObject)entity.Clone();
                copies.Add(entity, copy);
                model.Entities.Add(copy);
            }
            MText.RelinkClonedColumns(copies);
            return document;
        }

        /// <summary>Saves this block's contents to a text DXF stream.</summary>
        /// <param name="stream">Writable output, owned by the caller.</param>
        /// <param name="version">DXF database version.</param>
        /// <returns>True on success; otherwise the document writer's failure result.</returns>
        /// <remarks>The stream remains open. See ToDocument for the exported content contract.</remarks>
        public bool Save(Stream stream, DxfVersion version)
        {
            return this.Save(stream, version, false);
        }

        /// <summary>Saves this block's contents to a text or binary DXF stream.</summary>
        /// <param name="stream">Writable output, owned by the caller.</param>
        /// <param name="version">DXF database version.</param>
        /// <param name="isBinary">True for binary DXF; false for text DXF.</param>
        /// <returns>True on success; otherwise the document writer's failure result.</returns>
        /// <remarks>The stream remains open. See ToDocument for the exported content contract.
        /// Uses the document writer's version and Debug/Release error behavior.</remarks>
        public bool Save(Stream stream, DxfVersion version, bool isBinary)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite) throw new ArgumentException("Block DXF output must be writable.", nameof(stream));
            return this.ToDocument(version).Save(stream, isBinary);
        }
    }
}
