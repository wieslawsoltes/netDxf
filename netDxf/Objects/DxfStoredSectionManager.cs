// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace netDxf.Objects
{
    /// <summary>A loaded section-manager packet with immutable ordered section references.</summary>
    /// <remarks>
    /// This source-bound object preserves the stored update flag and section list without performing
    /// live sectioning or synchronizing the list with edits to the drawing. Creation, cloning, erasure
    /// and version conversion require the complete manager lifecycle and are not supported.
    /// </remarks>
    public sealed class DxfStoredSectionManager : DxfDatabaseObject
    {
        internal const int MaximumSections = 65536;
        private readonly DxfDocument source;
        private readonly List<Section> sections = new List<Section>();
        private readonly string[] sectionHandles;
        private DxfObject[] sourceReactors;
        private string sourceEntryName;
        private bool sourceEntryIsHardOwner;
        private bool resolved;

        internal DxfStoredSectionManager(DxfDocument source, string codeName, IList<DxfTag> tags,
            bool requiresFullUpdate, IEnumerable<string> handles) : base(codeName)
        {
            this.source = source;
            this.SourceVersion = source.DrawingVariables.AcadVer;
            this.RequiresFullUpdate = requiresFullUpdate;
            this.sectionHandles = handles.ToArray();
            this.Tags = new List<DxfTag>(tags).AsReadOnly();
        }

        /// <summary>Gets the original DXF profile, which must be retained on save.</summary>
        public DxfVersion SourceVersion { get; }
        /// <summary>Gets the stored group-70 update flag; no update or generation is performed.</summary>
        public bool RequiresFullUpdate { get; }
        /// <summary>Gets the complete immutable subclass packet, excluding common metadata and XData.</summary>
        public IReadOnlyList<DxfTag> Tags { get; }
        /// <summary>Gets the exact registered section entities in packet order, including repetitions.</summary>
        public IReadOnlyList<Section> Sections { get { return this.sections.AsReadOnly(); } }
        internal override IEnumerable<DxfObject> DatabaseReferences { get { return this.sections; } }
        internal override IEnumerable<DxfTag> AllocationReservations { get { return this.Tags; } }
        internal override DxfDatabaseObject CloneShell()
        { throw new NotSupportedException("Stored section-manager cloning requires the complete manager lifecycle."); }

        internal void Resolve(Func<string, DxfObject> resolve)
        {
            foreach (string handle in this.sectionHandles)
            {
                Section section = resolve(handle) as Section;
                if (section == null) throw new FormatException("A section-manager pointer requires an exact source SECTION entity: " + handle);
                this.sections.Add(section);
            }
            DxfDictionary root = this.source.Objects.Root;
            DxfDictionaryEntry entry = root.Entries.SingleOrDefault(item => string.Equals(item.Name, "ACAD_SECTION_MANAGER", StringComparison.OrdinalIgnoreCase));
            if (!ReferenceEquals(this.Owner, root) || entry == null || !ReferenceEquals(entry.Target, this))
                throw new FormatException("A stored section manager requires its source root ACAD_SECTION_MANAGER entry.");
            this.sourceEntryIsHardOwner = entry.IsHardOwner;
            this.sourceEntryName = entry.Name;
            this.sourceReactors = this.PersistentReactors.ToArray();
            this.resolved = true;
        }

        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            if (!this.resolved || !ReferenceEquals(database.Document, this.source))
            { errors.Add("A stored section manager must remain in its source document."); return; }
            if (this.source.DrawingVariables.AcadVer != this.SourceVersion)
                errors.Add("Stored section-manager profile conversion requires complete schema regeneration.");
            DxfDictionary root = this.source.Objects.Root;
            if (!ReferenceEquals(this.Owner, root) || !root.Entries.Any(entry => entry.Name == this.sourceEntryName
                && ReferenceEquals(entry.Target, this) && entry.IsHardOwner == this.sourceEntryIsHardOwner))
                errors.Add("The stored section manager's source root entry changed.");
            if (!this.PersistentReactors.SequenceEqual(this.sourceReactors))
                errors.Add("The stored section manager's source persistent reactors changed.");
            foreach (Section section in this.sections)
                if (!ReferenceEquals(this.source.GetObjectByHandle(section.Handle), section))
                    errors.Add("A stored section-manager target is no longer registered.");
        }
    }
}
