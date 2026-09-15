using System.Linq;
using netDxf.Collections;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void PrepareLayerFilterPointerClasses(DxfClassCollection definitions)
        {
            this.PrepareStoredEnvelopeClass(definitions, "LAYER_FILTER", "AcDbLayerFilter", "ObjectDBX Classes", 0,
                this.doc.Objects.Items.Any(item => item is DxfLayerFilter));
            // Autodesk supplies the C++ name and flags, but no application name for OBJECT_PTR.
            this.PrepareStoredEnvelopeClass(definitions, "OBJECT_PTR", "CAseDLPNTableRecord", string.Empty, 1,
                this.doc.Objects.Items.Any(item => item is DxfObjectPointer));
        }

        private void PrepareStoredEnvelopeClass(DxfClassCollection definitions, string name, string cppName,
            string applicationName, int flags, bool typedPresent)
        {
            int count = this.doc.Objects.Items.Count(item => item.CodeName == name);
            if (!typedPresent)
            {
                // A compatible declaration can outlive its last typed instance after erasure.
                // Unfamiliar declarations retain their private metadata unchanged.
                if (definitions.Contains(name) && definitions[name].CppClassName == cppName && !definitions[name].IsEntity)
                    definitions[name].InstanceCount = count;
                return;
            }
            if (definitions.Contains(name))
            {
                DxfClass definition = definitions[name];
                if (definition.CppClassName != cppName || definition.IsEntity)
                    throw new System.IO.InvalidDataException("CLASS conflicts with a typed database object: " + name);
                definition.InstanceCount = count;
            }
            else definitions.Add(new DxfClass(name, cppName, applicationName)
                { ProxyFlags = flags, IsEntity = false, WasProxy = false, InstanceCount = count });
        }

        private bool WriteLayerFilterPointerPayload(DxfDatabaseObject item)
        {
            if (item is DxfLayerFilter filter)
            {
                this.chunk.Write(100, "AcDbFilter");
                this.chunk.Write(100, "AcDbLayerFilter");
                foreach (string name in filter.LayerNames) this.chunk.Write(8, this.EncodeDatabaseString(name));
                return true;
            }
            return item is DxfObjectPointer; // No public subclass or pointer payload.
        }
    }
}
