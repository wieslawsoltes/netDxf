using System;
using System.Collections.Generic;
using System.IO;
using netDxf.Collections;
using netDxf.Tables;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly List<Tuple<DxfObject, short, string>> ucsReferences = new List<Tuple<DxfObject, short, string>>();
        private void AddUcsReference(DxfObject owner, short code, string handle)
        {
            if (handle != null && handle != "0") this.ucsReferences.Add(Tuple.Create(owner, code, handle));
        }
        private void ResolveUcsReferences()
        {
            this.ResolveUcsBaseReferences();
            foreach (var reference in this.ucsReferences)
            {
                UCS target = this.doc.GetObjectByHandle(reference.Item3) as UCS;
                if (target == null) throw new InvalidDataException("Unresolved or non-UCS target for group " + reference.Item2 + ": " + reference.Item3);
                if (reference.Item1 is View view)
                {
                    if (reference.Item2 == 345) view.Ucs.NamedUcs = target;
                    else view.Ucs.BaseUcs = target;
                }
                else if (reference.Item1 is VPort port)
                {
                    if (reference.Item2 == 345) port.NamedUcs = target;
                    else port.BaseUcs = target;
                }
            }
            try
            {
                foreach (View view in this.doc.Views) UcsReferences.Validate(view, this.doc);
                foreach (VPort port in this.doc.VPorts.Records) UcsReferences.Validate(port, this.doc);
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException)
            { throw new InvalidDataException("Invalid associated UCS relationship.", error); }
        }

        private sealed class ViewUcsInput
        {
            internal bool Enabled;
            internal readonly HashSet<short> Seen = new HashSet<short>();
            internal Vector3 Origin, XAxis = Vector3.UnitX, YAxis = Vector3.UnitY;
            internal short Type;
            internal double Elevation;
            internal string Named, Base;
        }
        private bool TryReadViewUcs(ViewUcsInput input)
        {
            short code = this.chunk.Code;
            switch (code)
            {
                case 72: case 110: case 120: case 130: case 111: case 121: case 131:
                case 112: case 122: case 132: case 79: case 146: case 345: case 346: break;
                default: return false;
            }
            if (!input.Seen.Add(code)) throw new InvalidDataException("Duplicate VIEW UCS group " + code + ".");
            switch (code)
            {
                case 72:
                    short enabled = this.chunk.ReadShort();
                    if (enabled != 0 && enabled != 1) throw new InvalidDataException("VIEW group 72 must be zero or one.");
                    input.Enabled = enabled == 1; break;
                case 110: input.Origin.X = this.chunk.ReadDouble(); break;
                case 120: input.Origin.Y = this.chunk.ReadDouble(); break;
                case 130: input.Origin.Z = this.chunk.ReadDouble(); break;
                case 111: input.XAxis.X = this.chunk.ReadDouble(); break;
                case 121: input.XAxis.Y = this.chunk.ReadDouble(); break;
                case 131: input.XAxis.Z = this.chunk.ReadDouble(); break;
                case 112: input.YAxis.X = this.chunk.ReadDouble(); break;
                case 122: input.YAxis.Y = this.chunk.ReadDouble(); break;
                case 132: input.YAxis.Z = this.chunk.ReadDouble(); break;
                case 79: input.Type = this.chunk.ReadShort(); break;
                case 146: input.Elevation = this.chunk.ReadDouble(); break;
                case 345: input.Named = this.chunk.ReadHex(); break;
                case 346: input.Base = this.chunk.ReadHex(); break;
            }
            this.chunk.Next(); return true;
        }
        private void CompleteViewUcs(View view, ViewUcsInput input)
        {
            if (!input.Enabled)
            {
                if (input.Seen.Count > (input.Seen.Contains(72) ? 1 : 0))
                    throw new InvalidDataException("Associated VIEW UCS fields require group 72 equal to one.");
                return;
            }
            RequireVPortPoint(input.Seen, 110, 3); RequireVPortPoint(input.Seen, 111, 3); RequireVPortPoint(input.Seen, 112, 3);
            try
            {
                view.Ucs = new ViewUcs { Origin = input.Origin, XAxis = input.XAxis, YAxis = input.YAxis,
                    OrthographicType = input.Type, Elevation = input.Elevation };
            }
            catch (ArgumentException error) { throw new InvalidDataException("Invalid associated VIEW UCS values.", error); }
            this.AddUcsReference(view, 345, input.Named); this.AddUcsReference(view, 346, input.Base);
        }
    }

    internal sealed partial class DxfWriter
    {
        private void ValidateUcsReferences()
        {
            foreach (UCS ucs in this.doc.UCSs) UcsReferences.Validate(ucs, this.doc);
            foreach (View view in this.doc.Views)
            {
                UcsReferences.Validate(view, this.doc);
                view.ValidateLiveSection(this.doc);
            }
            foreach (VPort port in this.doc.VPorts.Records) UcsReferences.Validate(port, this.doc);
        }
        private void WriteViewUcs(ViewUcs value)
        {
            this.chunk.Write(72, value == null ? (short)0 : (short)1);
            if (value == null) return;
            this.chunk.Write(110, value.Origin.X); this.chunk.Write(120, value.Origin.Y); this.chunk.Write(130, value.Origin.Z);
            this.chunk.Write(111, value.XAxis.X); this.chunk.Write(121, value.XAxis.Y); this.chunk.Write(131, value.XAxis.Z);
            this.chunk.Write(112, value.YAxis.X); this.chunk.Write(122, value.YAxis.Y); this.chunk.Write(132, value.YAxis.Z);
            this.chunk.Write(79, value.OrthographicType); this.chunk.Write(146, value.Elevation);
            if (value.NamedUcs != null) this.chunk.Write(345, value.NamedUcs.Handle);
            if (value.BaseUcs != null) this.chunk.Write(346, value.BaseUcs.Handle);
        }
    }
}
