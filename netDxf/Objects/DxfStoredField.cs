// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using netDxf.Header;
using netDxf.IO;

namespace netDxf.Objects
{
    /// <summary>A loaded FIELD with immutable source payload and proven leading object relationships.</summary>
    /// <remarks>Evaluator code, flags and caches are stored without execution or recomputation. Only the
    /// source document and DXF version can be saved. Private evaluator references prevent graph cloning
    /// and subtree erasure. Common object metadata and XData retain their ordinary interfaces.</remarks>
    public sealed class DxfStoredField : DxfDatabaseObject
    {
        private readonly DxfDocument source;
        private readonly List<DxfStoredField> children = new List<DxfStoredField>();
        private readonly List<DxfObject> objects = new List<DxfObject>();
        private readonly List<DxfObject> references = new List<DxfObject>();
        private readonly Dictionary<string, DxfObject> identities = new Dictionary<string, DxfObject>();
        private bool resolved;

        internal DxfStoredField(DxfDocument source, IList<DxfTag> payload, string evaluator, string code)
            : base("FIELD")
        {
            this.source = source;
            this.SourceVersion = source.DrawingVariables.AcadVer;
            this.Payload = new ReadOnlyCollection<DxfTag>(new List<DxfTag>(payload));
            this.EvaluatorId = evaluator;
            this.FieldCode = code;
            this.Children = this.children.AsReadOnly();
            this.ReferencedObjects = this.objects.AsReadOnly();
            this.References = this.references.AsReadOnly();
        }
        /// <summary>Gets the original typed DXF profile; conversion requires evaluator schema knowledge.</summary>
        public DxfVersion SourceVersion { get; }
        /// <summary>Gets immutable subclass tags, excluding common identity, ownership, reactors and XData.</summary>
        /// <remarks>Tags preserve typed values; numeric lexical spelling follows the normal reader contract.</remarks>
        public IReadOnlyList<DxfTag> Payload { get; }
        /// <summary>Gets the decoded evaluator identifier from the leading group 1.</summary>
        public string EvaluatorId { get; }
        /// <summary>Gets the decoded leading group 2 and ordered group 3 continuation text without evaluating it.</summary>
        public string FieldCode { get; }
        /// <summary>Gets the ordered non-null FIELD children owned by the leading group-360 sequence.</summary>
        public IReadOnlyList<DxfStoredField> Children { get; }
        /// <summary>Gets the ordered leading group-331 targets; repeats and null references are retained.</summary>
        public IReadOnlyList<DxfObject> ReferencedObjects { get; }
        /// <summary>Gets exposed semantic payload dependencies, excluding null and arbitrary handle values.</summary>
        public IReadOnlyList<DxfObject> References { get; }
        internal static bool IsSemantic(DxfTag tag)
        { return tag.HandleKind != DxfHandleKind.None && tag.HandleKind != DxfHandleKind.Arbitrary && tag.HandleKind != DxfHandleKind.ObjectIdentity; }
        private static string Canonical(string handle)
        { return ulong.Parse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture).ToString("X", CultureInfo.InvariantCulture); }
        internal void Resolve(IList<string> children, IList<string> objects, Func<string, DxfObject> resolve)
        {
            foreach (DxfTag tag in this.Payload)
            {
                if (!IsSemantic(tag)) continue;
                string handle = Canonical((string)tag.Value);
                if (handle == "0") continue;
                DxfObject target = resolve(handle);
                if (target == null) throw new FormatException("Unresolved source FIELD dependency: " + handle);
                this.identities[handle] = target;
                this.references.Add(target);
            }
            var seen = new HashSet<DxfStoredField>();
            foreach (string handle in children)
            {
                DxfStoredField child = resolve(handle) as DxfStoredField;
                if (child == null || !ReferenceEquals(child.Owner, this) || !seen.Add(child) || DxfObjectDatabase.IsAncestor(child, this))
                    throw new FormatException("FIELD children require unique FIELD targets and reciprocal source ownership: " + handle);
                this.children.Add(child);
            }
            foreach (string handle in objects)
                this.objects.Add(Canonical(handle) == "0" ? null : resolve(handle));
            this.resolved = true;
            var errors = new List<string>();
            this.ValidateDatabaseSchema(this.Database, errors);
            if (errors.Count > 0) throw new FormatException(string.Join("; ", errors));
        }
        internal void ValidateSource(DxfDocument document)
        {
            if (!ReferenceEquals(document, this.source) || !this.resolved || this.IsErased)
                throw new InvalidOperationException("A stored FIELD requires its live source document.");
            if (document.DrawingVariables.AcadVer != this.SourceVersion)
                throw new NotSupportedException("Stored FIELD profile conversion requires its evaluator schema.");
        }
        internal override IEnumerable<DxfObject> DatabaseReferences { get { return this.references; } }
        internal override IEnumerable<DxfDatabaseObject> DeclaredOwnedObjects { get { return this.children; } }
        internal override IEnumerable<DxfTag> AllocationReservations { get { return this.Payload; } }
        internal override DxfDatabaseObject CloneShell()
        { throw new NotSupportedException("Stored FIELD cloning requires its complete private evaluator reference grammar."); }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            if (!this.resolved || database == null || !ReferenceEquals(database.Document, this.source))
            { errors.Add("Stored FIELD requires its registered source document: " + this.Handle); return; }
            if (this.source.DrawingVariables.AcadVer != this.SourceVersion) errors.Add("Stored FIELD source profile changed: " + this.Handle);
            foreach (var pair in this.identities)
                if (!ReferenceEquals(this.source.GetObjectByHandle(pair.Key), pair.Value))
                    errors.Add("Stored FIELD dependency identity changed: " + pair.Key);
            foreach (DxfDatabaseObject item in database.Items)
                if (ReferenceEquals(item.Owner, this) && !ReferenceEquals(item, this.ExtensionDictionary) && !this.children.Contains(item as DxfStoredField))
                    errors.Add("FIELD has a child outside its declared leading slots: " + item.Handle);
            foreach (DxfStoredField child in this.children)
                if (!ReferenceEquals(child.Owner, this)) errors.Add("FIELD child ownership changed: " + child.Handle);
        }
    }
}
