using System;
using System.Collections.Generic;
using System.IO;
using netDxf.Collections;
using netDxf.Tables;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private sealed class UcsBaseContext
        {
            private int controls;
            private bool publicSubclass = true, xdata;
            internal void Observe(short code, object value)
            {
                if (code == 1001 && this.controls == 0) this.xdata = true;
                if (code == 102)
                {
                    string text = (string)value;
                    if (text.StartsWith("{", StringComparison.Ordinal)) this.controls++;
                    else if (text == "}" && this.controls > 0) this.controls--;
                }
                else if (code == 100 && this.controls == 0) this.publicSubclass = (string)value == SubclassMarker.Ucs;
            }
            internal bool IsPublic { get { return this.publicSubclass && this.controls == 0 && !this.xdata; } }
        }
        private readonly List<Tuple<UCS, string>> ucsBaseReferences = new List<Tuple<UCS, string>>();
        private void CompleteUcsBase(UCS owner, short type, string handle)
        {
            try { owner.SetLoadedOrthographicBase(type, null, handle != null); }
            catch (ArgumentException error) { throw new InvalidDataException("Invalid UCS orthographic base fields.", error); }
            if (type != 0 || handle != null) this.ucsBaseReferences.Add(Tuple.Create(owner, handle));
        }
        private void ResolveUcsBaseReferences()
        {
            foreach (var pending in this.ucsBaseReferences)
            {
                UCS owner = pending.Item1;
                if (!ReferenceEquals(this.GetObjectBySourceHandle(owner.Handle), owner)) throw new InvalidDataException("The referring UCS source record was not retained.");
                string handle = pending.Item2;
                bool empty = handle == null || handle == "0";
                UCS target = empty ? null : this.GetObjectBySourceHandle(handle) as UCS;
                if (target == null && !empty) throw new InvalidDataException("Unresolved or non-UCS source identity for group 346: " + handle);
                try { owner.SetLoadedOrthographicBase(owner.OrthographicViewType, target, handle != null); }
                catch (ArgumentException error) { throw new InvalidDataException("Invalid UCS base reference.", error); }
            }
            foreach (UCS ucs in this.doc.UCSs) UcsReferences.Validate(ucs, this.doc);
        }
    }
}
