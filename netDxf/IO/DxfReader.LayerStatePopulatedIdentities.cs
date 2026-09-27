// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        // Retention budgets, not a change to the existing layer-state conversion limits.
        private const int MaximumRetainedLayerStates = 4096;
        private const int MaximumRetainedLayerStateTags = 1048576;

        private void RestorePopulatedLayerStateIdentities(DatabaseRecord parent, DatabaseRecord child)
        {
            // Preserve only a fully checked writer-compatible projection. Unknown formats,
            // custom fields and lossy legacy conversions must not masquerade as retained state.
            var manager = this.doc.Layers.StateManager;
            if (parent.Object.CodeName != DxfObjectCode.Dictionary || child.Object.CodeName != DxfObjectCode.Dictionary
                || child.Entries.Count > MaximumRetainedLayerStates || manager.Count != child.Entries.Count
                || !EmptyLayerDictionaryShape(parent.DictionaryTags, false)
                || !PopulatedLayerDictionaryShape(child)) return;
            if (!ReferenceEquals(this.GetObjectBySourceHandle(this.doc.Layers.Handle, true), this.doc.Layers)) return;
            if (!ReferenceEquals(this.GetObjectBySourceHandle(parent.Metadata.Owner, true), this.doc.Layers)
                || !SameLayerDictionaryHandle(child.Metadata.Owner, parent.Object.Handle))
                throw new FormatException("Populated layer-state dictionaries have inconsistent structural ownership.");

            // One pass over OBJECTS, retaining at most the requested state count in the
            // temporary index rather than duplicating the entire drawing's object index.
            var wanted = new HashSet<string>(child.Entries.Select(e => e.Item2), StringComparer.OrdinalIgnoreCase);
            var byHandle = new Dictionary<string, DatabaseRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in this.databaseRecords)
                if (record.Object.Handle != null && wanted.Contains(record.Object.Handle)
                    && !byHandle.ContainsKey(record.Object.Handle))
                    byHandle.Add(record.Object.Handle, record);
            var states = new List<KeyValuePair<LayerState, string>>();
            var sources = new List<DatabaseRecord>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int remaining = MaximumRetainedLayerStateTags;
            foreach (var entry in child.Entries)
            {
                if (!names.Add(entry.Item1) || !handles.Add(entry.Item2))
                    throw new FormatException("Layer-state names and identities must be unique.");
                if (!byHandle.TryGetValue(entry.Item2, out DatabaseRecord record)
                    || !(record.Object is DxfXRecord xrecord)) return;
                if (!SameLayerDictionaryHandle(record.Metadata.Owner, child.Object.Handle))
                    throw new FormatException("Layer-state XRECORD owner does not match its dictionary.");
                LayerState state = manager[entry.Item1];
                if (state == null || state.Name != entry.Item1 || record.SourceIdentity == null
                    || record.SourceIdentity.Ambiguous || record.SourceIdentity.Handle == 0) return;
                remaining -= xrecord.Data.Count;
                if (remaining < 0 || !MatchesLayerStateProjection(record, xrecord, state, child.Object.Handle)) return;
                states.Add(new KeyValuePair<LayerState, string>(state, record.Object.Handle));
                sources.Add(record);
            }
            // Validate every registration before rebinding any of the generated state handles.
            manager.ValidateLoadedStateIdentities(states, parent.Object.Handle, child.Object.Handle);
            manager.RestoreEmptyDictionaryIdentities(parent.Object.Handle, child.Object.Handle);
            manager.RestoreLoadedStateIdentities(states);
            this.RecordSourceObject(manager, parent.SourceIdentity);
            this.RecordSourceObject(manager.StatesDictionaryObject, child.SourceIdentity);
            for (int i = 0; i < states.Count; i++) this.RecordSourceObject(states[i].Key, sources[i].SourceIdentity);
        }

        private static bool PopulatedLayerDictionaryShape(DatabaseRecord record)
        {
            var tags = record.DictionaryTags;
            if (tags == null || tags.Count != 5 + 2 * record.Entries.Count) return false;
            short[] common = { 5, 330, 100, 280, 281 };
            for (int i = 0; i < common.Length; i++) if (tags[i].Code != common[i]) return false;
            if (!Equals(tags[2].Value, "AcDbDictionary") || !Equals(tags[3].Value, (short)1)
                || !Equals(tags[4].Value, (short)1)) return false;
            for (int i = 5; i < tags.Count; i += 2)
                if (tags[i].Code != 3 || tags[i + 1].Code != 350 || record.Entries[(i - 5) / 2].Item3) return false;
            return true;
        }

        private static bool IsCanonicalLayerStateHeader(List<DxfTag> tags, int payload)
        {
            // XRECORD common header emitted by WriteLayerState, including its automatic
            // owner reactor. Do not retain a projection that discarded private header data.
            return payload == 5 && tags.Count > 6 && tags[0].Code == 5
                && tags[1].Code == 102 && Equals(tags[1].Value, "{ACAD_REACTORS")
                && tags[2].Code == 330 && tags[3].Code == 102 && Equals(tags[3].Value, "}")
                && tags[4].Code == 330 && tags[5].Code == 100 && Equals(tags[5].Value, "AcDbXrecord")
                && tags[6].Code == 280 && Equals(tags[6].Value, (short)1);
        }

        private bool MatchesLayerStateProjection(DatabaseRecord record, DxfXRecord payload, LayerState state, string owner)
        {
            if (!record.CanonicalLayerStateHeader || payload.Cloning != DictionaryCloningFlags.KeepExisting || payload.XData.Count != 0
                || !string.IsNullOrEmpty(record.Metadata.Extension) || record.Metadata.Reactors.Count != 1
                || !SameLayerDictionaryHandle(record.Metadata.Reactors[0], owner)) return false;
            int at = 0;
            Func<short, object, bool> take = (code, value) => at < payload.Data.Count
                && payload.Data[at].Code == code && Equals(payload.Data[at++].Value, value);
            if (!take(91, 2047) || !take(301, state.Description) || !take(290, state.PaperSpace)
                || !take(302, state.CurrentLayer)) return false;
            foreach (var property in state.Properties.Values)
            {
                var layer = this.doc.Layers[property.Name];
                var linetype = this.doc.Linetypes[property.LinetypeName];
                if (layer == null || linetype == null) return false;
                int alpha = property.Transparency.StoredAlphaValue
                    ?? (property.Transparency.Value == 0 ? 0 : Transparency.ToAlphaValue(property.Transparency));
                if (!take(330, layer.Handle) || !take(90, (int)property.Flags) || !take(62, property.Color.Index)
                    || !take(370, (short)property.Lineweight) || !take(331, linetype.Handle) || !take(440, alpha)) return false;
                if (property.Color.UseTrueColor && !take(92, AciColor.ToTrueColor(property.Color))) return false;
            }
            return at == payload.Data.Count;
        }
    }
}
