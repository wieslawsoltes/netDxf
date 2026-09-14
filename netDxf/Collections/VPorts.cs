#region netDxf library licensed under the MIT License
// 
//                       netDxf library
// Copyright (c) Daniel Carvajal (haplokuon@gmail.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
#endregion

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using netDxf.Tables;

namespace netDxf.Collections
{
    /// <summary>A table of named model-space viewport configurations.</summary>
    /// <remarks>
    /// Inherited name lookup, Count, Items, and enumeration expose the first record of each
    /// configuration. Records contains every tile in file order, including duplicate names.
    /// The first record named *Active is the document's current viewport.
    /// </remarks>
    public sealed class VPorts : TableObjects<VPort>
    {
        private readonly List<VPort> records = new List<VPort>();
        private readonly ReadOnlyCollection<VPort> readOnlyRecords;

        internal VPorts(DxfDocument document) : this(document, null, true) { }
        internal VPorts(DxfDocument document, string handle) : this(document, handle, true) { }
        internal VPorts(DxfDocument document, string handle, bool createActive)
            : base(document, DxfObjectCode.VportTable, handle)
        {
            this.readOnlyRecords = this.records.AsReadOnly();
            if (createActive) this.EnsureActive();
        }

        /// <summary>Every physical viewport record in table order, including repeated configuration names.</summary>
        public IReadOnlyList<VPort> Records { get { return this.readOnlyRecords; } }

        /// <summary>Returns a snapshot of all tiles in a named configuration, in table order.</summary>
        public IReadOnlyList<VPort> GetConfiguration(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            List<VPort> result = new List<VPort>();
            foreach (VPort record in this.records)
                if (string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase)) result.Add(record);
            return result.AsReadOnly();
        }

        /// <summary>Adds a detached tile, retaining other records with the same configuration name.</summary>
        /// <remarks>A record owned by another table must be cloned before adding it here.</remarks>
        public VPort AddRecord(VPort record) { return this.AddRecord(record, true); }

        internal VPort AddRecord(VPort record, bool assignHandle)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (ReferenceEquals(record.Owner, this)) return record;
            if (record.Owner != null) throw new ArgumentException("Clone a viewport record before moving it between documents.", nameof(record));
            if (string.IsNullOrWhiteSpace(record.Name) || (!VPort.IsActiveName(record.Name) && !TableObject.IsValidName(record.Name)))
                throw new ArgumentException("Invalid viewport configuration name.", nameof(record));
            foreach (XData data in record.XData.Values)
            {
                var registryOwner = data.ApplicationRegistry.Owner;
                if (registryOwner != null && !ReferenceEquals(registryOwner.Owner, this.Owner))
                    throw new ArgumentException("Clone XData whose application registry belongs to another document before adding this viewport.", nameof(record));
            }
            UcsReferences.Validate(record, this.Owner);
            if (assignHandle || string.IsNullOrEmpty(record.Handle))
            {
                // A minimal imported document can omit HANDSEED. Do not reuse an indexed identity.
                do { this.Owner.NumHandles = record.AssignHandle(this.Owner.NumHandles); }
                while (this.Owner.GetObjectByHandle(record.Handle) != null);
            }
            else if (this.Owner.GetObjectByHandle(record.Handle) != null)
                throw new ArgumentException("Duplicate viewport record handle: " + record.Handle, nameof(record));

            this.Owner.AddedObjects.Add(record.Handle, record);
            record.Owner = this;
            UcsReferences.Register(record);
            this.records.Add(record);
            if (!this.List.ContainsKey(record.Name))
            {
                this.List.Add(record.Name, record);
                this.References.Add(record.Name, new DxfObjectReferences());
            }
            return record;
        }

        /// <summary>Adds one named configuration, or returns its existing first record.</summary>
        internal override VPort Add(VPort record, bool assignHandle)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            VPort existing;
            return this.List.TryGetValue(record.Name, out existing) ? existing : this.AddRecord(record, assignHandle);
        }

        internal void EnsureActive()
        {
            if (!this.Contains(VPort.DefaultName)) this.AddRecord(VPort.Active, true);
        }

        /// <summary>Removes all records of a named configuration. The active configuration is retained.</summary>
        public override bool Remove(string name)
        {
            if (name == null || VPort.IsActiveName(name) || !this.Contains(name) || this.HasReferences(name)) return false;
            IReadOnlyList<VPort> entries = this.GetConfiguration(name);
            foreach (VPort record in entries) this.Remove(record);
            return entries.Count != 0;
        }

        /// <summary>Removes one physical record; the last active record cannot be removed.</summary>
        public override bool Remove(VPort record)
        {
            if (record == null || !ReferenceEquals(record.Owner, this)) return false;
            if (VPort.IsActiveName(record.Name) && this.GetConfiguration(VPort.DefaultName).Count == 1) return false;
            if (this.HasReferences(record.Name)) return false;
            UcsReferences.Unregister(record);
            this.Owner.AddedObjects.Remove(record.Handle);
            this.records.Remove(record);
            this.RebuildIndex(null, null);
            record.Owner = null;
            record.Handle = null;
            return true;
        }

        internal void ValidateRecordRename(VPort record)
        {
            if (this.HasReferences(record.Name)) throw new ArgumentException("A referenced viewport configuration cannot be renamed.");
        }

        internal void CommitRecordRename(VPort record, string newName)
        {
            this.RebuildIndex(record, newName);
        }

        private void RebuildIndex(VPort renamed, string newName)
        {
            // The event runs before TableObject updates Name; use its pending name here.
            var previousReferences = new Dictionary<string, DxfObjectReferences>(this.References, StringComparer.OrdinalIgnoreCase);
            this.List.Clear();
            this.References.Clear();
            foreach (VPort record in this.records)
            {
                string name = ReferenceEquals(record, renamed) ? newName : record.Name;
                if (this.List.ContainsKey(name)) continue;
                this.List.Add(name, record);
                DxfObjectReferences references;
                this.References.Add(name, previousReferences.TryGetValue(name, out references) ? references : new DxfObjectReferences());
            }
        }
    }
}
