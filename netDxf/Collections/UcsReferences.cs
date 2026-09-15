using System;
using System.Collections.Generic;
using netDxf.Tables;

namespace netDxf.Collections
{
    internal static class UcsReferences
    {
        internal static IEnumerable<UCS> Targets(DxfObject owner)
        {
            if (owner is UCS ucs)
            {
                if (ucs.BaseUcs != null) yield return ucs.BaseUcs;
            }
            else if (owner is View view && view.Ucs != null)
            {
                if (view.Ucs.NamedUcs != null) yield return view.Ucs.NamedUcs;
                if (view.Ucs.BaseUcs != null) yield return view.Ucs.BaseUcs;
            }
            else if (owner is VPort port)
            {
                if (port.NamedUcs != null) yield return port.NamedUcs;
                if (port.BaseUcs != null) yield return port.BaseUcs;
            }
        }
        private static DxfDocument Document(DxfObject owner)
        {
            while (owner != null && !(owner is DxfDocument)) owner = owner.Owner;
            return owner as DxfDocument;
        }
        internal static void Check(DxfObject owner, UCS target)
        {
            DxfDocument document = Document(owner);
            if (document != null) Check(document, target);
        }
        private static void Check(DxfDocument document, UCS target)
        {
            if (target != null && (target.Owner != document.UCSs || target.Handle == null || !ReferenceEquals(document.GetObjectByHandle(target.Handle), target)))
                throw new ArgumentException("A referenced UCS must already be registered in the same document.", nameof(target));
        }
        internal static void Validate(DxfObject owner, DxfDocument document)
        {
            foreach (UCS target in Targets(owner)) Check(document, target);
            if (owner is UCS ucs) ucs.ValidateOrthographicBase();
            if (owner is View view && view.Ucs != null) view.Ucs.Validate();
            if (owner is VPort port && port.BaseUcs != null && port.UcsOrthographicType == 0)
                throw new InvalidOperationException("A VPORT base UCS requires a nonzero orthographic type.");
        }
        internal static void ValidateXData(DxfObject owner, DxfDocument document)
        {
            foreach (XData data in owner.XData.Values)
            {
                var table = data.ApplicationRegistry.Owner;
                if (table != null && !ReferenceEquals(table.Owner, document))
                    throw new ArgumentException("Clone XData whose application registry belongs to another document before adding this table record.", nameof(owner));
            }
        }
        internal static void Replace(DxfObject owner, UCS previous, UCS next)
        {
            Check(owner, next);
            DxfDocument document = Document(owner);
            if (document == null || ReferenceEquals(previous, next)) return;
            if (previous != null) document.UCSs.References[previous.Name].Remove(owner);
            if (next != null) document.UCSs.References[next.Name].Add(owner);
        }
        internal static void Register(DxfObject owner)
        {
            DxfDocument document = Document(owner);
            if (document == null) return;
            foreach (UCS target in Targets(owner)) document.UCSs.References[target.Name].Add(owner);
        }
        internal static void Unregister(DxfObject owner)
        {
            DxfDocument document = Document(owner);
            if (document == null) return;
            foreach (UCS target in Targets(owner)) document.UCSs.References[target.Name].Remove(owner);
        }
    }
}
