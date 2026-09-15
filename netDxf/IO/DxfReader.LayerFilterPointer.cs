using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private DatabaseRecord ReadStoredObjectHeader(List<DxfTag> tags, out List<DxfTag> opaque, out int payload, out string handle)
        {
            var result = new DatabaseRecord();
            opaque = new List<DxfTag>();
            handle = null;
            bool reactorsSeen = false;
            bool extensionSeen = false;
            payload = 0;
            for (; payload < tags.Count; payload++)
            {
                DxfTag tag = tags[payload];
                if (tag.Code == 100 || tag.Code == 1001) break;
                if (tag.Code == 5)
                {
                    if (handle != null) throw new FormatException("Duplicate object identity.");
                    handle = (string)tag.Value;
                }
                else if (tag.Code == 330)
                {
                    string value = (string)tag.Value;
                    if (result.Metadata.Owner == null) result.Metadata.Owner = value;
                    else if (string.Equals(result.Metadata.Owner, value, StringComparison.OrdinalIgnoreCase))
                        throw new FormatException("Duplicate object owner.");
                    else opaque.Add(tag); // An unfamiliar distinct handle remains private payload.
                }
                else if (tag.Code == 102)
                {
                    int start = payload;
                    string group = (string)tag.Value;
                    if (group.Length == 0 || group[0] != '{') throw new FormatException("Invalid object control-group opening.");
                    while (++payload < tags.Count && !(tags[payload].Code == 102 && (string)tags[payload].Value == "}"))
                        if (tags[payload].Code == 102 || tags[payload].Code == 100 || tags[payload].Code == 1001)
                            throw new FormatException("Invalid object control-group framing.");
                    if (payload == tags.Count) throw new FormatException("Unterminated object control group.");
                    var content = tags.GetRange(start + 1, payload - start - 1);
                    if (group == "{ACAD_REACTORS")
                    {
                        if (reactorsSeen || content.Any(value => value.Code != 330)) throw new FormatException("Invalid persistent-reactor group.");
                        reactorsSeen = true;
                        foreach (DxfTag value in content) result.Metadata.Reactors.Add((string)value.Value);
                    }
                    else if (group == "{ACAD_XDICTIONARY")
                    {
                        if (content.Count != 1 || content[0].Code != 360 || extensionSeen)
                            throw new FormatException("Invalid extension-dictionary group.");
                        extensionSeen = true;
                        result.Metadata.Extension = (string)content[0].Value;
                    }
                    else opaque.AddRange(tags.GetRange(start, payload - start + 1));
                }
                else opaque.Add(tag);
            }
            if (handle == null) throw new FormatException("A database object requires an identity.");
            return result;
        }

        // Preserve unknown header data in these small public envelopes without changing other parsers.
        private DatabaseRecord ReadLayerFilterPointerRecord(string type, List<DxfTag> tags)
        {
            DatabaseRecord result = this.ReadStoredObjectHeader(tags, out List<DxfTag> opaque, out int payload, out string handle);
            int xdata = tags.FindIndex(payload, value => value.Code == 1001);
            if (xdata < 0) xdata = tags.Count;
            bool known = opaque.Count == 0;
            if (type == "OBJECT_PTR") known &= payload == xdata;
            else known &= xdata - payload >= 2 && tags[payload].Code == 100 && (string)tags[payload].Value == "AcDbFilter"
                && tags[payload + 1].Code == 100 && (string)tags[payload + 1].Value == "AcDbLayerFilter"
                && tags.Skip(payload + 2).Take(xdata - payload - 2).All(value => value.Code == 8);
            if (known)
            {
                if (type == "OBJECT_PTR") result.Object = new DxfObjectPointer();
                else
                {
                    try
                    {
                        result.Object = new DxfLayerFilter(tags.Skip(payload + 2).Take(xdata - payload - 2)
                            .Select(value => this.DecodeEncodedNonAsciiCharacters((string)value.Value)));
                    }
                    catch (ArgumentException error) { throw new FormatException("Invalid stored layer-filter name.", error); }
                }
                if (xdata < tags.Count) this.ReadDatabaseXData(result.Object, tags, xdata);
            }
            else
            {
                opaque.AddRange(tags.Skip(payload));
                result.Object = new DxfOpaqueObject(type, opaque);
            }
            result.Object.Handle = handle;
            return result;
        }
    }
}
