// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private void RestoreLayerStateIdentities(DictionaryObject projection)
        {
            // Only complete writer-shaped graphs are promoted from the legacy conversion.
            // Unknown payloads, alternate dictionary flags and lossy conversions stay on
            // their existing path. They must not acquire an accepted source identity.
            var parent = this.databaseRecords.SingleOrDefault(r => r.Object.Handle == projection.Handle);
            if (parent == null || parent.Entries.Count != 1) return;
            var entry = parent.Entries[0];
            if (entry.Item1 != DxfObjectCode.LayerStates || !entry.Item3) return;
            var child = this.databaseRecords.SingleOrDefault(r => r.Object.Handle == entry.Item2);
            if (child == null || !child.CanonicalLayerStateDictionary
                || !EmptyLayerDictionaryShape(parent.DictionaryTags, false)) return;
            // Preserve the old late diagnostic for absent/payload-only table identities.
            if (!ReferenceEquals(this.GetObjectBySourceHandle(this.doc.Layers.Handle, true), this.doc.Layers)) return;
            if (!ReferenceEquals(this.GetObjectBySourceHandle(parent.Metadata.Owner, true), this.doc.Layers)
                || !SameLayerDictionaryHandle(child.Metadata.Owner, parent.Object.Handle))
                throw new FormatException("Canonical layer-state dictionaries have inconsistent structural ownership.");

            var manager = this.doc.Layers.StateManager;
            if (child.Entries.Count != manager.Count) return;
            var wanted = new HashSet<string>(child.Entries.Select(e => e.Item2), StringComparer.OrdinalIgnoreCase);
            if (wanted.Count != child.Entries.Count) return; // A converted alias is not a new state.
            var records = new Dictionary<string, DatabaseRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in this.databaseRecords)
                if (wanted.Contains(record.Object.Handle)) records.Add(record.Object.Handle, record);
            // The exact entry name, complete collection cardinality and full packet
            // comparison identify the converted state without a second source map.
            // Duplicate/case-collapsed names cannot pass this one-to-one validation.
            var replacements = new List<KeyValuePair<LayerState, string>>(wanted.Count);
            var accepted = new List<DatabaseRecord>(wanted.Count);
            foreach (var item in child.Entries)
            {
                // WriteDictionary uses 360 for this reserved name and 350 otherwise.
                bool hard = string.Equals(item.Item1, DxfObjectCode.LayerStates, StringComparison.InvariantCultureIgnoreCase);
                if (item.Item3 != hard || !records.TryGetValue(item.Item2, out var record)
                    || !record.CanonicalLayerStateRecord
                    || !manager.TryGetValue(item.Item1, out var state)
                    || state.Name != item.Item1 || !ReferenceEquals(manager[state.Name], state)
                    || !this.MatchesLayerStateProjection(record, state)) return;
                if (!SameLayerDictionaryHandle(record.Metadata.Owner, child.Object.Handle)
                    || record.Metadata.Reactors.Count != 1
                    || !SameLayerDictionaryHandle(record.Metadata.Reactors[0], child.Object.Handle))
                    throw new FormatException("Canonical layer-state record has inconsistent owner or reactor.");
                replacements.Add(new KeyValuePair<LayerState, string>(state, item.Item2));
                accepted.Add(record);
            }
            // Hydration-only: preflight every state before rebinding either dictionary.
            manager.ValidateLayerStateSourceIdentities(replacements);
            manager.RestoreDictionaryIdentities(parent.Object.Handle, child.Object.Handle);
            manager.RestoreLayerStateSourceIdentities(replacements);
            this.RecordSourceObject(manager, parent.SourceIdentity);
            this.RecordSourceObject(manager.StatesDictionaryObject, child.SourceIdentity);
            for (int i = 0; i < replacements.Count; i++)
                this.RecordSourceObject(replacements[i].Key, accepted[i].SourceIdentity);
        }

        private bool MatchesLayerStateProjection(DatabaseRecord record, LayerState state)
        {
            if (!(record.Object is DxfXRecord source) || source.Cloning != DictionaryCloningFlags.KeepExisting
                || source.XData.Count != 0 || record.Metadata.Extension != null) return false;
            // Compare the entire decoded packet, including all per-layer values, rather
            // than trusting that a permissive legacy parser consumed every input field.
            var data = source.Data;
            int at = 0;
            Func<short, object, bool> take = (code, value) => at < data.Count
                && data[at].Code == code && Equals(data[at++].Value, value);
            if (!take(91, 2047) || !take(301, state.Description) || !take(290, state.PaperSpace)
                || !take(302, state.CurrentLayer)) return false;
            foreach (var properties in state.Properties.Values)
            {
                if (!this.doc.Layers.TryGetValue(properties.Name, out var layer)
                    || !this.doc.Linetypes.TryGetValue(properties.LinetypeName, out var linetype)
                    || !ReferenceEquals(this.GetObjectBySourceHandle(layer.Handle, true), layer)
                    || !ReferenceEquals(this.GetObjectBySourceHandle(linetype.Handle, true), linetype)) return false;
                if (!take(330, layer.Handle) || !take(90, (int)properties.Flags)
                    || !take(62, properties.Color.Index) || !take(370, (short)properties.Lineweight)
                    || !take(331, linetype.Handle)
                    || !take(440, properties.Transparency.StoredAlphaValue ?? (properties.Transparency.Value == 0
                        ? 0 : Transparency.ToAlphaValue(properties.Transparency)))) return false;
                if (properties.Color.UseTrueColor && !take(92, AciColor.ToTrueColor(properties.Color))) return false;
            }
            return at == data.Count;
        }

        private static bool CanonicalLayerStateDictionary(List<DxfTag> tags)
        {
            if (tags.Count < 5 || (tags.Count - 5) % 2 != 0) return false;
            short[] header = { 5, 330, 100, 280, 281 };
            for (int i = 0; i < header.Length; i++) if (tags[i].Code != header[i]) return false;
            if (!Equals(tags[2].Value, "AcDbDictionary") || !Equals(tags[3].Value, (short)1)
                || !Equals(tags[4].Value, (short)1)) return false;
            for (int i = 5; i < tags.Count; i += 2)
                if (tags[i].Code != 3 || (tags[i + 1].Code != 350 && tags[i + 1].Code != 360)) return false;
            return true;
        }

        private static bool CanonicalLayerStateRecord(List<DxfTag> tags)
        {
            // Exact common header generated by WriteLayerState. Other metadata and
            // unknown application groups must not be silently treated as retained.
            if (tags.Count < 11) return false;
            short[] header = { 5, 102, 330, 102, 330, 100, 280 };
            for (int i = 0; i < header.Length; i++) if (tags[i].Code != header[i]) return false;
            return Equals(tags[1].Value, "{ACAD_REACTORS") && Equals(tags[3].Value, "}")
                && Equals(tags[5].Value, "AcDbXrecord") && Equals(tags[6].Value, (short)1);
        }

        private static bool EmptyLayerDictionaryShape(List<DxfTag> tags, bool child)
        {
            if (tags == null || tags.Count != (child ? 5 : 7)) return false;
            short[] codes = child ? new short[] { 5, 330, 100, 280, 281 }
                : new short[] { 5, 330, 100, 280, 281, 3, 360 };
            for (int i = 0; i < codes.Length; i++) if (tags[i].Code != codes[i]) return false;
            return Equals(tags[2].Value, "AcDbDictionary") && Equals(tags[3].Value, (short)1)
                && Equals(tags[4].Value, (short)1)
                && (child || Equals(tags[5].Value, DxfObjectCode.LayerStates));
        }

        private static bool SameLayerDictionaryHandle(string a, string b)
        {
            return ulong.TryParse(a, System.Globalization.NumberStyles.AllowHexSpecifier,
                    System.Globalization.CultureInfo.InvariantCulture, out ulong first)
                && ulong.TryParse(b, System.Globalization.NumberStyles.AllowHexSpecifier,
                    System.Globalization.CultureInfo.InvariantCulture, out ulong second)
                && first != 0 && first == second;
        }
    }
}
