// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Header;
using netDxf.IO;

namespace netDxf.Objects
{
    /// <summary>A loaded SUNSTUDY version-zero empty-date packet with immutable source data.</summary>
    /// <remarks>Only the qualified R2013/R2018 stored shape is projected. Values are not evaluated,
    /// output numbers are not assigned enumeration meanings, and raw hour flags are not clock times.
    /// The complete packet remains in its source document and profile. Cloning and erasure require
    /// additional application lifecycle knowledge. Common metadata and XData keep their normal APIs.</remarks>
    public sealed class DxfStoredSunStudy : DxfDatabaseObject
    {
        private readonly DxfDocument source;
        private readonly List<DxfObject> references = new List<DxfObject>();
        private readonly Dictionary<string, DxfObject> identities = new Dictionary<string, DxfObject>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<DxfObject, DxfObject> owners = new Dictionary<DxfObject, DxfObject>();
        private bool resolved;

        internal DxfStoredSunStudy(DxfDocument source, IList<DxfTag> payload, Func<string, string> decode) : base("SUNSTUDY")
        {
            this.source = source;
            this.SourceVersion = source.DrawingVariables.AcadVer;
            this.Payload = new List<DxfTag>(payload).AsReadOnly();
            this.Name = decode((string)payload[2].Value);
            this.Description = decode((string)payload[3].Value);
            this.SheetSetName = decode((string)payload[5].Value);
            this.SheetSubsetName = decode((string)payload[7].Value);
            this.RawHourFlags = payload.Skip(15).Take((short)payload[14].Value).Select(tag => (bool)tag.Value).ToList().AsReadOnly();
            this.References = this.references.AsReadOnly();
        }
        /// <summary>Gets the exact source DXF profile; profile conversion is unsupported.</summary>
        public DxfVersion SourceVersion { get; }
        /// <summary>Gets the complete immutable subclass packet, excluding common metadata and XData.</summary>
        public IReadOnlyList<DxfTag> Payload { get; }
        /// <summary>Gets the stored internal version number, currently zero.</summary>
        public int Version { get { return (int)this.Payload[1].Value; } }
        /// <summary>Gets the decoded setup name.</summary>
        public string Name { get; }
        /// <summary>Gets the decoded description.</summary>
        public string Description { get; }
        /// <summary>Gets the stored output type number without inferring enumeration semantics.</summary>
        public short OutputType { get { return (short)this.Payload[4].Value; } }
        /// <summary>Gets the decoded stored sheet-set name.</summary>
        public string SheetSetName { get; }
        /// <summary>Gets the stored subset-selection flag.</summary>
        public bool UseSubset { get { return (bool)this.Payload[6].Value; } }
        /// <summary>Gets the decoded stored subset name.</summary>
        public string SheetSubsetName { get; }
        /// <summary>Gets the stored calendar-selection flag; the qualified date array remains empty.</summary>
        public bool SelectDates { get { return (bool)this.Payload[8].Value; } }
        /// <summary>Gets the stored date-array size, currently zero.</summary>
        public int DateCount { get { return (int)this.Payload[9].Value; } }
        /// <summary>Gets the stored date-range flag.</summary>
        public bool SelectDateRange { get { return (bool)this.Payload[10].Value; } }
        /// <summary>Gets the raw group-93 start value without computing a date.</summary>
        public int StartTime { get { return (int)this.Payload[11].Value; } }
        /// <summary>Gets the raw group-94 end value without computing a date.</summary>
        public int EndTime { get { return (int)this.Payload[12].Value; } }
        /// <summary>Gets the raw group-95 interval value.</summary>
        public int Interval { get { return (int)this.Payload[13].Value; } }
        /// <summary>Gets the ordered group-290 boolean values after group 73; these are not clock hours.</summary>
        public IReadOnlyList<bool> RawHourFlags { get; }
        /// <summary>Gets the exact group-340 page-setup identity, or null for a stored null handle.</summary>
        public DxfObject PageSetup { get; private set; }
        /// <summary>Gets the exact group-341 view identity, or null for a stored null handle.</summary>
        public DxfObject View { get; private set; }
        /// <summary>Gets the exact group-342 visual-style identity, or null for a stored null handle.</summary>
        public DxfObject VisualStyle { get; private set; }
        /// <summary>Gets the exact group-343 text-style identity, or null for a stored null handle.</summary>
        public DxfObject TextStyle { get; private set; }
        /// <summary>Gets all non-null exposed source references in packet order, including repetitions.</summary>
        public IReadOnlyList<DxfObject> References { get; }
        internal override IEnumerable<DxfObject> DatabaseReferences { get { return this.references; } }
        internal override IEnumerable<DxfTag> AllocationReservations { get { return this.Payload; } }
        internal override DxfDatabaseObject CloneShell()
        { throw new NotSupportedException("Stored SUNSTUDY cloning requires its complete application lifecycle."); }

        internal void Resolve(Func<string, DxfObject> resolve)
        {
            foreach (DxfTag tag in this.Payload.Where(DxfObjectDatabase.IsReference))
            {
                string handle = (string)tag.Value;
                DxfObject target = Convert.ToUInt64(handle, 16) == 0 ? null : resolve(handle);
                if (target == null && Convert.ToUInt64(handle, 16) != 0) throw new FormatException("SUNSTUDY requires an exact source reference identity: " + handle);
                if (target != null) { this.identities[handle] = target; this.references.Add(target); }
                switch (tag.Code) { case 340: this.PageSetup = target; break; case 341: this.View = target; break; case 342: this.VisualStyle = target; break; case 343: this.TextStyle = target; break; }
            }
            if (!(this.Owner is DxfDictionary)) throw new FormatException("SUNSTUDY requires its registered source owner dictionary.");
            var visited = new HashSet<DxfObject>();
            for (DxfObject item = this; item != null && !ReferenceEquals(item, this.source); item = item.Owner)
            {
                if (!visited.Add(item)) throw new FormatException("SUNSTUDY source ownership contains a cycle.");
                if (!ReferenceEquals(resolve(item.Handle), item)) throw new FormatException("SUNSTUDY source ancestry contains an unregistered source object.");
                this.owners.Add(item, item.Owner);
            }
            this.resolved = true;
        }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            if (!this.resolved || database == null || !ReferenceEquals(database.Document, this.source))
            { errors.Add("Stored SUNSTUDY requires its registered source document."); return; }
            if (this.source.DrawingVariables.AcadVer != this.SourceVersion) errors.Add("Stored SUNSTUDY profile conversion requires the complete application schema.");
            foreach (var pair in this.owners)
                if (!ReferenceEquals(pair.Key.Owner, pair.Value) || !ReferenceEquals(this.source.GetObjectByHandle(pair.Key.Handle), pair.Key))
                    errors.Add("Stored SUNSTUDY source ownership changed.");
            foreach (var pair in this.identities)
                if (!ReferenceEquals(this.source.GetObjectByHandle(pair.Key), pair.Value)) errors.Add("Stored SUNSTUDY dependency identity changed: " + pair.Key);
        }
    }
}
