// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using netDxf.Entities;
using netDxf.Header;

namespace netDxf.Objects
{
    public sealed partial class DxfObjectDatabase
    {
        private bool creatingSectionManager;
        private bool sectionManagerCreationReentered;

        /// <summary>Creates and registers an explicit canonical section manager in the named-object root.</summary>
        /// <param name="sections">Actual registered sections in this document, in stored order. Repetitions are retained; null entries are not permitted.</param>
        /// <param name="requiresFullUpdate">The stored update flag, without performing a section update.</param>
        /// <returns>The newly registered, source-profile-bound manager.</returns>
        /// <remarks>
        /// Caller enumeration and disposal finish before document validation or handle allocation.
        /// An occupied manager anchor, existing manager object, incompatible CLASS, invalid target,
        /// unsupported profile or exhausted handle range rejects without a partial manager.
        /// The required root common metadata and each distinct requested section are validated
        /// after caller code finishes; unrelated document objects are outside this validation.
        /// Any recursive creation attempt invalidates the outer request, even if the caller catches it.
        /// The root's existing flags and other entries are retained. A compatible CLASS keeps all
        /// metadata, with a present instance count updated to one; absent declarations or counts
        /// are not synthesized. Sections are neither created nor
        /// evaluated, and membership is never maintained automatically. Caller-owned document changes
        /// made during enumeration are not rolled back.
        /// </remarks>
        public DxfStoredSectionManager CreateSectionManager(IEnumerable<Section> sections, bool requiresFullUpdate)
        {
            if (this.creatingSectionManager)
            {
                this.sectionManagerCreationReentered = true;
                throw new InvalidOperationException("Section-manager creation cannot be reentered.");
            }
            if (sections == null) throw new ArgumentNullException(nameof(sections));
            this.creatingSectionManager = true;
            this.sectionManagerCreationReentered = false;
            try
            {
                var members = new List<Section>();
                foreach (Section section in sections)
                {
                    if (members.Count == DxfStoredSectionManager.MaximumSections)
                        throw new ArgumentException("The section-manager membership exceeds 65536 entries.", nameof(sections));
                    members.Add(section);
                }
                if (this.sectionManagerCreationReentered)
                    throw new InvalidOperationException("A recursive creation attempt invalidated section-manager creation.");
                DxfVersion version = this.Document.DrawingVariables.AcadVer;
                if (version != DxfVersion.AutoCad2007 && version != DxfVersion.AutoCad2010
                    && version != DxfVersion.AutoCad2013 && version != DxfVersion.AutoCad2018)
                    throw new NotSupportedException("SECTION_MANAGER creation requires an R2007 through R2018 typed profile.");
                if (this.Root.IsErased || this.Root.Database != this || !this.IsRegistered(this.Root)
                    || !ReferenceEquals(this.Root.Owner, this.Document))
                    throw new InvalidOperationException("The section manager requires this document's registered named-object root.");
                if (this.Root.Contains("ACAD_SECTION_MANAGER") || this.objects.Values.Any(item =>
                    string.Equals(item.CodeName, "SECTION_MANAGER", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.CodeName, "SECTIONMANAGER", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("The document already contains a section-manager object or root anchor.");
                DxfClass definition = null;
                if (this.Document.Classes.Contains("SECTION_MANAGER"))
                {
                    definition = this.Document.Classes["SECTION_MANAGER"];
                    if (definition.CppClassName != "AcDbSectionManager" || definition.ApplicationName != "ObjectDBX Classes"
                        || definition.ProxyFlags != 1024 || definition.WasProxy || definition.IsEntity)
                        throw new InvalidOperationException("The existing SECTION_MANAGER CLASS conflicts with the canonical manager definition.");
                }
                this.ValidateSectionManagerRootMetadata();
                var validated = new HashSet<DxfObject>(ObjectIdentity);
                foreach (Section section in members)
                {
                    if (section == null || section.IsErased || section.Handle == null || section.Owner == null
                        || section.Owner.Record.Owner != this.Document.Blocks || !section.Owner.Entities.Contains(section)
                        || !ReferenceEquals(this.Document.GetObjectByHandle(section.Handle), section))
                        throw new ArgumentException("Every manager member must be an actual registered section in this document.", nameof(sections));
                    if (validated.Add(section)) section.Validate(this.Document);
                }
                long candidate = this.Document.NumHandles;
                if (candidate <= 0) throw new InvalidOperationException("The document handle range is exhausted.");
                while (candidate < long.MaxValue && this.Document.StoredTableHandleTarget(candidate.ToString("X", CultureInfo.InvariantCulture)) != null) candidate++;
                if (candidate == long.MaxValue) throw new InvalidOperationException("The document handle range is exhausted.");
                var manager = DxfStoredSectionManager.CreateCanonical(this.Document, this.Root, members, requiresFullUpdate);
                manager.Handle = candidate.ToString("X", CultureInfo.InvariantCulture);
                // Only checked internal registration and the fixed root entry remain. No caller code runs.
                this.Register(manager, true);
                this.Root.AddLoaded("ACAD_SECTION_MANAGER", manager, false);
                if (definition != null && definition.InstanceCount.HasValue) definition.InstanceCount = 1;
                return manager;
            }
            finally
            {
                this.creatingSectionManager = false;
                this.sectionManagerCreationReentered = false;
            }
        }

        private void ValidateSectionManagerRootMetadata()
        {
            if (this.Root.ExtensionDictionary != null && (!this.IsRegistered(this.Root.ExtensionDictionary)
                || !ReferenceEquals(this.Root.ExtensionDictionary.Owner, this.Root)))
                throw new InvalidOperationException("The manager root extension dictionary must be registered with its reciprocal owner.");
            foreach (DxfObject reactor in this.Root.PersistentReactors)
                if (reactor == null || !this.IsRegistered(reactor))
                    throw new InvalidOperationException("The manager root reactors must be registered in this document.");
            foreach (XData data in this.Root.XData.Values)
                foreach (XDataRecord tag in data.XDataRecord)
                    if (tag.Code == XDataCode.DatabaseHandle && !IsNullHandle((string)tag.Value)
                        && this.Document.GetObjectByHandle((string)tag.Value) == null)
                        throw new InvalidOperationException("The manager root XData references must resolve in this document.");
        }

        /// <summary>Explicitly erases a registered section manager and its qualified typed owned metadata.</summary>
        /// <param name="manager">The exact manager registered under its original root anchor in this document.</param>
        /// <remarks>
        /// Original profile, root entry and reactor invariants must remain valid. External incoming
        /// references and unqualified owned objects reject before mutation. Root aliases are removed;
        /// referenced Section entities remain registered. Existing CLASS metadata is retained, with
        /// a present instance count updated to the actual remaining record count. Erasure is terminal.
        /// </remarks>
        public void EraseSectionManager(DxfStoredSectionManager manager)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            if (manager.IsErased) throw new InvalidOperationException("The section manager has already been erased.");
            if (manager.Database != this) throw new ArgumentException("The manager must belong to this database.", nameof(manager));
            this.CheckRegistered(manager);
            var errors = new List<string>();
            manager.ValidateDatabaseSchema(this, errors);
            if (errors.Count != 0) throw new InvalidOperationException("Cannot erase an invalid stored section manager: " + string.Join("; ", errors));
            DxfClass definition = this.Document.Classes.Contains(manager.CodeName) ? this.Document.Classes[manager.CodeName] : null;
            int remaining = this.objects.Values.Count(item => item.CodeName == manager.CodeName) - 1;
            this.EraseOwnedTreeCore(manager, manager);
            if (definition != null && definition.InstanceCount.HasValue) definition.InstanceCount = remaining;
        }
    }
}
