using System;
using System.Linq;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private void ResolveDeclaredOwnership()
        {
            foreach (DxfXRecord record in this.doc.Objects.Items.OfType<DxfXRecord>())
            {
                if (!record.IsTableRoundtripRecord) continue;
                DxfTag content = record.Data.FirstOrDefault(tag => tag.Code == 360);
                DxfTag geometry = record.Data.FirstOrDefault(tag => tag.Code == 361);
                try
                {
                    record.BindTableRoundtripChildren(
                        content == null ? null : this.doc.GetObjectByHandle((string)content.Value) as DxfDatabaseObject,
                        geometry == null ? null : this.doc.GetObjectByHandle((string)geometry.Value) as DxfDatabaseObject);
                }
                catch (ArgumentException error) { throw new FormatException("Invalid TABLE roundtrip ownership record: " + record.Handle, error); }
            }
        }
    }
}
