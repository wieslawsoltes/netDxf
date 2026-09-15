using System.Collections.Generic;
using System.Linq;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        internal bool SectionReferencesRemoval(HashSet<DxfObject> removed)
        {
            // Ordinary collection removal cannot orphan a section's registered ownership graph.
            foreach (Section section in removed.OfType<Section>())
                if (this.Objects.Items.Any(item => DxfObjectDatabase.IsAncestor(section, item))) return true;
            foreach (DxfSectionSettings settings in this.Objects.Items.OfType<DxfSectionSettings>())
                if (!removed.Contains(settings) && settings.DatabaseReferences.Any(target => target != null && removed.Contains(target))) return true;
            var sections = new HashSet<DxfObject>(removed.OfType<Section>(), new MetadataIdentityComparer());
            if (sections.Count == 0) return false;
            if (this.Views.Any(view => !removed.Contains(view) && view.LiveSection != null && sections.Contains(view.LiveSection))) return true;
            foreach (DxfObject item in this.RetainedMetadataObjects())
            {
                if (removed.Contains(item)) continue;
                if (item.PersistentReactors.Any(sections.Contains)) return true;
                if (item is EntityObject entity && entity.Reactors.Any(sections.Contains)) return true;
                foreach (XData data in item.XData.Values) foreach (XDataRecord tag in data.XDataRecord)
                    if (tag.Code == XDataCode.DatabaseHandle && sections.Contains(this.GetObjectByHandle((string)tag.Value))) return true;
                if (item is DxfDatabaseObject databaseObject && databaseObject.DatabaseReferences.Any(sections.Contains)) return true;
                if (item is DxfXRecord record)
                    foreach (DxfTag tag in record.Data)
                        if (DxfObjectDatabase.IsReference(tag) && sections.Contains(this.GetObjectByHandle((string)tag.Value))) return true;
                if (item is DxfOpaqueObject opaque)
                    foreach (DxfTag tag in opaque.Tags)
                        if (tag.ValueType == DxfTagValueType.Handle && sections.Contains(this.GetObjectByHandle((string)tag.Value))) return true;
            }
            foreach (HeaderVariable variable in this.DrawingVariables.CustomValues())
            {
                DxfHandleKind kind = DxfGroupCode.GetHandleKind(variable.GroupCode);
                if (kind != DxfHandleKind.None && kind != DxfHandleKind.Arbitrary && variable.Value is string handle && sections.Contains(this.GetObjectByHandle(handle))) return true;
            }
            return false;
        }
    }
}
