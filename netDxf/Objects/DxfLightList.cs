using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using netDxf.Entities;
using netDxf.Header;

namespace netDxf.Objects
{
    /// <summary>A LIGHTLIST entry containing a LIGHT reference and its independently stored name.</summary>
    public sealed class DxfLightListEntry
    {
        /// <summary>Creates an entry. The name need not equal <see cref="Entities.Light.Name"/>.</summary>
        public DxfLightListEntry(Light light, string name)
        {
            this.Light = light ?? throw new ArgumentNullException(nameof(light));
            CheckName(name);
            this.Name = name;
        }
        /// <summary>Gets the exact LIGHT referenced by the payload group 5.</summary>
        public Light Light { get; }
        /// <summary>Gets the stored group-1 name; an empty name is retained.</summary>
        public string Name { get; }
        private static void CheckName(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (name.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
                throw new ArgumentException("A LIGHTLIST name must be a single line without NUL.", nameof(name));
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsHighSurrogate(name[i]))
                {
                    if (i + 1 >= name.Length || !char.IsLowSurrogate(name[i + 1]))
                        throw new ArgumentException("A LIGHTLIST name cannot contain an unpaired UTF-16 surrogate.", nameof(name));
                    i++;
                }
                else if (char.IsLowSurrogate(name[i]))
                    throw new ArgumentException("A LIGHTLIST name cannot contain an unpaired UTF-16 surrogate.", nameof(name));
            }
        }
    }

    /// <summary>An ordered LIGHTLIST storing explicit version metadata and LIGHT/name pairs.</summary>
    /// <remarks>
    /// This model qualifies the published DXF storage grammar, not the meaning or validity of any
    /// particular version value. The caller supplies the version explicitly. Entries can repeat a
    /// LIGHT and retain names independently of that entity. No ACAD_LIGHT attachment is inferred.
    /// Typed export requires DXF 2007 or later.
    /// </remarks>
    public sealed class DxfLightList : DxfDatabaseObject
    {
        private readonly EntryCollection entries;
        /// <summary>Creates an empty detached LIGHTLIST with the caller's exact stored version.</summary>
        /// <param name="storedVersion">The raw signed group-90 value; no semantic version is assumed.</param>
        public DxfLightList(int storedVersion) : base("LIGHTLIST")
        { this.StoredVersion = storedVersion; this.entries = new EntryCollection(this); }
        /// <summary>Gets or sets the uninterpreted signed group-90 version value.</summary>
        public int StoredVersion { get; set; }
        /// <summary>Gets the editable ordered entries. Duplicate LIGHT references are retained.</summary>
        public Collection<DxfLightListEntry> Entries { get { return this.entries; } }
        internal override IEnumerable<DxfObject> DatabaseReferences
        { get { foreach (DxfLightListEntry entry in this.entries) yield return entry.Light; } }
        internal override DxfDatabaseObject CloneShell() { return new DxfLightList(this.StoredVersion); }
        internal override void CopyDatabaseReferencesTo(DxfDatabaseObject clone, Func<DxfObject, DxfObject> resolve)
        {
            foreach (DxfLightListEntry entry in this.entries)
            {
                Light light = resolve(entry.Light) as Light;
                if (light == null) throw new ArgumentException("A LIGHTLIST clone mapping must identify a LIGHT.");
                ((DxfLightList)clone).Entries.Add(new DxfLightListEntry(light, entry.Name));
            }
        }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            if (database.Document.DrawingVariables.AcadVer < DxfVersion.AutoCad2007)
                errors.Add("Typed LIGHTLIST storage requires DXF 2007 or later.");
        }
        private sealed class EntryCollection : Collection<DxfLightListEntry>
        {
            private readonly DxfLightList owner;
            internal EntryCollection(DxfLightList owner) { this.owner = owner; }
            private void Check(DxfLightListEntry entry)
            {
                if (entry == null) throw new ArgumentNullException(nameof(entry));
                if (this.owner.Database != null) this.owner.Database.CheckRegistered(entry.Light);
            }
            protected override void InsertItem(int index, DxfLightListEntry item) { this.Check(item); base.InsertItem(index, item); }
            protected override void SetItem(int index, DxfLightListEntry item) { this.Check(item); base.SetItem(index, item); }
        }
    }
}
