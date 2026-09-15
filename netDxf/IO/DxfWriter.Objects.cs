using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Entities;
using netDxf.Collections;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private string EncodeDatabaseString(string value)
        {
            return this.EncodeNonAsciiCharacters(value.Replace("\\", "\\U+005C"));
        }
        private void ValidateDatabaseTransport()
        {
            if (this.isBinary) return;
            foreach (DxfDatabaseObject item in this.doc.Objects.Items)
            {
                if (item is DxfDictionaryVariable variable) CheckDatabaseText(variable.Value);
                if (item is DxfXRecord record)
                    foreach (DxfTag tag in record.Data) if (tag.Value is string text) CheckDatabaseText(text);
                if (item is DxfOpaqueObject opaque)
                    foreach (DxfTag tag in opaque.Tags) if (tag.Value is string text) CheckDatabaseText(text);
                foreach (XData data in item.XData.Values)
                    foreach (XDataRecord tag in data.XDataRecord) if (tag.Value is string text) CheckDatabaseText(text);
            }
        }
        private static void CheckDatabaseText(string value)
        {
            if (value.IndexOfAny(new[] { '\r', '\n' }) >= 0) throw new System.IO.InvalidDataException("Text DXF cannot encode a database string containing CR or LF; use binary DXF or replace the line break.");
        }
        private void WriteDatabaseMetadata(DxfObject item, IEnumerable<string> automaticReactors = null)
        {
            item = this.doc.GetObjectByHandle(item.Handle) ?? item;
            if (item.ExtensionDictionary != null)
            {
                this.chunk.Write(102, "{ACAD_XDICTIONARY");
                this.chunk.Write(360, item.ExtensionDictionary.Handle);
                this.chunk.Write(102, "}");
            }
            HashSet<string> reactors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (automaticReactors != null) foreach (string handle in automaticReactors) reactors.Add(handle);
            if (item is EntityObject entity) foreach (DxfObject reactor in entity.Reactors) reactors.Add(reactor.Handle);
            foreach (DxfObject reactor in item.PersistentReactors)
            {
                if (reactor == null) throw new InvalidOperationException("A persistent reactor cannot be null.");
                reactors.Add(reactor.Handle);
            }
            if (reactors.Count > 0)
            {
                this.chunk.Write(102, "{ACAD_REACTORS");
                foreach (string handle in reactors) this.chunk.Write(330, handle);
                this.chunk.Write(102, "}");
            }
        }
        private void PrepareDatabaseClasses(DxfClassCollection definitions)
        {
            string[] names = { "DICTIONARYVAR", "ACDBDICTIONARYWDFLT", "ACDBPLACEHOLDER", "IDBUFFER", "SORTENTSTABLE", "SPATIAL_FILTER", "PLOTSETTINGS", "WIPEOUTVARIABLES" };
            string[] cppNames = { "AcDbDictionaryVar", "AcDbDictionaryWithDefault", "AcDbPlaceHolder", "AcDbIdBuffer", "AcDbSortentsTable", "AcDbSpatialFilter", "AcDbPlotSettings", "AcDbWipeoutVariables" };
            for (int i = 0; i < names.Length; i++)
            {
                int count = this.doc.Objects.Items.Count(o => o.CodeName == names[i]);
                if (definitions.Contains(names[i]))
                {
                    DxfClass definition = definitions[names[i]];
                    if (definition.CppClassName != cppNames[i] || definition.IsEntity) throw new System.IO.InvalidDataException("CLASS conflicts with a typed database object: " + names[i]);
                    definition.InstanceCount = count;
                }
                else if (count > 0) definitions.Add(new DxfClass(names[i], cppNames[i], "ObjectDBX Classes") { ProxyFlags = 0, IsEntity = false, InstanceCount = count });
            }
            this.PrepareStoredEnvelopeClasses(definitions);
            this.PrepareGeoDataClass(definitions);
            this.PrepareLayerFilterPointerClasses(definitions);
            this.PrepareMultiLeaderClasses(definitions);
            this.PrepareLightListClass(definitions);
            this.PrepareDataTableClass(definitions);
        }
        private void WriteDatabaseObject(DxfDatabaseObject item, DictionaryObject generatedRoot = null)
        {
            this.chunk.Write(0, item.CodeName);
            this.chunk.Write(5, item.Handle);
            this.WriteDatabaseMetadata(item);
            this.chunk.Write(330, item.Owner?.Handle ?? "0");
            if (item is DxfDictionary dictionary)
            {
                this.chunk.Write(100, "AcDbDictionary");
                this.chunk.Write(280, dictionary.IsHardOwner ? (short)1 : (short)0);
                this.chunk.Write(281, (short)dictionary.Cloning);
                if (generatedRoot != null)
                    foreach (KeyValuePair<string, string> entry in generatedRoot.Entries)
                    {
                        this.chunk.Write(3, this.EncodeNonAsciiCharacters(entry.Value));
                        this.chunk.Write(350, entry.Key);
                    }
                foreach (DxfDictionaryEntry entry in dictionary.Entries)
                {
                    this.chunk.Write(3, this.EncodeDatabaseString(entry.Name));
                    this.chunk.Write(entry.IsHardOwner ? (short)360 : (short)350, entry.Target.Handle);
                }
                if (dictionary is DxfDictionaryWithDefault fallback)
                {
                    this.chunk.Write(100, "AcDbDictionaryWithDefault");
                    this.chunk.Write(340, fallback.Default?.Handle ?? "0");
                }
            }
            else if (item is DxfXRecord record)
            {
                this.chunk.Write(100, "AcDbXrecord");
                this.chunk.Write(280, (short)record.Cloning);
                foreach (DxfTag tag in record.Data) this.WriteDatabaseTag(tag, true);
            }
            else if (item is DxfDictionaryVariable variable)
            {
                this.chunk.Write(100, "DictionaryVariables");
                this.chunk.Write(280, variable.Schema);
                this.chunk.Write(1, this.EncodeDatabaseString(variable.Value));
            }
            else if (item is DxfPlaceholder) { /* ACDBPLACEHOLDER has no subclass payload. */ }
            else if (this.WriteStoredEnvelopePayload(item)) { }
            else if (this.WriteContainerPayload(item)) { }
            else if (this.WriteGeoDataPayload(item)) { }
            else if (this.WriteOutputSettingsPayload(item)) { }
            else if (this.WriteMLeaderStylePayload(item)) { }
            else if (this.WriteLayerFilterPointerPayload(item)) { }
            else if (this.WriteLightListPayload(item)) { }
            else if (this.WriteDataTablePayload(item)) { }
            else if (item is DxfOpaqueObject opaque)
                foreach (DxfTag tag in opaque.Tags) this.WriteDatabaseTag(tag, false);
            this.WriteXData(item.XData);
        }
        private void WriteDatabaseTag(DxfTag tag, bool encodeStrings)
        {
            object value = tag.Value;
            if (encodeStrings && tag.ValueType == DxfTagValueType.String) value = this.EncodeDatabaseString((string)value);
            this.chunk.Write(tag.Code, value);
        }
    }
}
