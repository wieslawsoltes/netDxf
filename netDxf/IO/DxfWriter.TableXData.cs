// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Blocks;
using netDxf.Tables;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void WriteBlockRecordXData(BlockRecord record)
        {
            bool found = false;
            foreach (string app in record.XData.AppIds)
            {
                IList<XDataRecord> records = record.XData[app].XDataRecord;
                if (string.Equals(app, ApplicationRegistry.DefaultName, StringComparison.OrdinalIgnoreCase))
                {
                    records = BlockRecordXData.WithUnits(records, (short)record.Units);
                    found = true;
                }
                this.WriteXDataRecords(app, records);
            }
            if (!found)
                this.WriteXDataRecords(ApplicationRegistry.DefaultName, BlockRecordXData.WithUnits(null, (short)record.Units));
        }

        private static IList<XDataRecord> LayerDescriptionRecords(IList<XDataRecord> original, string description)
        {
            var records = original == null ? new List<XDataRecord>() : new List<XDataRecord>(original);
            int slot = records.FindLastIndex(record => record.Code == XDataCode.String);
            if (slot < 0)
            {
                // Preserve the conventional empty first string for newly authored payloads.
                records.Add(new XDataRecord(XDataCode.String, string.Empty));
                records.Add(new XDataRecord(XDataCode.String, description));
            }
            else if (!string.Equals((string)records[slot].Value, description, StringComparison.Ordinal))
                records[slot] = new XDataRecord(XDataCode.String, description);
            return records;
        }

        private static IList<XDataRecord> LayerTransparencyRecords(IList<XDataRecord> original, int alpha)
        {
            var records = original == null ? new List<XDataRecord>() : new List<XDataRecord>(original);
            int slot = records.FindLastIndex(record => record.Code == XDataCode.Int32);
            if (slot < 0) records.Add(new XDataRecord(XDataCode.Int32, alpha));
            else if ((int)records[slot].Value != alpha) records[slot] = new XDataRecord(XDataCode.Int32, alpha);
            return records;
        }

        private void WriteLayerXData(Layer layer)
        {
            const string descriptionApp = "AcAecLayerStandard", transparencyApp = "AcCmTransparency";
            bool descriptionFound = false, transparencyFound = false;
            bool writeDescription = layer.HasDescriptionAssignment || !string.IsNullOrEmpty(layer.Description);
            // Keep the existing alpha-presence and opaque/raw-value policy unchanged.
            bool writeTransparency = layer.Transparency.Value >= 0 &&
                (layer.Transparency.StoredAlphaValue.HasValue || layer.Transparency.Value > 0 ||
                (layer.Transparency.HasValueEdit || layer.HasTransparencyAssignment) && layer.XData.ContainsAppId(transparencyApp));

            foreach (string app in layer.XData.AppIds)
            {
                IList<XDataRecord> records = layer.XData[app].XDataRecord;
                if (string.Equals(app, descriptionApp, StringComparison.OrdinalIgnoreCase))
                {
                    if (writeDescription) records = LayerDescriptionRecords(records, layer.Description);
                    descriptionFound = true;
                }
                else if (string.Equals(app, transparencyApp, StringComparison.OrdinalIgnoreCase))
                {
                    if (writeTransparency) records = LayerTransparencyRecords(records, Transparency.ToAlphaValue(layer.Transparency));
                    transparencyFound = true;
                }
                this.WriteXDataRecords(app, records);
            }
            if (!descriptionFound && !string.IsNullOrEmpty(layer.Description))
                this.WriteXDataRecords(descriptionApp, LayerDescriptionRecords(null, layer.Description));
            if (!transparencyFound && writeTransparency)
                this.WriteXDataRecords(transparencyApp, LayerTransparencyRecords(null, Transparency.ToAlphaValue(layer.Transparency)));
        }
    }
}
