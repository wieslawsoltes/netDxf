// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace netDxf.Objects
{
    public sealed partial class DxfObjectDatabase
    {
        /// <summary>Attaches detached typed settings as the registered section's owned group-360 object.</summary>
        /// <remarks>An existing settings slot is never replaced. Invalid ownership or references reject before registration.</remarks>
        public void SetSectionSettings(Section section, DxfSectionSettings settings)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            this.CheckRegistered(section); section.Validate(this.Document);
            if (section.GeometrySettings != null) throw new InvalidOperationException("The section already owns geometry settings.");
            if (settings.Database != null || settings.Owner != null) throw new ArgumentException("Section settings must be detached and unowned.", nameof(settings));
            settings.ValidateValues();
            this.PrepareTarget(settings);
            settings.Owner = section; section.GeometrySettings = settings; section.HasSettingsField = true;
        }

        /// <summary>Copies a section and every typed object it owns into a registered destination block.</summary>
        /// <remarks>Includes settings, extension dictionaries, aliases, reactors, XData and handle remapping. Cross-document external references require explicit mappings, including the entity's layer and linetype. Private opaque owned objects reject before mutation.</remarks>
        public Section CloneSection(Section source, Block destination, IReadOnlyDictionary<DxfObject, DxfObject> externalReferences = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            // Snapshot caller code before reading the source or destination graph.
            var external = new Dictionary<DxfObject, DxfObject>(ObjectIdentity);
            if (externalReferences != null) foreach (var pair in externalReferences) external.Add(pair.Key, pair.Value);
            this.CheckRegistered(destination.Record);
            if (destination.Flags.HasFlag(BlockTypeFlags.ExternallyDependent)) throw new ArgumentException("An externally dependent block cannot receive a section.", nameof(destination));
            if (this.Document.DrawingVariables.AcadVer < DxfVersion.AutoCad2007) throw new NotSupportedException("SECTION requires R2007 or later.");
            DxfObjectDatabase sourceDatabase = source.GeometrySettings?.Database ?? source.Owner?.Record.Owner?.Owner.Objects;
            if (sourceDatabase == null) throw new ArgumentException("The source section must be registered.", nameof(source));
            sourceDatabase.CheckRegistered(source); source.Validate(sourceDatabase.Document);
            var sourceErrors = sourceDatabase.Validate();
            if (sourceErrors.Count != 0) throw new InvalidOperationException("Cannot clone an invalid section database: " + string.Join("; ", sourceErrors));
            List<DxfDatabaseObject> originals = sourceDatabase.objects.Values.Where(o => IsAncestor(source, o)).ToList();
            // Resource identities are mapped below; never execute public Clone overrides while planning.
            var copy = source.CloneValues(false);
            var map = new Dictionary<DxfObject, DxfObject>(ObjectIdentity) { { source, copy } };
            foreach (DxfDatabaseObject original in originals) map.Add(original, original.CloneShell());
            if (source.Owner != null)
            {
                map[source.Owner] = destination;
                map[source.Owner.Record] = destination.Record;
            }
            foreach (var pair in external)
                if (map.TryGetValue(pair.Key, out DxfObject automatic) && !ReferenceEquals(automatic, pair.Value))
                    throw new ArgumentException("An explicit reference mapping conflicts with the cloned ownership graph.", nameof(externalReferences));
            Func<DxfObject, DxfObject> resolve = value =>
            {
                if (value == null) return null;
                if (map.TryGetValue(value, out DxfObject owned)) return owned;
                if (external.TryGetValue(value, out DxfObject replacement)) { this.CheckRegistered(replacement); return replacement; }
                if (sourceDatabase == this) { this.CheckRegistered(value); return value; }
                throw new InvalidOperationException("An external section reference needs an explicit destination mapping: " + value.Handle);
            };
            copy.Layer = resolve(source.Layer) as Layer ?? throw new ArgumentException("The section layer mapping must target a layer.");
            copy.Linetype = resolve(source.Linetype) as Linetype ?? throw new ArgumentException("The section linetype mapping must target a linetype.");
            copy.GeometrySettings = (DxfDatabaseObject)resolve(source.GeometrySettings);
            var carriers = new List<DxfObject> { source }; carriers.AddRange(originals);
            foreach (DxfObject original in carriers)
            {
                DxfObject clone = map[original];
                if (original is DxfDatabaseObject databaseObject)
                {
                    clone.Owner = resolve(original.Owner);
                    databaseObject.CopyDatabaseReferencesTo((DxfDatabaseObject)clone, resolve);
                    if (original is DxfDictionary dictionary)
                        foreach (DxfDictionaryEntry entry in dictionary.Entries) ((DxfDictionary)clone).AddLoaded(entry.Name, resolve(entry.Target), entry.IsHardOwner);
                    if (original is DxfDictionaryWithDefault fallback) ((DxfDictionaryWithDefault)clone).Default = resolve(fallback.Default);
                    foreach (XData data in original.XData.Values) clone.XData.Add(data.CopyStoredGraph());
                }
                clone.ExtensionDictionary = (DxfDictionary)resolve(original.ExtensionDictionary);
                foreach (DxfObject reactor in original.PersistentReactors) clone.PersistentReactors.Add(resolve(reactor));
                if (original is EntityObject entity) foreach (DxfObject reactor in entity.Reactors) ((EntityObject)clone).AddReactor(resolve(reactor));
                foreach (XData data in original.XData.Values)
                    foreach (XDataRecord tag in data.XDataRecord)
                        if (tag.Code == XDataCode.DatabaseHandle && !IsNullHandle((string)tag.Value)) resolve(SectionHandleTarget(sourceDatabase, (string)tag.Value));
                if (original is DxfXRecord record)
                    foreach (DxfTag tag in record.Data)
                        if (IsReference(tag) && !IsNullHandle((string)tag.Value)) resolve(SectionHandleTarget(sourceDatabase, (string)tag.Value));
            }
            List<string> errors = new List<string>();
            var clonedObjects = originals.Select(o => (DxfDatabaseObject)map[o]).ToList();
            foreach (DxfDatabaseObject clone in clonedObjects)
            {
                clone.ValidateDatabaseSchema(this, errors);
                this.ValidateDeclaredOwnership(clone, clonedObjects, errors, false);
            }
            if (errors.Count != 0) throw new InvalidOperationException("Invalid cloned section graph: " + string.Join("; ", errors));

            // Calculate all identities and fix raw references on detached copies before touching the document.
            long candidate = this.Document.NumHandles;
            var registries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DxfObject original in carriers)
            {
                DxfObject clone = map[original];
                if (clone is DxfDatabaseObject databaseObject)
                    foreach (DxfTag tag in databaseObject.AllocationReservations) candidate = this.GetReservedSeed(tag, candidate);
                if (clone is DxfXRecord record) foreach (DxfTag tag in record.Data) candidate = this.GetReservedSeed(tag, candidate);
                foreach (XData data in clone.XData.Values)
                {
                    if (!this.Document.ApplicationRegistries.Contains(data.ApplicationRegistry.Name)) registries.Add(data.ApplicationRegistry.Name);
                    foreach (XDataRecord tag in data.XDataRecord)
                        if (tag.Code == XDataCode.DatabaseHandle) candidate = this.GetReservedSeed(new DxfTag(1005, tag.Value), candidate);
                }
            }
            if (candidate <= 0 || candidate > long.MaxValue - carriers.Count - registries.Count - 1) throw new InvalidOperationException("The section graph exceeds the available handle range.");
            foreach (DxfObject original in carriers) map[original].Handle = (candidate++).ToString("X", System.Globalization.CultureInfo.InvariantCulture);
            foreach (DxfObject original in carriers)
            {
                DxfObject clone = map[original];
                foreach (XData data in original.XData.Values)
                    for (int i = 0; i < data.XDataRecord.Count; i++)
                    {
                        XDataRecord tag = data.XDataRecord[i];
                        if (tag.Code == XDataCode.DatabaseHandle && !IsNullHandle((string)tag.Value))
                            clone.XData[data.ApplicationRegistry.Name].XDataRecord[i] = new XDataRecord(XDataCode.DatabaseHandle, resolve(SectionHandleTarget(sourceDatabase, (string)tag.Value)).Handle);
                    }
                if (original is DxfXRecord record)
                    for (int i = 0; i < record.Data.Count; i++)
                    {
                        DxfTag tag = record.Data[i];
                        if (IsReference(tag) && !IsNullHandle((string)tag.Value)) ((DxfXRecord)clone).ReplaceLoadedData(i, new DxfTag(tag.Code, resolve(SectionHandleTarget(sourceDatabase, (string)tag.Value)).Handle));
                    }
            }
            // No caller enumerators, callbacks or graph discovery occur during this commit.
            this.Document.NumHandles = candidate;
            copy.PendingInputReferences = true;
            this.Document.AddEntityToDocument(copy, false);
            foreach (DxfDatabaseObject clone in clonedObjects) this.Register(clone, true);
            foreach (DxfDatabaseObject clone in clonedObjects) clone.MaterializeOwnedObjectReferences();
            destination.AddPreparedSection(copy);
            copy.PendingInputReferences = false;
            return copy;
        }
        private static DxfObject SectionHandleTarget(DxfObjectDatabase database, string handle)
        { return database.Document.GetObjectByHandle(handle) ?? throw new InvalidOperationException("Cannot clone an unresolved section metadata handle: " + handle); }

        /// <summary>Permanently erases a section and its complete typed owned object subtree.</summary>
        /// <remarks>Incoming references and private opaque owned objects reject before mutation. Ordinary entity removal refuses sections that still own database objects.</remarks>
        public void EraseSection(Section section)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            this.CheckRegistered(section); section.Validate(this.Document);
            var carriers = this.ErasureCarriers();
            var deleted = new HashSet<DxfObject>(ObjectIdentity) { section };
            var tree = this.objects.Values.Where(o => IsAncestor(section, o)).ToList();
            foreach (DxfDatabaseObject item in tree)
            {
                if (item is DxfStoredTableContent) throw new NotSupportedException("Stored TABLECONTENT erasure requires its complete application schema.");
                if (item is DxfStoredField) throw new NotSupportedException("Stored FIELD erasure requires its complete evaluator graph schema.");
                if (item is DxfStoredDimAssoc) throw new NotSupportedException("Stored DIMASSOC erasure requires the complete dimension association lifecycle.");
                if (item is DxfStoredSectionManager) throw new NotSupportedException("Stored section-manager erasure requires the complete manager lifecycle.");
                if (item is DxfOpaqueObject) throw new NotSupportedException("An opaque section-owned object requires its application schema before erasure.");
                this.CheckRegistered(item); deleted.Add(item);
            }
            foreach (DxfObject item in carriers)
                if (IsAncestor(section, item) && !deleted.Contains(item)) throw new NotSupportedException("The section owns an unsupported managed object.");
            var handles = new HashSet<ulong>(deleted.Select(o => ErasureHandle(o.Handle)));
            foreach (DxfObject item in carriers)
            {
                if (deleted.Contains(item)) continue;
                Action<DxfObject, string> reference = (target, field) => { if (target != null && deleted.Contains(target)) throw ErasureReference(item, field, target.Handle); };
                Action<string, string> handle = (target, field) => { if (handles.Contains(ErasureHandle(target))) throw ErasureReference(item, field, target); };
                reference(item.Owner, "owner"); reference(item.ExtensionDictionary, "extension dictionary");
                foreach (DxfObject reactor in item.PersistentReactors) reference(reactor, "persistent reactor");
                if (item is EntityObject entity) foreach (DxfObject reactor in entity.Reactors) reference(reactor, "entity reactor");
                foreach (XData data in item.XData.Values) foreach (XDataRecord tag in data.XDataRecord)
                    if (tag.Code == XDataCode.DatabaseHandle) handle((string)tag.Value, "XData 1005");
                if (item is DxfDatabaseObject databaseObject) foreach (DxfObject target in databaseObject.DatabaseReferences) reference(target, "typed reference");
                if (item is DxfDictionary dictionary) foreach (DxfDictionaryEntry entry in dictionary.Entries) reference(entry.Target, "dictionary entry");
                if (item is DxfDictionaryWithDefault fallback) reference(fallback.Default, "dictionary default");
                if (item is DxfXRecord record) foreach (DxfTag tag in record.Data) if (IsReference(tag)) handle((string)tag.Value, "XRECORD reference");
                if (item is DxfOpaqueObject opaque) foreach (DxfTag tag in opaque.Tags) if (tag.ValueType == DxfTagValueType.Handle) handle((string)tag.Value, "opaque handle");
                if (item is Section other) reference(other.GeometrySettings, "section settings");
                if (item is Polyline3DRecord polylineRecord)
                {
                    foreach (DxfObject target in polylineRecord.References) reference(target, "polyline record reference");
                    foreach (DxfTag tag in polylineRecord.OpaqueHandleTags) handle((string)tag.Value, "polyline record handle");
                }
                if (item is PolygonMeshRecord meshRecord)
                {
                    foreach (DxfObject target in meshRecord.References) reference(target, "polygon mesh record reference");
                    foreach (DxfTag tag in meshRecord.OpaqueHandleTags) handle((string)tag.Value, "polygon mesh record handle");
                }
                if (item is PolyfaceMeshRecord polyfaceRecord)
                {
                    foreach (DxfObject target in polyfaceRecord.References) reference(target, "polyface record reference");
                    foreach (DxfTag tag in polyfaceRecord.OpaqueHandleTags) handle((string)tag.Value, "polyface record handle");
                }
                if (item is PolyfaceMesh polyface)
                    foreach (DxfTag tag in polyface.StoredHeaderReferences) handle((string)tag.Value, "polyface header handle");
                if (item is Polyline2DRecord legacyRecord)
                {
                    foreach (DxfObject target in legacyRecord.References) reference(target, "legacy 2D record reference");
                    foreach (DxfTag tag in legacyRecord.OpaqueHandleTags) handle((string)tag.Value, "legacy 2D record handle");
                }
                if (item is Polyline2D legacy)
                    foreach (DxfTag tag in legacy.StoredHeaderReferences) handle((string)tag.Value, "legacy 2D header handle");
                if (item is StoredTable table) foreach (DxfObject target in table.References) reference(target, "ACAD_TABLE reference");
                if (item is MultiLeader leader) foreach (MLeaderData data in leader.Data) foreach (DxfObject target in data.References) reference(target, "MULTILEADER reference");
                if (item is Layout layout) reference(layout.PlotSettings?.ShadePlotObject, "layout shade plot");
            }
            foreach (HeaderVariable variable in this.Document.DrawingVariables.CustomValues())
            {
                DxfHandleKind kind = DxfGroupCode.GetHandleKind(variable.GroupCode);
                if (kind != DxfHandleKind.None && kind != DxfHandleKind.Arbitrary && variable.Name != "$HANDSEED" && variable.Value is string handle && handles.Contains(ErasureHandle(handle)))
                    throw ErasureReference(this.Document, "header " + variable.Name, handle);
            }
            foreach (DxfObject item in deleted) foreach (XData data in item.XData.Values)
                if (!this.Document.ApplicationRegistries.References.ContainsKey(data.ApplicationRegistry.Name)) throw new InvalidOperationException("Section erasure found invalid APPID bookkeeping.");
            Block owner = section.Owner; string originalHandle = section.Handle;
            if (owner == null || !owner.Entities.Contains(section)) throw new InvalidOperationException("The section has inconsistent block membership.");
            foreach (DxfDatabaseObject item in tree)
            { this.Document.AddedObjects.Remove(item.Handle); this.objects.Remove(item.Handle); item.Database = null; item.IsErased = true; }
            owner.RemovePreparedSection(section);
            this.Document.RemoveEntityFromDocument(section);
            section.Handle = originalHandle; section.IsErased = true;
        }
    }
}
