// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using netDxf.Header;
using netDxf.Objects;

namespace netDxf.Entities
{
    internal sealed class MLeaderField
    {
        internal readonly short Code;
        internal readonly Type Type;
        internal readonly object Default;
        internal readonly bool Reference;
        internal readonly DxfVersion MinimumVersion;
        internal MLeaderField(short code, Type type, object value, bool reference, int version)
        {
            this.Code = code; this.Type = type; this.Default = value; this.Reference = reference;
            this.MinimumVersion = version == 2010 ? DxfVersion.AutoCad2010 : version == 2013 ? DxfVersion.AutoCad2013 : DxfVersion.AutoCad2007;
        }
    }

    /// <summary>A component of the stored MULTILEADER grammar.</summary>
    /// <remarks>Each component belongs to at most one parent. Cloning copies value data while retaining referenced document objects.</remarks>
    public abstract class MLeaderData : ICloneable
    {
        private readonly Dictionary<short, object> values = new Dictionary<short, object>();
        internal object Parent;
        internal abstract MLeaderField[] Fields { get; }
        internal virtual IEnumerable<MLeaderData> Children { get { yield break; } }
        internal MLeaderField Field(short code) { return this.Fields.FirstOrDefault(f => f.Code == code); }
        internal object Value(MLeaderField field) { return this.values.TryGetValue(field.Code, out object value) ? value : field.Default; }
        internal T Get<T>(short code) { return (T)this.Value(this.Field(code)); }
        internal void Set(short code, object value)
        {
            MLeaderField field = this.Field(code) ?? throw new ArgumentException("Unknown MULTILEADER field.", nameof(code));
            if (value == null)
            {
                if (field.Default != null && !field.Reference) throw new ArgumentNullException(nameof(value));
            }
            else
            {
                if (!field.Type.IsInstanceOfType(value)) throw new ArgumentException("Invalid MULTILEADER field type.", nameof(value));
                if (value is double number) Finite(number);
                if (value is Vector3 vector) Finite(vector);
                if (value is string text && text.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
                    throw new ArgumentException("MULTILEADER strings must not contain NUL, CR or LF; use MTEXT formatting for paragraphs.", nameof(value));
                if (value is string unicode)
                    for (int i = 0; i < unicode.Length; i++)
                    {
                        if (char.IsHighSurrogate(unicode[i]))
                        { if (++i >= unicode.Length || !char.IsLowSurrogate(unicode[i])) throw new ArgumentException("MULTILEADER strings require paired UTF-16 surrogates.", nameof(value)); }
                        else if (char.IsLowSurrogate(unicode[i])) throw new ArgumentException("MULTILEADER strings require paired UTF-16 surrogates.", nameof(value));
                    }
                if (field.Reference && this.Document != null) this.Document.Objects.CheckRegistered((DxfObject)value);
            }
            this.values[code] = value;
        }
        internal DxfDocument Document
        {
            get
            {
                object current = this.Parent;
                while (current is MLeaderData data) current = data.Parent;
                if (current is DxfDatabaseObject item) return item.Database?.Document;
                if (current is MultiLeader leader) return RegisteredDocument(leader);
                return null;
            }
        }
        internal static DxfDocument RegisteredDocument(DxfObject item)
        {
            DxfObject current = item;
            while (current != null && !(current is DxfDocument)) current = current.Owner;
            DxfDocument document = current as DxfDocument;
            return document != null && ReferenceEquals(document.GetObjectByHandle(item.Handle), item) ? document : null;
        }
        internal IEnumerable<MLeaderData> Tree()
        {
            yield return this;
            foreach (MLeaderData child in this.Children) foreach (MLeaderData descendant in child.Tree()) yield return descendant;
        }
        internal IEnumerable<DxfObject> References
        {
            get
            {
                foreach (MLeaderData item in this.Tree())
                    foreach (MLeaderField field in item.Fields)
                        if (field.Reference && item.Value(field) is DxfObject target) yield return target;
            }
        }
        internal void CheckDocument(DxfDocument document)
        {
            if (document == null) return;
            foreach (DxfObject target in this.References) document.Objects.CheckRegistered(target);
        }
        internal static void Finite(double value)
        { if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), "MULTILEADER coordinates and parameters must be finite."); }
        internal static void Finite(Vector3 value) { Finite(value.X); Finite(value.Y); Finite(value.Z); }
        internal static void ValidateDirection(Vector3 value)
        { Finite(value); if (value.X == 0 && value.Y == 0 && value.Z == 0) throw new InvalidOperationException("MULTILEADER directions cannot be zero."); }
        internal virtual void ValidateValues(DxfVersion version)
        {
            foreach (MLeaderData item in this.Tree())
                foreach (MLeaderField field in item.Fields)
                    if (item.Value(field) != null && version < field.MinimumVersion)
                        throw new NotSupportedException("MULTILEADER group " + field.Code + " is outside the selected qualified DXF profile.");
        }
        internal void CopyValuesTo(MLeaderData copy)
        { foreach (KeyValuePair<short, object> pair in this.values) copy.values.Add(pair.Key, pair.Value); }
        internal void MapReferences(Func<DxfObject, DxfObject> resolve)
        {
            var changes = new List<Tuple<MLeaderData, short, object>>();
            foreach (MLeaderData item in this.Tree())
                foreach (MLeaderField field in item.Fields)
                    if (field.Reference && item.Value(field) is DxfObject old)
                    {
                        DxfObject replacement = resolve(old);
                        if (replacement == null || !field.Type.IsInstanceOfType(replacement)) throw new ArgumentException("MULTILEADER reference mapping changed the required target type.", nameof(resolve));
                        if (this.Document != null) this.Document.Objects.CheckRegistered(replacement);
                        changes.Add(Tuple.Create(item, field.Code, (object)replacement));
                    }
            foreach (var change in changes) change.Item1.values[change.Item2] = change.Item3;
        }
        /// <summary>Copies this component and its value children, retaining document-object references and clearing its parent.</summary>
        public object Clone()
        {
            MLeaderData copy = (MLeaderData)Activator.CreateInstance(this.GetType());
            this.CopyValuesTo(copy);
            this.CopyChildrenTo(copy);
            return copy;
        }
        internal virtual void CopyChildrenTo(MLeaderData copy) { }
    }

    internal sealed class MLeaderChildCollection<T> : Collection<T> where T : MLeaderData
    {
        private readonly MLeaderData owner;
        internal MLeaderChildCollection(MLeaderData owner) { this.owner = owner; }
        private void Check(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (item.Parent != null) throw new ArgumentException("The MULTILEADER component already has a parent. Clone it before sharing.", nameof(item));
            item.CheckDocument(this.owner.Document);
        }
        protected override void InsertItem(int index, T item) { if (index < 0 || index > this.Count) throw new ArgumentOutOfRangeException(nameof(index)); this.Check(item); base.InsertItem(index, item); item.Parent = this.owner; }
        protected override void SetItem(int index, T item) { T old = this[index]; if (ReferenceEquals(old,item)) return; this.Check(item); base.SetItem(index,item); old.Parent = null; item.Parent = this.owner; }
        protected override void RemoveItem(int index) { T old=this[index]; base.RemoveItem(index); old.Parent=null; }
        protected override void ClearItems() { foreach(T item in this) item.Parent=null; base.ClearItems(); }
    }
}
