using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly HashSet<string> managedReactorHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<DatabaseRecord> databaseRecords = new List<DatabaseRecord>();
        private readonly Dictionary<string, DatabaseMetadata> entityDatabaseMetadata = new Dictionary<string, DatabaseMetadata>(StringComparer.OrdinalIgnoreCase);
        private sealed class DatabaseMetadata
        {
            internal string Owner;
            internal string Extension;
            internal readonly List<string> Reactors = new List<string>();
        }
        private sealed class DatabaseRecord
        {
            internal DxfDatabaseObject Object;
            internal SourceRecordIdentity SourceIdentity;
            internal DatabaseMetadata Metadata = new DatabaseMetadata();
            internal readonly List<Tuple<string, string, bool>> Entries = new List<Tuple<string, string, bool>>();
            internal string Default;
            internal readonly List<string> ContainerReferences = new List<string>();
            internal readonly List<string> SortKeys = new List<string>();
        }
        private DatabaseRecord ReadDatabaseRecord()
        {
            SourceRecordIdentity source = this.CurrentSourceRecord;
            string codeName = this.chunk.ReadString();
            List<DxfTag> tags = new List<DxfTag>();
            this.chunk.Next();
            while (this.chunk.Code != 0)
            {
                if (this.chunk.Code != 999) tags.Add(new DxfTag(this.chunk.Code, this.chunk.Value));
                this.chunk.Next();
            }
            if (codeName == "TABLESTYLE" && this.doc.DrawingVariables.AcadVer >= netDxf.Header.DxfVersion.AutoCad2004
                && tags.FirstOrDefault(t => t.Code == 100)?.Value as string == "AcDbTableStyle")
            {
                DatabaseRecord style = this.ReadTableStyleRecord(tags);
                style.SourceIdentity = source;
                this.databaseRecords.Add(style);
                return style;
            }
            if (codeName == "TABLECONTENT")
            {
                DatabaseRecord content = this.ReadStoredTableContentRecord(tags);
                content.SourceIdentity = source;
                this.databaseRecords.Add(content);
                return content;
            }
            if (codeName == "SUN")
            {
                DatabaseRecord sun = this.ReadSunRecord(tags);
                sun.SourceIdentity = source;
                this.databaseRecords.Add(sun);
                return sun;
            }
            if (codeName == "FIELD" || codeName == "ACAD_FIELD")
            {
                DatabaseRecord field = this.ReadStoredFieldRecord(codeName, tags);
                field.SourceIdentity = source; this.databaseRecords.Add(field); return field;
            }
            if (codeName == "DIMASSOC")
            {
                DatabaseRecord association = this.ReadStoredDimAssocRecord(tags);
                association.SourceIdentity = source;
                this.databaseRecords.Add(association);
                return association;
            }
            if (codeName == "DATATABLE")
            {
                DatabaseRecord table = this.ReadDataTableRecord(tags);
                table.SourceIdentity = source;
                this.databaseRecords.Add(table);
                return table;
            }
            if (codeName == "SECTIONSETTINGS" || codeName == "SECTION_SETTINGS")
            {
                DatabaseRecord envelope = this.ReadSectionSettingsRecord(codeName, tags);
                envelope.SourceIdentity = source;
                this.databaseRecords.Add(envelope);
                return envelope;
            }
            if (codeName == "SECTIONMANAGER" || codeName == "SECTION_MANAGER")
            {
                DatabaseRecord manager = this.ReadSectionManagerRecord(codeName, tags);
                manager.SourceIdentity = source;
                this.databaseRecords.Add(manager);
                return manager;
            }
            if (codeName == "LAYER_INDEX")
            {
                DatabaseRecord envelope = this.ReadLayerIndexRecord(tags);
                envelope.SourceIdentity = source;
                this.databaseRecords.Add(envelope);
                return envelope;
            }
            if (codeName == "LAYER_FILTER" || codeName == "OBJECT_PTR")
            {
                DatabaseRecord envelope = this.ReadLayerFilterPointerRecord(codeName, tags);
                envelope.SourceIdentity = source;
                this.databaseRecords.Add(envelope);
                return envelope;
            }
            if (codeName == "XRECORD" && this.TryReadPrivateXRecord(tags, out DatabaseRecord privateRecord))
            {
                privateRecord.SourceIdentity = source;
                this.databaseRecords.Add(privateRecord);
                return privateRecord;
            }
            DatabaseRecord result = new DatabaseRecord { SourceIdentity = source };
            string handle = null;
            int payload = 0;
            for (; payload < tags.Count; payload++)
            {
                DxfTag tag = tags[payload];
                if (tag.Code == 100 || tag.Code == 1001) break;
                if (tag.Code == 5) handle = (string)tag.Value;
                else if (tag.Code == 330) result.Metadata.Owner = (string)tag.Value;
                else if (tag.Code == 102)
                {
                    string group = (string)tag.Value;
                    bool closed = false;
                    int depth = 1;
                    while (++payload < tags.Count)
                    {
                        tag = tags[payload];
                        if (tag.Code == 102)
                        {
                            string control = (string)tag.Value;
                            if (control == "}" && --depth == 0) { closed = true; break; }
                            if (control.StartsWith("{", StringComparison.Ordinal)) depth++;
                            continue;
                        }
                        if (depth == 1 && group == "{ACAD_XDICTIONARY" && tag.Code == 360) result.Metadata.Extension = (string)tag.Value;
                        if (depth == 1 && group == "{ACAD_REACTORS" && tag.Code == 330) result.Metadata.Reactors.Add((string)tag.Value);
                    }
                    if (!closed) throw new FormatException("Unterminated database control group.");
                }
            }
            if (codeName == "DICTIONARY" || codeName == "ACDBDICTIONARYWDFLT")
            {
                DxfDictionary dictionary = codeName == "DICTIONARY" ? new DxfDictionary() : new DxfDictionaryWithDefault();
                result.Object = dictionary;
                dictionary.IsHardOwner = false;
                string pendingName = null;
                for (int i = payload; i < tags.Count; i++)
                {
                    DxfTag tag = tags[i];
                    if (tag.Code == 1001) { this.ReadDatabaseXData(dictionary, tags, i); break; }
                    switch (tag.Code)
                    {
                        case 280: dictionary.IsHardOwner = (short)tag.Value != 0; break;
                        case 281: dictionary.Cloning = (DictionaryCloningFlags)(short)tag.Value; break;
                        case 3:
                            if (pendingName != null) throw new FormatException("Dictionary name has no associated object handle.");
                            pendingName = this.DecodeEncodedNonAsciiCharacters((string)tag.Value); break;
                        case 350: case 360:
                            if (pendingName == null) throw new FormatException("Dictionary handle has no associated name.");
                            result.Entries.Add(Tuple.Create(pendingName, (string)tag.Value, tag.Code == 360)); pendingName = null; break;
                        case 340: result.Default = (string)tag.Value; break;
                    }
                }
                if (pendingName != null) throw new FormatException("Dictionary name has no associated object handle.");
            }
            else if (codeName == "XRECORD")
            {
                DxfXRecord record = new DxfXRecord(); result.Object = record;
                if (payload < tags.Count && tags[payload].Code == 100 && (string)tags[payload].Value == "AcDbXrecord") payload++;
                else throw new FormatException("XRECORD requires AcDbXrecord subclass data.");
                if (payload < tags.Count && tags[payload].Code == 280) record.Cloning = (DictionaryCloningFlags)(short)tags[payload++].Value;
                for (; payload < tags.Count; payload++)
                {
                    DxfTag tag = tags[payload];
                    if (tag.Code == 1001) { this.ReadDatabaseXData(record, tags, payload); break; }
                    if (tag.ValueType == DxfTagValueType.String) tag = new DxfTag(tag.Code, this.DecodeEncodedNonAsciiCharacters((string)tag.Value));
                    record.AddLoadedData(tag);
                }
            }
            else if (codeName == "DICTIONARYVAR")
            {
                DxfDictionaryVariable variable = new DxfDictionaryVariable(); result.Object = variable;
                for (int i = payload; i < tags.Count; i++)
                {
                    if (tags[i].Code == 280) variable.Schema = (short)tags[i].Value;
                    else if (tags[i].Code == 1) variable.Value = this.DecodeEncodedNonAsciiCharacters((string)tags[i].Value);
                    else if (tags[i].Code == 1001) { this.ReadDatabaseXData(variable, tags, i); break; }
                }
            }
            else if (codeName == "ACDBPLACEHOLDER")
            {
                result.Object = new DxfPlaceholder();
                for (int i = payload; i < tags.Count; i++)
                    if (tags[i].Code == 1001) { this.ReadDatabaseXData(result.Object, tags, i); break; }
            }
            else if (!this.ReadStoredEnvelopePayload(result, codeName, tags, payload) && !this.ReadContainerPayload(result, codeName, tags, payload) && !this.ReadGeoDataPayload(result, codeName, tags, payload) && !this.ReadOutputSettingsPayload(result, codeName, tags, payload) && !this.ReadMLeaderStylePayload(result, codeName, tags, payload) && !this.ReadLightListPayload(result, codeName, tags, payload)) result.Object = new DxfOpaqueObject(codeName, tags.Skip(payload).ToList());
            result.Object.Handle = handle;
            this.databaseRecords.Add(result);
            return result;
        }
        private void ReadDatabaseXData(DxfObject target, List<DxfTag> tags, int start)
        {
            XData data = null;
            for (int i = start; i < tags.Count; i++)
            {
                DxfTag tag = tags[i];
                if (tag.Code == 1001)
                {
                    data = new XData(this.GetApplicationRegistry(this.DecodeEncodedNonAsciiCharacters((string)tag.Value)));
                    target.XData.Add(data);
                }
                else
                {
                    if (data == null || tag.Code < 1000 || tag.Code > 1071) throw new FormatException("Invalid object XData.");
                    object value = tag.Value;
                    if (value is string text && tag.Code != 1005) value = this.DecodeEncodedNonAsciiCharacters(text);
                    data.XDataRecord.Add(new XDataRecord((XDataCode)tag.Code, value));
                }
            }
        }
        private DictionaryObject ReadDictionaryDatabaseRecord()
        {
            DatabaseRecord record = this.ReadDatabaseRecord();
            DxfDictionary typed = (DxfDictionary)record.Object;
            DictionaryObject legacy = new DictionaryObject(null) { Handle = typed.Handle, IsHardOwner = typed.IsHardOwner, Cloning = typed.Cloning };
            foreach (Tuple<string, string, bool> entry in record.Entries)
                if (!legacy.Entries.ContainsKey(entry.Item2)) legacy.Entries.Add(entry.Item2, entry.Item1);
            legacy.XData.AddRange(typed.XData.Values);
            return legacy;
        }
        private XRecord ReadXRecordDatabaseRecord()
        {
            DatabaseRecord record = this.ReadDatabaseRecord();
            if (!(record.Object is DxfXRecord typed)) return null; // Private records have no legacy layer-state projection.
            XRecord legacy = new XRecord { Handle = typed.Handle, OwnerHandle = record.Metadata.Owner, Flags = typed.Cloning };
            foreach (DxfTag tag in typed.Data) legacy.Entries.Add(new XRecordEntry(tag.Code, tag.Value));
            return legacy;
        }
        private void ImportDatabaseObjects()
        {
            foreach (DatabaseRecord record in this.databaseRecords) this.RecordSourceObject(record.Object, record.SourceIdentity);
            this.ValidateSourceIdentityDeclarations();
            if (this.databaseRecords.Count == 0) { this.ResolveSunReferences(); this.ResolveOutputSettingsReferences(); return; }
            // Reserve source identities before lazily creating the document's temporary root.
            foreach (DatabaseRecord record in this.databaseRecords)
                if (long.TryParse(record.Object.Handle, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out long sourceHandle) && sourceHandle >= this.doc.NumHandles && sourceHandle < long.MaxValue)
                    this.doc.NumHandles = sourceHandle + 1;
            DxfObjectDatabase database = this.doc.Objects;
            foreach (DatabaseRecord record in this.databaseRecords)
            {
                if (record.Object is DxfXRecord xrecord) foreach (DxfTag tag in xrecord.Data) database.ReserveUnresolvedReference(tag);
                if (record.Object is DxfOpaqueObject opaque) foreach (DxfTag tag in opaque.Tags) database.ReserveUnresolvedReference(tag);
                if (record.Object is DxfStoredTableContent content) foreach (DxfTag tag in content.Payload) database.ReserveUnresolvedReference(tag);
                if (record.Object is DxfStoredSectionManager manager) foreach (DxfTag tag in manager.Tags) database.ReserveUnresolvedReference(tag);
                if (record.Object is DxfTableStyle style) foreach (DxfTag tag in style.Tags) database.ReserveUnresolvedReference(tag);
                if (record.Object is DxfStoredField field) foreach (DxfTag tag in field.Payload) database.ReserveUnresolvedReference(tag);
                foreach (XData data in record.Object.XData.Values)
                    foreach (XDataRecord tag in data.XDataRecord)
                        if (tag.Code == XDataCode.DatabaseHandle) database.ReserveUnresolvedReference(new DxfTag(1005, tag.Value));
            }
            DatabaseRecord root = this.databaseRecords.FirstOrDefault(r => r.Object.Handle == this.namedDictionary?.Handle);
            HashSet<string> managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (this.layerStateManagerDictionaryHandle != null && this.dictionaries.TryGetValue(this.layerStateManagerDictionaryHandle, out DictionaryObject layerManager))
            {
                managed.Add(layerManager.Handle);
                foreach (string child in layerManager.Entries.Keys)
                {
                    managed.Add(child);
                    if (this.dictionaries.TryGetValue(child, out DictionaryObject states))
                        foreach (string stateHandle in states.Entries.Keys) managed.Add(stateHandle);
                }
            }
            if (root != null) database.ReplaceRoot((DxfDictionary)root.Object);
            foreach (DatabaseRecord record in this.databaseRecords)
            {
                if (record == root || managed.Contains(record.Object.Handle)) continue;
                DxfObject existing = this.doc.GetObjectByHandle(record.Object.Handle);
                if (existing != null)
                {
                    bool collection = record.Object is DxfDictionary && root != null
                        && record.Metadata.Owner == root.Object.Handle
                        && this.namedDictionary.Entries.TryGetValue(record.Object.Handle, out string collectionName)
                        && (collectionName == DxfObjectCode.GroupDictionary && existing == this.doc.Groups
                            || collectionName == DxfObjectCode.LayoutDictionary && existing == this.doc.Layouts
                            || collectionName == DxfObjectCode.MLineStyleDictionary && existing == this.doc.MlineStyles
                            || collectionName == DxfObjectCode.ImageDefDictionary && existing == this.doc.ImageDefinitions
                            || collectionName == DxfObjectCode.UnderlayDgnDefinitionDictionary && existing == this.doc.UnderlayDgnDefinitions
                            || collectionName == DxfObjectCode.UnderlayDwfDefinitionDictionary && existing == this.doc.UnderlayDwfDefinitions
                            || collectionName == DxfObjectCode.UnderlayPdfDefinitionDictionary && existing == this.doc.UnderlayPdfDefinitions);
                    if (!collection && !(record.Object is DxfXRecord && existing is LayerState)) throw new FormatException("Duplicate database identity: " + record.Object.Handle);
                    // These collections were constructed from the named dictionary's
                    // source handles and intentionally represent its DICTIONARY records.
                    // LayerState conversions do not preserve source identity.
                    if (collection) this.RecordSourceObject(existing, record.SourceIdentity);
                    managed.Add(record.Object.Handle); continue;
                }
                database.Register(record.Object, true);
            }
            foreach (DatabaseRecord record in this.databaseRecords)
            {
                if (managed.Contains(record.Object.Handle)) continue;
                DxfDatabaseObject item = record.Object;
                if (record != root && record.Metadata.Owner != null && record.Metadata.Owner != "0")
                {
                    item.Owner = this.GetObjectBySourceHandle(record.Metadata.Owner);
                    if (item.Owner == null) throw new FormatException("Unresolved database owner: " + record.Metadata.Owner);
                }
            }
            foreach (DatabaseRecord record in this.databaseRecords)
            {
                if (managed.Contains(record.Object.Handle)) continue;
                DxfDatabaseObject item = record.Object;
                this.ResolveContainerReferences(record);
                if (item is DxfDictionary dictionary)
                    foreach (Tuple<string, string, bool> entry in record.Entries)
                    {
                        if (record == root && DxfObjectDatabase.IsReservedName(entry.Item1)) continue;
                        DxfObject target = this.GetObjectBySourceHandle(entry.Item2);
                        if (target == null) throw new FormatException("Unresolved dictionary entry: " + entry.Item1 + " -> " + entry.Item2);
                        dictionary.AddLoaded(entry.Item1, target, entry.Item3);
                    }
                if (item is DxfDictionaryWithDefault fallback && record.Default != null && record.Default != "0")
                {
                    fallback.Default = this.GetObjectBySourceHandle(record.Default);
                    if (fallback.Default == null) throw new FormatException("Unresolved dictionary default: " + record.Default);
                }
                this.ApplyDatabaseMetadata(item, record.Metadata);
            }
            foreach (KeyValuePair<string, DatabaseMetadata> pair in this.entityDatabaseMetadata)
            {
                DxfObject target = this.GetObjectBySourceHandle(pair.Key);
                if (target != null) this.ApplyDatabaseMetadata(target, pair.Value);
            }
            this.ResolveStoredDimAssocReferences();
            this.ResolveTableStyleReferences();
            this.ResolveStoredTableContentReferences();
            this.ResolveSunReferences();
            this.ResolveStoredFields();
            this.ResolveDataTableReferences();
            this.ResolveLayerIndexReferences();
            this.ResolveSectionSettingsReferences();
            this.ResolveSectionManagerReferences();
            this.ResolveDeclaredOwnership();
            this.ResolveGeoDataHosts();
            this.ResolveOutputSettingsReferences();
            this.ResolveLightListReferences();
        }
        private void ApplyDatabaseMetadata(DxfObject item, DatabaseMetadata metadata)
        {
            if (!string.IsNullOrEmpty(metadata.Extension) && metadata.Extension != "0")
            {
                item.ExtensionDictionary = this.GetObjectBySourceHandle(metadata.Extension) as DxfDictionary;
                if (item.ExtensionDictionary == null)
                {
                    // Existing layer-state collections have their own extension-dictionary writer.
                    if (!ReferenceEquals(item, this.doc.Layers) || metadata.Extension != this.layerStateManagerDictionaryHandle || !this.IsAcceptedSourceDictionary(metadata.Extension))
                        throw new FormatException("Unresolved extension dictionary: " + metadata.Extension);
                }
                else if (item.ExtensionDictionary.Owner != item) throw new FormatException("Extension dictionary owner mismatch: " + metadata.Extension);
            }
            foreach (string handle in metadata.Reactors)
            {
                DxfObject target = this.GetObjectBySourceHandle(handle);
                if (target != null && !item.PersistentReactors.Contains(target)) item.PersistentReactors.Add(target);
                else if (target == null && handle != "0" && !this.managedReactorHandles.Contains(handle))
                    throw new FormatException("Unresolved persistent reactor: " + handle);
            }
        }
    }
}
