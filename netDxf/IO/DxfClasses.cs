#region netDxf library licensed under the MIT License
// 
//                       netDxf library
// Copyright (c) Daniel Carvajal (haplokuon@gmail.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
#endregion

using System;
using System.IO;
using netDxf.Blocks;
using netDxf.Collections;
using netDxf.Entities;
using netDxf.Header;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private void ReadClassDefinitions()
        {
            this.ReadNextClassTag();
            while (true)
            {
                if (this.chunk.Code != 0) throw new InvalidDataException("Expected a CLASS record boundary.");
                string type = this.chunk.ReadString();
                if (type == DxfObjectCode.EndSection) return;
                if (type == DxfObjectCode.EndOfFile) throw new EndOfStreamException("CLASSES ended without ENDSEC.");
                if (type != DxfObjectCode.Class) throw new InvalidDataException("Unexpected record in CLASSES: " + type);
                string name = null, cppName = null, application = string.Empty;
                int flags = 0; int? count = null; bool wasProxy = false, isEntity = false;
                var sourceFields = new System.Collections.Generic.HashSet<short>();
                bool opaqueQualified = true;
                this.ReadNextClassTag();
                while (this.chunk.Code != 0)
                {
                    if (!sourceFields.Add(this.chunk.Code)) opaqueQualified = false;
                    switch (this.chunk.Code)
                    {
                        case 1: name = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString()); break;
                        case 2: cppName = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString()); break;
                        case 3: application = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString()); break;
                        case 90: flags = this.chunk.ReadInt(); break;
                        case 91: count = this.chunk.ReadInt(); break;
                        case 280: wasProxy = this.ReadClassFlag(); break;
                        case 281: isEntity = this.ReadClassFlag(); break;
                        // Unknown class-field tags remain outside this typed-definition feature.
                        default: opaqueQualified = false; break;
                    }
                    this.ReadNextClassTag();
                }
                if (!opaqueQualified && name != null) this.unqualifiedOpaqueClasses.Add(name);
                try
                {
                    this.doc.Classes.Add(new DxfClass(name, cppName, application)
                    {
                        ProxyFlags = flags, InstanceCount = count, WasProxy = wasProxy, IsEntity = isEntity
                    });
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidDataException("Invalid or duplicate CLASS definition: " + name, exception);
                }
            }
        }

        private bool ReadClassFlag()
        {
            short value = this.chunk.ReadShort();
            if (value != 0 && value != 1)
                throw new InvalidDataException(string.Format("CLASS group {0} must be 0 or 1 at position {1}.", this.chunk.Code, this.chunk.CurrentPosition));
            return value == 1;
        }

        private void ReadNextClassTag()
        {
            do { this.chunk.Next(); } while (this.chunk.Code == 999);
        }
    }

    internal sealed partial class DxfWriter
    {
        private DxfClassCollection PrepareClassDefinitions()
        {
            var definitions = new DxfClassCollection();
            foreach (DxfClass definition in this.doc.Classes)
                definitions.Add((DxfClass) definition.Clone());

            this.AddGeneratedClass(definitions, DxfObjectCode.RasterVariables, SubclassMarker.RasterVariables, 0, false, 1);
            if (this.doc.ImageDefinitions.Count > 0)
            {
                int images = 0;
                foreach (Block block in this.doc.Blocks)
                    foreach (EntityObject entity in block.Entities)
                        if (entity is Image) images = checked(images + 1);
                this.AddGeneratedClass(definitions, DxfObjectCode.ImageDef, SubclassMarker.RasterImageDef, 0, false, this.doc.ImageDefinitions.Count);
                this.AddGeneratedClass(definitions, DxfObjectCode.ImageDefReactor, SubclassMarker.RasterImageDefReactor, 1, false, images);
                this.AddGeneratedClass(definitions, DxfObjectCode.Image, SubclassMarker.RasterImage, 127, true, images);
            }
            else
            {
                // Keep unused source declarations, but do not retain stale counts for types we own.
                foreach (string name in new[] { DxfObjectCode.ImageDef, DxfObjectCode.ImageDefReactor, DxfObjectCode.Image })
                    if (definitions.Contains(name)) definitions[name].InstanceCount = 0;
            }
            this.PrepareHelixClass(definitions);
            this.PrepareDatabaseClasses(definitions);
            return definitions;
        }

        private void AddGeneratedClass(DxfClassCollection definitions, string name, string cppName, int flags, bool entity, int count)
        {
            if (definitions.Contains(name))
            {
                DxfClass existing = definitions[name];
                if (existing.CppClassName != cppName || existing.IsEntity != entity)
                    throw new InvalidDataException("CLASS conflicts with a generated raster definition: " + name);
                existing.InstanceCount = count;
            }
            else
            {
                definitions.Add(new DxfClass(name, cppName, "ISM")
                {
                    ProxyFlags = flags, IsEntity = entity, InstanceCount = count
                });
            }
        }

        private void WriteClassDefinition(DxfClass definition)
        {
            Func<string, string> encode = this.IsOpaqueEntityClass(definition.Name)
                ? (Func<string, string>) this.EncodeDatabaseString : this.EncodeNonAsciiCharacters;
            this.chunk.Write(0, DxfObjectCode.Class);
            this.chunk.Write(1, encode(definition.Name));
            this.chunk.Write(2, encode(definition.CppClassName));
            this.chunk.Write(3, encode(definition.ApplicationName));
            this.chunk.Write(90, definition.ProxyFlags);
            if (this.doc.DrawingVariables.AcadVer > DxfVersion.AutoCad2000 && definition.InstanceCount.HasValue)
                this.chunk.Write(91, definition.InstanceCount.Value);
            this.chunk.Write(280, (short) (definition.WasProxy ? 1 : 0));
            this.chunk.Write(281, (short) (definition.IsEntity ? 1 : 0));
        }
    }
}
