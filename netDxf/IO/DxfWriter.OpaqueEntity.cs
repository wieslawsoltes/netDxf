// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Linq;
using netDxf.Collections;
using netDxf.Entities;
namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        internal void PreflightOpaqueEntities(DxfDocument document, bool binary)
        { this.doc = document; this.isBinary = binary; this.ValidateOpaqueEntities(); }
        private void ValidateOpaqueEntities()
        {
            int total = 0;
            var entities = this.doc.Blocks.SelectMany(block => block.Entities).OfType<DxfOpaqueEntity>().ToList();
            foreach (DxfOpaqueEntity entity in entities)
            {
                entity.Validate(this.doc);
                var tags = entity.OutputTags(text => text);
                if (tags.Count > 65536 || (total += tags.Count - 1) > 1048576)
                    throw new InvalidOperationException("Unknown entity output exceeds the reader's tag admission budget.");
                foreach (DxfTag tag in tags)
                {
                    if (this.isBinary && tag.Code == 999)
                        throw new NotSupportedException("Retained unknown entity comments require ASCII output.");
                    if (this.isBinary && tag.Value is byte[] bytes && bytes.Length > byte.MaxValue)
                        throw new NotSupportedException("Retained unknown entity binary chunks exceed binary transport framing.");
                    if (!(tag.Value is string text)) continue;
                    this.ValidateOpaqueText(text);
                }
            }
            if (entities.Count != 0) this.ValidateOpaqueEntityClasses(this.PrepareClassDefinitions());
        }
        private void ValidateOpaqueEntityClasses(DxfClassCollection definitions)
        {
            foreach (DxfOpaqueEntity entity in this.doc.Blocks.SelectMany(block => block.Entities).OfType<DxfOpaqueEntity>())
            {
                entity.ValidatePreparedClass(definitions);
                if (!definitions.Contains(entity.CodeName)) continue;
                DxfClass definition = definitions[entity.CodeName];
                this.ValidateOpaqueText(definition.Name);
                this.ValidateOpaqueText(definition.CppClassName);
                this.ValidateOpaqueText(definition.ApplicationName);
            }
        }
        private void ValidateOpaqueText(string text)
        {
            CheckStyleUnicode(text);
            if (text.IndexOf('\0') >= 0 || !this.isBinary && text.Any(c => c == '\r' || c == '\n'))
                throw new InvalidOperationException("Unknown entity contains a string unsupported by the selected transport.");
        }
        private bool IsOpaqueEntityClass(string name)
        {
            return this.doc.Blocks.SelectMany(block => block.Entities).OfType<DxfOpaqueEntity>()
                .Any(entity => string.Equals(entity.CodeName, name, StringComparison.OrdinalIgnoreCase));
        }
        private void WriteOpaqueEntity(DxfOpaqueEntity entity)
        {
            foreach (DxfTag tag in entity.OutputTags(this.EncodeDatabaseString)) this.chunk.Write(tag.Code, tag.Value);
        }
    }
}
