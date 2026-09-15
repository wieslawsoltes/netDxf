// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly List<Tuple<DxfStoredField, List<string>, List<string>>> storedFields = new List<Tuple<DxfStoredField, List<string>, List<string>>>();
        private DatabaseRecord ReadStoredFieldRecord(string codeName, List<DxfTag> tags)
        {
            DatabaseRecord result = this.ReadStoredObjectHeader(tags, out List<DxfTag> opaque, out int start, out string handle);
            int end = tags.FindIndex(start, tag => tag.Code == 1001);
            if (end < 0) end = tags.Count;
            bool known = codeName == "FIELD" && opaque.Count == 0 && start < end && tags[start].Code == 100
                && (string)tags[start].Value == "AcDbField" && tags.Skip(start + 1).Take(end - start - 1).All(t => t.Code != 100 && t.Code != 102);
            if (known && this.TryReadStoredFieldHeader(tags, start + 1, end, out string evaluator, out string code, out List<string> children, out List<string> objects))
            {
                var field = new DxfStoredField(this.doc, tags.GetRange(start, end - start), evaluator, code);
                result.Object = field;
                if (end < tags.Count) this.ReadDatabaseXData(field, tags, end);
                this.storedFields.Add(Tuple.Create(field, children, objects));
            }
            else
            {
                opaque.AddRange(tags.Skip(start));
                result.Object = new DxfOpaqueObject(codeName, opaque);
            }
            result.Object.Handle = handle;
            return result;
        }
        private bool TryReadStoredFieldHeader(List<DxfTag> tags, int start, int end, out string evaluator, out string code, out List<string> children, out List<string> objects)
        {
            evaluator = code = null; children = new List<string>(); objects = new List<string>();
            int i = start;
            if (i >= end || tags[i].Code != 1) return false;
            evaluator = this.DecodeEncodedNonAsciiCharacters((string)tags[i++].Value);
            if (i >= end || tags[i].Code != 2) return false;
            var text = new StringBuilder((string)tags[i++].Value);
            while (i < end && tags[i].Code == 3) text.Append((string)tags[i++].Value);
            code = this.DecodeEncodedNonAsciiCharacters(text.ToString());
            if (i >= end || tags[i].Code != 90) return false;
            int count = (int)tags[i++].Value;
            while (i < end && tags[i].Code == 360) children.Add((string)tags[i++].Value);
            if (count < 0 || count != children.Count) throw new FormatException("FIELD child count differs from its leading group-360 sequence.");
            if (i >= end || tags[i].Code != 97) return false;
            count = (int)tags[i++].Value;
            while (i < end && tags[i].Code == 331) objects.Add((string)tags[i++].Value);
            if (count < 0 || count != objects.Count) throw new FormatException("FIELD object count differs from its leading group-331 sequence.");
            // Later caches repeat integer/string codes; they are retained without a value projection.
            return true;
        }
        private void ResolveStoredFields()
        {
            foreach (var item in this.storedFields) item.Item1.Resolve(item.Item2, item.Item3, handle => this.GetObjectBySourceHandle(handle));
        }
    }
}
