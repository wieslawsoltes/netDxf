// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly List<DxfStoredDimAssoc> storedDimAssocs = new List<DxfStoredDimAssoc>();

        private DatabaseRecord ReadStoredDimAssocRecord(List<DxfTag> tags)
        {
            DatabaseRecord record = this.ReadStoredObjectHeader(tags, out List<DxfTag> opaque, out int start, out string handle);
            int end = tags.FindIndex(start, tag => tag.Code == 1001);
            if (end < 0) end = tags.Count;
            var body = tags.GetRange(start, end - start);
            if (body.Count == 0 || body[0].Code != 100) throw new FormatException("DIMASSOC requires its subclass marker.");
            bool unknown = opaque.Count != 0 || (string)body[0].Value != "AcDbDimAssoc";
            int mainHandles = 0;
            foreach (DxfTag tag in body.Skip(1))
            {
                if (tag.Code == 1) { mainHandles = 0; if ((string)tag.Value != "AcDbOsnapPointRef") unknown = true; }
                else if (tag.Code == 331) { if (++mainHandles > 1) unknown = true; }
                else if (tag.Code == 72 && (short)tag.Value != 1 && (short)tag.Value != 3 && (short)tag.Value != 13) unknown = true;
                else if (tag.Code == 75 && (short)tag.Value != 0) unknown = true;
                else if (!new short[] { 330, 90, 70, 71, 72, 73, 91, 40, 10, 20, 30, 75 }.Contains(tag.Code)) unknown = true;
            }
            if (unknown)
            {
                opaque.AddRange(tags.Skip(start));
                record.Object = new DxfOpaqueObject("DIMASSOC", opaque) { Handle = handle };
                return record;
            }
            int index = 1;
            Func<short, DxfTag> take = code =>
            {
                if (index >= body.Count || body[index].Code != code) throw new FormatException("DIMASSOC has a missing, duplicate or misplaced group " + code + ".");
                return body[index++];
            };
            string dimension = (string)take(330).Value;
            int mask = (int)take(90).Value;
            short trans = (short)take(70).Value, rotated = (short)take(71).Value;
            if (mask < 0 || mask > 15 || trans < 0 || trans > 1 || rotated < 0 || rotated > 1)
                throw new FormatException("DIMASSOC contains an invalid mask, trans-space flag or rotated type.");
            var points = new List<DxfStoredDimAssocPoint>();
            for (int slot = 0; slot < 4; slot++)
            {
                if ((mask & (1 << slot)) == 0) continue;
                take(1);
                short osnap = (short)take(72).Value;
                string geometry = (string)take(331).Value;
                short subentity = (short)take(73).Value;
                int marker = (int)take(91).Value;
                double parameter = (double)take(40).Value;
                var point = new Vector3((double)take(10).Value, (double)take(20).Value, (double)take(30).Value);
                take(75);
                points.Add(new DxfStoredDimAssocPoint(slot, osnap, geometry, subentity, marker, parameter, point));
            }
            if (index != body.Count) throw new FormatException("DIMASSOC point-reference count does not match its mask.");
            var association = new DxfStoredDimAssoc(this.doc, body, dimension, mask, trans, rotated, points) { Handle = handle };
            record.Object = association;
            if (end < tags.Count) this.ReadDatabaseXData(association, tags, end);
            this.storedDimAssocs.Add(association);
            return record;
        }
        private void ResolveStoredDimAssocReferences()
        {
            foreach (DxfStoredDimAssoc association in this.storedDimAssocs) association.Resolve(handle => this.GetObjectBySourceHandle(handle));
        }
    }
}
