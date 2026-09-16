// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using netDxf.Header;
using netDxf.IO;

namespace netDxf.Objects
{
    public sealed partial class DxfStoredCellStyleMap
    {
        private bool editing;
        private bool reentered;

        /// <summary>Atomically replaces the decoded names of the existing entries in their stored order.</summary>
        /// <param name="names">Exactly one non-null name for each current entry. Empty and duplicate names are allowed.</param>
        /// <remarks>
        /// Identifiers, types, counts, formatting, dependencies and every other stored tag remain unchanged.
        /// This operation does not assign cell roles, regenerate tables or synchronize application-defined
        /// name references. Enumeration and disposal finish before the source graph is validated. Reentry,
        /// including a caught nested request, rejects the outer operation. Earlier Payload and Entries
        /// snapshots remain unchanged; an equivalent request preserves the current snapshots. Names must
        /// be valid UTF-16 without NUL and fit the 1,048,576-code-unit limit after DXF escaping.
        /// CR/LF are retained for binary output; text output rejects them under the ordinary save contract.
        /// Changes to other document objects made by caller callbacks are not rolled back.
        /// </remarks>
        public void ReplaceEntryNames(IEnumerable<string> names)
        {
            if (this.editing)
            { this.reentered = true; throw new InvalidOperationException("CELLSTYLEMAP name replacement cannot be reentered."); }
            if (names == null) throw new ArgumentNullException(nameof(names));
            this.editing = true; this.reentered = false;
            try
            {
                var replacements = new List<string>();
                foreach (string name in names)
                {
                    if (replacements.Count >= this.Entries.Count)
                        throw new ArgumentException("The name count must equal the current entry count.", nameof(names));
                    DxfStoredTableContent.CheckEditableText(name, nameof(names));
                    replacements.Add(name);
                }
                if (this.reentered) throw new InvalidOperationException("CELLSTYLEMAP name replacement was reentered during enumeration.");
                if (replacements.Count != this.Entries.Count)
                    throw new ArgumentException("The name count must equal the current entry count.", nameof(names));
                this.ValidateNameReplacementSource();
                if (this.Entries.Select(entry => entry.Name).SequenceEqual(replacements)) return;

                var tags = this.Payload.ToList();
                var entries = new List<DxfStoredCellStyleMapEntry>(this.Entries.Count);
                for (int i = 0; i < this.Entries.Count; i++)
                {
                    DxfStoredCellStyleMapEntry original = this.Entries[i];
                    string name = replacements[i];
                    if (name != original.Name) tags[original.NameIndex] = new DxfTag(300, this.EncodeEntryName(name));
                    entries.Add(new DxfStoredCellStyleMapEntry(original.Id, original.StoredType, name,
                        original.FormatPayload.ToList(), original.NameIndex));
                }
                var nextPayload = tags.AsReadOnly();
                var nextEntries = entries.AsReadOnly();
                // All caller code, validation and allocations have completed before the state swap.
                this.Payload = nextPayload;
                this.Entries = nextEntries;
            }
            finally { this.editing = false; this.reentered = false; }
        }

        private void ValidateNameReplacementSource()
        {
            if (!this.resolved || this.IsErased || this.Database == null
                || !ReferenceEquals(this.Database.Document, this.source)
                || !ReferenceEquals(this.source.GetObjectByHandle(this.Handle), this))
                throw new InvalidOperationException("CELLSTYLEMAP must remain registered in its source document.");
            IReadOnlyList<string> errors = this.Database.Validate();
            if (errors.Count != 0)
                throw new InvalidOperationException("Cannot edit CELLSTYLEMAP in an invalid source database: " + string.Join("; ", errors));
            long count = this.Payload.Count + 2L;
            if (this.ExtensionDictionary != null) count += 3;
            int reactors = this.PersistentReactors.Where(item => item != null).Select(item => item.Handle)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (reactors != 0) count += reactors + 2L;
            foreach (XData data in this.XData.Values)
            {
                count++;
                foreach (XDataRecord record in data.XDataRecord)
                    count += record.Code == XDataCode.BinaryData ? Math.Max(1L, (((byte[])record.Value).LongLength + 126L) / 127L) : 1L;
            }
            if (count > MaximumPayloadTags)
                throw new InvalidOperationException("CELLSTYLEMAP and its common metadata exceed the stored record tag limit.");
        }

        private string EncodeEntryName(string name)
        {
            var result = new StringBuilder();
            foreach (char value in name)
            {
                bool escape = value == '\\' || this.SourceVersion < DxfVersion.AutoCad2007 && value > 127;
                if (result.Length > DxfStoredTableContent.MaximumEditedStringLength - (escape ? 7 : 1))
                    throw new ArgumentOutOfRangeException(nameof(name), "The encoded entry name exceeds the stored string limit.");
                if (escape) result.Append("\\U+").Append(((int)value).ToString("X4", CultureInfo.InvariantCulture));
                else result.Append(value);
            }
            return result.ToString();
        }
    }
}
