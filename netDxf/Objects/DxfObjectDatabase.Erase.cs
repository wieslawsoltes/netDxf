using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace netDxf.Objects
{
    public sealed partial class DxfObjectDatabase
    {
        /// <summary>Permanently erases a registered object and every typed object owned by it.</summary>
        /// <param name="root">The registered root of the ownership subtree, including an object whose owning dictionary names were already removed.</param>
        /// <remarks>
        /// All owning aliases of the selected root and its reciprocal host extension attachment are removed.
        /// Other incoming references reject the operation before any mutation. Ownership follows common
        /// owners, not pointer strength; referenced entities, blocks and resources are never cascaded.
        /// Opaque objects inside the subtree reject. Surviving opaque exposed handle fields are checked,
        /// but dependencies hidden in private strings or binary data cannot be proven absent.
        /// Erasure is terminal: handles and payload remain available for inspection, without a resurrection API.
        /// </remarks>
        public void EraseOwnedTree(DxfDatabaseObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (root.IsErased) throw new InvalidOperationException("The object has already been erased.");
            if (root.Database != this) throw new ArgumentException("The object must belong to this database.", nameof(root));
            this.CheckRegistered(root);
            if (ReferenceEquals(root, this.Root)) throw new InvalidOperationException("The named object dictionary root cannot be erased.");

            // Snapshot all retained metadata carriers, including ATTRIBs that are not in AddedObjects.
            List<DxfObject> carriers = this.ErasureCarriers();
            var children = new Dictionary<DxfObject, List<DxfObject>>(ObjectIdentity);
            foreach (DxfObject item in carriers)
                if (item.Owner != null)
                {
                    if (!children.TryGetValue(item.Owner, out List<DxfObject> owned)) children.Add(item.Owner, owned = new List<DxfObject>());
                    owned.Add(item);
                }
            var deleted = new HashSet<DxfObject>(ObjectIdentity);
            var tree = new List<DxfDatabaseObject>();
            var pending = new Queue<DxfObject>(); pending.Enqueue(root);
            while (pending.Count != 0)
            {
                DxfObject item = pending.Dequeue();
                if (!deleted.Add(item)) throw new InvalidOperationException("The erased ownership subtree contains a cycle.");
                if (!(item is DxfDatabaseObject value) || ReferenceEquals(item, this.Root))
                    throw new NotSupportedException("An ownership subtree containing a managed legacy object cannot be erased.");
                if (value.IsErased || value.Database != this || !this.IsRegistered(value))
                    throw new InvalidOperationException("The erased ownership subtree has inconsistent registration.");
                if (value is DxfStoredDimAssoc) throw new NotSupportedException("Stored DIMASSOC erasure requires the complete dimension association lifecycle.");
                if (value is DxfOpaqueObject) throw new NotSupportedException("An opaque object requires its application schema before erasure: " + value.CodeName);
                tree.Add(value);
                if (children.TryGetValue(item, out List<DxfObject> next)) foreach (DxfObject child in next) pending.Enqueue(child);
            }
            if (root.Owner == null || !this.IsRegistered(root.Owner)) throw new InvalidOperationException("The erased root has no registered owner.");
            if (ReferenceEquals(root.Owner, this.Document.Layers)) throw new NotSupportedException("The LAYER table extension is managed by the layer-state collection.");
            DxfDictionary parent = root.Owner as DxfDictionary;
            var aliases = parent == null ? new List<DxfDictionaryEntry>() : parent.Entries.Where(e => ReferenceEquals(e.Target, root)).ToList();
            if (ReferenceEquals(parent, this.Root) && aliases.Any(e => IsReservedName(e.Name)))
                throw new NotSupportedException("Managed legacy dictionary entries cannot be erased through this API.");
            bool extension = ReferenceEquals(root.Owner.ExtensionDictionary, root);
            if (parent == null && !extension) throw new NotSupportedException("Erase an object through its owning dictionary or reciprocal extension attachment.");

            var handles = new HashSet<ulong>();
            foreach (DxfDatabaseObject item in tree)
            {
                if (!handles.Add(ErasureHandle(item.Handle))) throw new InvalidOperationException("The erased ownership subtree contains duplicate numeric handles.");
                // AddedObjects.Remove releases these counts and subscriptions. Verify its lookups now,
                // before the commit performs any collection mutation or event unsubscription.
                foreach (XData data in item.XData.Values)
                    if (!this.Document.ApplicationRegistries.References.ContainsKey(data.ApplicationRegistry.Name))
                        throw new InvalidOperationException("An erased object has inconsistent APPID reference bookkeeping: " + data.ApplicationRegistry.Name);
            }

            foreach (DxfObject item in carriers)
            {
                if (deleted.Contains(item)) continue;
                Action<DxfObject, string> reference = (target, field) =>
                {
                    if (target != null && deleted.Contains(target)) throw ErasureReference(item, field, target.Handle);
                };
                Action<string, string> handle = (target, field) =>
                {
                    if (handles.Contains(ErasureHandle(target))) throw ErasureReference(item, field, target);
                };
                reference(item.Owner, "owner");
                if (!(extension && ReferenceEquals(item, root.Owner) && ReferenceEquals(item.ExtensionDictionary, root)))
                    reference(item.ExtensionDictionary, "extension dictionary");
                foreach (DxfObject reactor in item.PersistentReactors) reference(reactor, "persistent reactor");
                if (item is EntityObject entity) foreach (DxfObject reactor in entity.Reactors) reference(reactor, "entity reactor");
                foreach (XData data in item.XData.Values)
                    foreach (XDataRecord tag in data.XDataRecord)
                        if (tag.Code == XDataCode.DatabaseHandle) handle((string)tag.Value, "XData 1005");
                if (item is DxfDatabaseObject databaseObject)
                    foreach (DxfObject target in databaseObject.DatabaseReferences) reference(target, "typed reference");
                if (item is DxfDictionary dictionary)
                    foreach (DxfDictionaryEntry entry in dictionary.Entries)
                        if (!(ReferenceEquals(dictionary, parent) && ReferenceEquals(entry.Target, root))) reference(entry.Target, "dictionary entry " + entry.Name);
                if (item is DxfDictionaryWithDefault fallback) reference(fallback.Default, "dictionary default");
                if (item is DxfXRecord record)
                    foreach (DxfTag tag in record.Data)
                        if (IsReference(tag)) handle((string)tag.Value, "XRECORD " + tag.Code);
                if (item is DxfOpaqueObject opaque)
                    foreach (DxfTag tag in opaque.Tags)
                        if (tag.ValueType == DxfTagValueType.Handle) handle((string)tag.Value, "opaque handle " + tag.Code);
                if (item is Section section) reference(section.GeometrySettings, "section settings");
                if (item is StoredTable table)
                    foreach (DxfObject target in table.References) reference(target, "ACAD_TABLE reference");
                if (item is MultiLeader leader)
                    foreach (MLeaderData data in leader.Data)
                        foreach (DxfObject target in data.References) reference(target, "MULTILEADER reference");
                if (item is Layout layout) reference(layout.PlotSettings?.ShadePlotObject, "layout shade plot 333");
            }
            foreach (HeaderVariable variable in this.Document.DrawingVariables.CustomValues())
            {
                DxfHandleKind kind = DxfGroupCode.GetHandleKind(variable.GroupCode);
                if (kind == DxfHandleKind.None || kind == DxfHandleKind.Arbitrary || variable.Name.Equals("$HANDSEED", StringComparison.OrdinalIgnoreCase)) continue;
                if (!(variable.Value is string value)) throw new InvalidOperationException("A custom header handle has an invalid value: " + variable.Name);
                if (handles.Contains(ErasureHandle(value))) throw ErasureReference(this.Document, "header " + variable.Name, value);
            }

            // No caller callbacks, graph discovery or validation occur after this point.
            // The internal registration removal event only releases APPID counts and subscriptions.
            foreach (DxfDictionaryEntry alias in aliases) parent.Remove(alias.Name);
            if (extension) root.Owner.ExtensionDictionary = null;
            foreach (DxfDatabaseObject item in tree)
            {
                this.Document.AddedObjects.Remove(item.Handle);
                this.objects.Remove(item.Handle);
                item.Database = null;
                item.IsErased = true;
            }
            root.Owner = null;
        }

        private List<DxfObject> ErasureCarriers()
        {
            var found = new HashSet<DxfObject>(ObjectIdentity);
            var result = new List<DxfObject>();
            Action<DxfObject> add = item => { if (item != null && found.Add(item)) result.Add(item); };
            foreach (DxfObject item in this.Document.AddedObjects.Values)
            {
                add(item);
                if (item is Insert insert) foreach (netDxf.Entities.Attribute attribute in insert.Attributes) add(attribute);
                if (item is Block block) add(block.End);
                if (item is Layout layout) add(layout.Viewport);
            }
            foreach (DxfDatabaseObject item in this.objects.Values) add(item);
            return result;
        }
        private static ulong ErasureHandle(string handle)
        {
            if (handle == null || handle.Length == 0 || handle.Length > 16 ||
                !ulong.TryParse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value))
                throw new InvalidOperationException("A retained handle cannot be safely inspected for erasure: " + handle);
            return value;
        }
        private static InvalidOperationException ErasureReference(DxfObject source, string field, string handle)
        { return new InvalidOperationException("Erasure would invalidate " + source.CodeName + " " + source.Handle + " " + field + " referencing " + handle + "."); }
    }
}
