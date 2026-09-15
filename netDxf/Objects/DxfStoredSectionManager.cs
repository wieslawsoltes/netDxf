// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace netDxf.Objects
{
    /// <summary>A source-profile-bound section-manager packet with explicitly replaceable ordered section references.</summary>
    /// <remarks>
    /// This source-bound object stores the update flag and section list without performing
    /// live sectioning or automatically synchronizing the list with edits to the drawing. Explicit creation
    /// and erasure use the document database's manager APIs. Cloning and version conversion are not supported.
    /// </remarks>
    public sealed class DxfStoredSectionManager : DxfDatabaseObject
    {
        internal const int MaximumSections = 65536;
        private readonly DxfDocument source;
        private List<Section> sections = new List<Section>();
        private readonly string[] sectionHandles;
        private DxfObject[] sourceReactors;
        private string sourceEntryName;
        private bool sourceEntryIsHardOwner;
        private bool resolved;
        private bool editing;

        internal DxfStoredSectionManager(DxfDocument source, string codeName, IList<DxfTag> tags,
            bool requiresFullUpdate, IEnumerable<string> handles) : base(codeName)
        {
            this.source = source;
            this.SourceVersion = source.DrawingVariables.AcadVer;
            this.RequiresFullUpdate = requiresFullUpdate;
            this.sectionHandles = handles.ToArray();
            this.Tags = new List<DxfTag>(tags).AsReadOnly();
        }

        internal static DxfStoredSectionManager CreateCanonical(DxfDocument source, DxfDictionary root, IList<Section> members, bool requiresFullUpdate)
        {
            var tags = new List<DxfTag>(members.Count + 3)
            {
                new DxfTag(100, "AcDbSectionManager"), new DxfTag(70, requiresFullUpdate ? (short)1 : (short)0),
                new DxfTag(90, members.Count)
            };
            foreach (Section section in members) tags.Add(new DxfTag(330, section.Handle));
            var manager = new DxfStoredSectionManager(source, "SECTION_MANAGER", tags, requiresFullUpdate, Array.Empty<string>())
            {
                Owner = root, sourceEntryName = "ACAD_SECTION_MANAGER", sourceEntryIsHardOwner = false,
                sourceReactors = new DxfObject[] { root }, resolved = true
            };
            manager.sections.AddRange(members);
            manager.PersistentReactors.Add(root);
            return manager;
        }

        /// <summary>Gets the original DXF profile, which must be retained on save.</summary>
        public DxfVersion SourceVersion { get; }
        /// <summary>Gets the stored group-70 update flag; no update or generation is performed.</summary>
        public bool RequiresFullUpdate { get; private set; }
        /// <summary>Gets a read-only snapshot of the current subclass packet, excluding common metadata and XData.</summary>
        public IReadOnlyList<DxfTag> Tags { get; private set; }
        /// <summary>Gets the exact registered section entities in packet order, including repetitions.</summary>
        public IReadOnlyList<Section> Sections { get { return this.sections.AsReadOnly(); } }
        internal override IEnumerable<DxfObject> DatabaseReferences { get { return this.sections; } }
        internal override IEnumerable<DxfTag> AllocationReservations { get { return this.Tags; } }
        internal override DxfDatabaseObject CloneShell()
        { throw new NotSupportedException("Stored section-manager cloning requires the complete manager lifecycle."); }

        /// <summary>Replaces the ordered section membership and stored update flag after validating the complete request.</summary>
        /// <param name="sections">Actual registered sections in this manager's source document. Repetitions retain their positions; null entries are not permitted.</param>
        /// <param name="requiresFullUpdate">The group-70 flag to store, without performing a section update.</param>
        /// <remarks>
        /// Only the qualified public group-70/90/330 envelope changes. The manager retains its source
        /// profile, root anchor, reactors, metadata and XData. Caller enumeration is completed before
        /// validation and commit; no handles are assigned. Explicit destination section identities
        /// may be supplied to another loaded manager, but foreign objects are never rebound by name.
        /// Previously obtained Sections and Tags snapshots remain unchanged. This method does not
        /// create, clone or erase a manager or section, or automatically evaluate section geometry.
        /// </remarks>
        public void ReplaceSections(IEnumerable<Section> sections, bool requiresFullUpdate)
        {
            if (sections == null) throw new ArgumentNullException(nameof(sections));
            if (this.editing) throw new InvalidOperationException("Section-manager membership replacement cannot be reentered.");
            this.editing = true;
            try
            {
                var replacement = new List<Section>();
                foreach (Section section in sections)
                {
                    if (replacement.Count == MaximumSections) throw new ArgumentException("The section-manager membership exceeds 65536 entries.", nameof(sections));
                    replacement.Add(section);
                }
                if (this.Database == null || !ReferenceEquals(this.Database.Document, this.source)
                    || !ReferenceEquals(this.source.GetObjectByHandle(this.Handle), this))
                    throw new InvalidOperationException("The section manager must remain registered in its source document.");
                var errors = new List<string>();
                this.ValidateDatabaseSchema(this.Database, errors);
                if (errors.Count != 0) throw new InvalidOperationException("Cannot edit an invalid stored section manager: " + string.Join("; ", errors));
                foreach (Section section in replacement)
                    if (section == null || section.IsErased || section.Handle == null || section.Owner == null
                        || section.Owner.Record.Owner != this.source.Blocks || !section.Owner.Entities.Contains(section)
                        || !ReferenceEquals(this.source.GetObjectByHandle(section.Handle), section))
                        throw new ArgumentException("Every manager member must be an actual registered section in its source document.", nameof(sections));
                var tags = new List<DxfTag>(replacement.Count + 3)
                {
                    new DxfTag(100, "AcDbSectionManager"),
                    new DxfTag(70, requiresFullUpdate ? (short)1 : (short)0),
                    new DxfTag(90, replacement.Count)
                };
                foreach (Section section in replacement) tags.Add(new DxfTag(330, section.Handle));
                var packet = tags.AsReadOnly();
                // All enumeration, allocation and validation has finished; these assignments cannot call user code.
                this.sections = replacement;
                this.Tags = packet;
                this.RequiresFullUpdate = requiresFullUpdate;
            }
            finally { this.editing = false; }
        }

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
                if (section.IsErased || section.Owner == null || section.Owner.Record.Owner != this.source.Blocks
                    || !section.Owner.Entities.Contains(section) || !ReferenceEquals(this.source.GetObjectByHandle(section.Handle), section))
                    errors.Add("A stored section-manager target is no longer registered.");
        }
    }
}
