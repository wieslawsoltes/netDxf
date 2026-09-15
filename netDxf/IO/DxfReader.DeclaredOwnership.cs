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
                if (record.IsCompositeTableRoundtripRecord)
                {
                    try
                    {
                        record.BindCompositeTableRoundtripChildren(
                            this.GetObjectBySourceHandle((string)record.Data[1].Value) as DxfDatabaseObject,
                            this.GetObjectBySourceHandle((string)record.Data[9].Value) as DxfDatabaseObject,
                            this.GetObjectBySourceHandle((string)record.Data[14].Value) as DxfDataTable);
                    }
                    catch (ArgumentException error) { throw new FormatException("Invalid composite TABLE ownership record: " + record.Handle, error); }
                    continue;
                }
                // Unknown composite layouts retain their complete unbound payload.
                if (record.Data.Skip(1).Any(tag => tag.Code == 102)) continue;
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
