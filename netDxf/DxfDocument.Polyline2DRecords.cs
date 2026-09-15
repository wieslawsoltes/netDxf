using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        private void RegisterStoredPolyline2DRecords(Polyline2D polyline)
        {
            if (polyline == null || !polyline.HasStoredRecords) return;
            foreach (Polyline2DRecord record in polyline.StoredRecords)
            {
                foreach (int key in record.Resources.Keys.ToList())
                {
                    if (record.Resources[key] is Layer layer) record.Resources[key] = this.Layers.Add(layer);
                    else if (record.Resources[key] is Linetype linetype) record.Resources[key] = this.Linetypes.Add(linetype);
                }
                if (record.Handle == null) this.NumHandles = record.AssignHandle(this.NumHandles);
                record.SourceDocument = this;
                this.AddedObjects.Add(record.Handle, record);
            }
            polyline.BindStoredRecordDocument(this);
        }
        private void UnregisterStoredPolyline2DRecords(Polyline2D polyline)
        {
            if (polyline == null) return;
            foreach (Polyline2DRecord record in polyline.StoredRecords) this.AddedObjects.Remove(record.Handle);
        }
    }
}
