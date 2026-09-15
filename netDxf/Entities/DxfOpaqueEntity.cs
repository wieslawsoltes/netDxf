// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using netDxf.Blocks;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>A loaded standalone unknown entity with an immutable original tag packet.</summary>
    /// <remarks>
    /// Only the qualified common appearance and XData fields are editable. Private subclasses and
    /// application groups are inert storage. Geometry, proxy entities, aggregate sequences, hidden
    /// application dependencies, copying, owner changes and conversion between profiles are unsupported.
    /// </remarks>
    public sealed partial class DxfOpaqueEntity : EntityObject
    {
        private readonly DxfDocument source;
        private readonly ReadOnlyCollection<DxfTag> tags;
        private readonly Dictionary<int, DxfObject> links = new Dictionary<int, DxfObject>();
        private readonly Dictionary<XDataRecord, DxfObject> xdataLinks = new Dictionary<XDataRecord, DxfObject>();
        private Dictionary<short, List<DxfTag>> initialCommon;
        private List<DxfTag> initialXData;
        private Block sourceOwner;
        private DxfDictionary originalExtension;
        private DxfObject[] originalReactors;
        private DxfObject[] originalManagedReactors;
        private DxfClass sourceClass;
        private DxfClass classSnapshot;
        private bool pending = true;
        private bool retired;
        private Layout sourceLayout;
        private string sourceLayoutName;
        internal readonly HashSet<int> CommonFields = new HashSet<int>();
        internal readonly List<int> ReferenceIndices = new List<int>();
        internal readonly List<int> OwnerIndices = new List<int>();
        internal int CommonEnd;
        internal int XDataStart;
        internal string SourceOwnerHandle;

        internal DxfOpaqueEntity(DxfDocument source, string name, IList<DxfTag> tags, string handle)
            : base(EntityType.OpaqueEntity, name)
        {
            this.source = source;
            this.SourceVersion = source.DrawingVariables.AcadVer;
            this.SourceHandle = CanonicalHandle(handle);
            this.Handle = this.SourceHandle;
            this.tags = new ReadOnlyCollection<DxfTag>(new List<DxfTag>(tags));
            this.XDataStart = tags.Count;
        }

        /// <summary>Gets the captured source DXF profile; a different output profile is unsupported.</summary>
        public DxfVersion SourceVersion { get; }
        /// <summary>Gets the original physical handle, including after this object is removed.</summary>
        public string SourceHandle { get; }
        /// <summary>Gets the complete immutable original typed tag sequence, including common fields and XData.</summary>
        /// <remarks>Common edits do not change this snapshot. Tags retain values, not lexical whitespace or numeric spelling.</remarks>
        public IReadOnlyList<DxfTag> SourceTags { get { return this.tags; } }
        /// <summary>Gets actual objects referenced by qualified standard links and current common metadata.</summary>
        /// <remarks>Targets are live objects. Arbitrary handles, unknown application groups and binary payloads are not interpreted.</remarks>
        public IReadOnlyList<DxfObject> References
        {
            get
            {
                var result = new List<DxfObject>(this.links.Where(link => !this.releasedHatchReactors.Contains(link.Key)).Select(link => link.Value));
                result.Add(this.Layer); result.Add(this.Linetype);
                if (this.ExtensionDictionary != null) result.Add(this.ExtensionDictionary);
                result.AddRange(this.PersistentReactors);
                foreach (XData data in this.XData.Values)
                {
                    result.Add(data.ApplicationRegistry);
                    foreach (XDataRecord tag in data.XDataRecord)
                    { DxfObject target = this.XDataTarget(tag); if (target != null) result.Add(target); }
                }
                return result.Distinct().ToList().AsReadOnly();
            }
        }
        /// <summary>Gets the base entity UnitZ placeholder; it is not a geometric projection of the stored packet.</summary>
        /// <remarks>Only assigning the unchanged placeholder is accepted. Private extrusion and coordinate tags remain uninterpreted.</remarks>
        public override Vector3 Normal
        {
            get { return Vector3.UnitZ; }
            set { if (value != Vector3.UnitZ) throw new NotSupportedException("Unknown entity geometry cannot be edited."); }
        }
        internal static string CanonicalHandle(string value)
        { return ulong.Parse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture).ToString("X", CultureInfo.InvariantCulture); }

        internal void Resolve(Func<string, DxfObject> resolve, DxfClass definition)
        {
            if (!ReferenceEquals(resolve(this.SourceHandle), this)) throw new InvalidDataException("Unknown entity requires its exact physical source identity.");
            if (this.Owner == null || !ReferenceEquals(resolve(this.SourceOwnerHandle), this.Owner.Record))
                throw new InvalidDataException("Unknown entity requires its actual source BLOCK_RECORD owner.");
            this.sourceOwner = this.Owner;
            this.sourceLayout = this.Owner.Record.Layout;
            foreach (int index in this.CommonFields)
            {
                if (this.tags[index].Code == 67 && (short)this.tags[index].Value != (this.sourceLayout != null && this.sourceLayout.IsPaperSpace ? 1 : 0))
                    throw new InvalidDataException("Unknown entity space flag conflicts with its physical owner.");
                if (this.tags[index].Code == 410)
                {
                    this.sourceLayoutName = this.sourceLayout?.Name;
                    if (this.sourceLayoutName == null || !string.Equals(DxfObjectText.Decode((string)this.tags[index].Value), this.sourceLayoutName, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Unknown entity layout name conflicts with its physical owner.");
                }
            }
            foreach (int index in this.ReferenceIndices)
            {
                string handle = CanonicalHandle((string)this.tags[index].Value);
                if (handle == "0") continue;
                DxfObject target = resolve(handle);
                if (target == null) throw new InvalidDataException("Unresolved unknown entity source reference: " + handle);
                this.links.Add(index, target);
            }
            foreach (int index in this.OwnerIndices)
            {
                if (!this.links.TryGetValue(index, out DxfObject target)) continue;
                if (ReferenceEquals(target, this) || !ReferenceEquals(target.Owner, this))
                    throw new InvalidDataException("Unknown entity owner link requires reciprocal source ownership.");
            }
            foreach (XData data in this.XData.Values)
            {
                if (!ReferenceEquals(resolve(data.ApplicationRegistry.Handle), data.ApplicationRegistry))
                    throw new InvalidDataException("Unknown entity XData requires an actual source APPID.");
                foreach (XDataRecord tag in data.XDataRecord)
                {
                    DxfObject target = this.XDataTarget(tag);
                    if (tag.Code == XDataCode.DatabaseHandle && CanonicalHandle((string)tag.Value) == "0") continue;
                    if (tag.Code != XDataCode.DatabaseHandle && tag.Code != XDataCode.LayerName) continue;
                    if (target == null || !ReferenceEquals(resolve(target.Handle), target))
                        throw new InvalidDataException("Unknown entity XData requires an actual source target.");
                    this.xdataLinks.Add(tag, target);
                }
            }
            this.sourceClass = definition;
            this.classSnapshot = definition == null ? null : (DxfClass)definition.Clone();
            this.originalExtension = this.ExtensionDictionary;
            this.originalReactors = this.PersistentReactors.ToArray();
            this.originalManagedReactors = this.Reactors.ToArray();
            this.initialCommon = this.CommonValues();
            this.initialXData = this.XDataValues();
            this.pending = false;
            this.Validate(this.source);
        }
        internal void ValidateIncoming(DxfDocument document, Block owner = null)
        {
            if (this.retired) throw new InvalidOperationException("A removed unknown entity cannot be reattached.");
            if (document != null && !ReferenceEquals(document, this.source) || !this.pending && document == null)
                throw new InvalidOperationException("An unknown entity belongs only to its source document.");
            if (document != null && document.DrawingVariables.AcadVer != this.SourceVersion)
                throw new NotSupportedException("Unknown entity conversion between DXF profiles is unsupported.");
            if (owner != null && CanonicalHandle(owner.Record.Handle ?? "0") != this.SourceOwnerHandle)
                throw new InvalidOperationException("An unknown entity cannot change its physical block owner.");
            if (!this.pending) this.Validate(document);
        }
        internal void Validate(DxfDocument document)
        {
            if (this.pending || this.retired || !ReferenceEquals(document, this.source) || !ReferenceEquals(this.Owner, this.sourceOwner)
                || !ReferenceEquals(document.StoredTableHandleTarget(this.SourceHandle), this))
                throw new InvalidOperationException("Unknown entity source registration or owner changed.");
            if (document.DrawingVariables.AcadVer != this.SourceVersion)
                throw new NotSupportedException("Unknown entity conversion between DXF profiles is unsupported.");
            if (!ReferenceEquals(this.sourceOwner.Record.Layout, this.sourceLayout) || this.sourceLayoutName != null && this.sourceLayout.Name != this.sourceLayoutName)
                throw new NotSupportedException("Unknown entity layout changes require a common metadata mapping.");
            foreach (var link in this.links)
                if (!this.releasedHatchReactors.Contains(link.Key) && !ReferenceEquals(document.StoredTableHandleTarget(CanonicalHandle((string)this.tags[link.Key].Value)), link.Value))
                    throw new InvalidOperationException("Unknown entity dependency identity changed.");
            foreach (int index in this.OwnerIndices)
                if (this.links.TryGetValue(index, out DxfObject target) && !ReferenceEquals(target.Owner, this))
                    throw new InvalidOperationException("Unknown entity dependency ownership changed.");
            if (!ReferenceEquals(this.ExtensionDictionary, this.originalExtension) || !this.PersistentReactors.SequenceEqual(this.permittedPersistentReactors ?? this.originalReactors) || !this.Reactors.SequenceEqual(this.permittedManagedReactors ?? this.originalManagedReactors))
                throw new NotSupportedException("Unknown entity extension and reactor edits require a complete metadata mapping.");
            if (this.sourceClass != null && (!document.Classes.Contains(this.sourceClass.Name)
                || !ReferenceEquals(document.Classes[this.sourceClass.Name], this.sourceClass) || !SameClass(this.sourceClass, this.classSnapshot)))
                throw new InvalidOperationException("Unknown entity CLASS declaration changed.");
            if (this.sourceClass == null && document.Classes.Contains(this.CodeName))
                throw new InvalidOperationException("An unknown entity cannot acquire a different CLASS declaration.");
            if (!ReferenceEquals(document.GetObjectByHandle(this.Layer.Handle), this.Layer) || !ReferenceEquals(document.GetObjectByHandle(this.Linetype.Handle), this.Linetype))
                throw new InvalidOperationException("Unknown entity common resources must remain registered.");
            if (double.IsNaN(this.LinetypeScale) || double.IsInfinity(this.LinetypeScale))
                throw new InvalidOperationException("Unknown entity linetype scale must be finite.");
            if (!Enum.IsDefined(typeof(Lineweight), this.Lineweight)) throw new InvalidOperationException("Unknown entity lineweight is invalid.");
            if (this.ColorName != null && this.SourceVersion < DxfVersion.AutoCad2004 || this.ShadowMode.HasValue && this.SourceVersion < DxfVersion.AutoCad2007)
                throw new NotSupportedException("Unknown entity common metadata is unavailable in its source profile.");
            this.XDataValues(); // Validate mutable XData before writer initialization or output.
            foreach (XData data in this.XData.Values)
                foreach (XDataRecord tag in data.XDataRecord)
                {
                    if (tag.Code == XDataCode.DatabaseHandle && CanonicalHandle((string)tag.Value) == "0") continue;
                    if (tag.Code != XDataCode.DatabaseHandle && tag.Code != XDataCode.LayerName) continue;
                    DxfObject target = this.XDataTarget(tag);
                    if (target == null || !ReferenceEquals(document.StoredTableHandleTarget(target.Handle), target))
                        throw new InvalidOperationException("Unknown entity XData target is no longer registered.");
                }
        }
        private DxfObject XDataTarget(XDataRecord record)
        {
            if (record == null) return null;
            if (this.xdataLinks.TryGetValue(record, out DxfObject target)) return target;
            if (record.Code == XDataCode.DatabaseHandle) return this.source.StoredTableHandleTarget(CanonicalHandle((string)record.Value));
            if (record.Code == XDataCode.LayerName && this.source.Layers.TryGetValue((string)record.Value, out Layer layer)) return layer;
            return null;
        }
        private static bool SameClass(DxfClass a, DxfClass b)
        { return a.Name == b.Name && a.CppClassName == b.CppClassName && a.ApplicationName == b.ApplicationName && a.ProxyFlags == b.ProxyFlags && a.InstanceCount == b.InstanceCount && a.WasProxy == b.WasProxy && a.IsEntity == b.IsEntity; }
        internal void ValidatePreparedClass(netDxf.Collections.DxfClassCollection definitions)
        {
            if (this.classSnapshot != null && (!definitions.Contains(this.CodeName) || !SameClass(definitions[this.CodeName], this.classSnapshot)))
                throw new NotSupportedException("Generated CLASS output would change an unknown entity declaration.");
            if (this.classSnapshot != null && this.SourceVersion == DxfVersion.AutoCad2000 && this.classSnapshot.InstanceCount.HasValue)
                throw new NotSupportedException("DXF 2000 output would omit the unknown entity CLASS instance count.");
        }
        internal static void RejectBlockGeometry(Block root)
        {
            var visited = new HashSet<Block>();
            Action<Block> visit = null;
            visit = block =>
            {
                if (block == null || !visited.Add(block)) return;
                foreach (EntityObject entity in block.Entities)
                {
                    if (entity is DxfOpaqueEntity) throw new NotSupportedException("Unknown entity block geometry requires its complete application schema.");
                    if (entity is Insert insert) visit(insert.Block);
                    if (entity is Dimension dimension) visit(dimension.Block);
                }
            };
            visit(root);
        }
        internal void MarkRemoved() { this.retired = true; }
        /// <summary>Rejects copying private entity data without its application schema.</summary>
        public override object Clone() { throw new NotSupportedException("Unknown entity cloning requires its complete application schema."); }
        /// <summary>Accepts only the exact identity transformation.</summary>
        public override void TransformBy(Matrix3 transformation, Vector3 translation)
        {
            for (int row = 0; row < 3; row++) for (int column = 0; column < 3; column++)
                if (transformation[row, column] != (row == column ? 1.0 : 0.0)) throw new NotSupportedException("Unknown entity transforms require its application schema.");
            if (translation != Vector3.Zero) throw new NotSupportedException("Unknown entity transforms require its application schema.");
        }
        /// <summary>Accepts only the exact four-by-four identity transformation.</summary>
        public override void TransformBy(Matrix4 transformation)
        {
            for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++)
                if (transformation[row, column] != (row == column ? 1.0 : 0.0)) throw new NotSupportedException("Unknown entity transforms require its application schema.");
        }

        private Dictionary<short, List<DxfTag>> CommonValues()
        {
            var result = new Dictionary<short, List<DxfTag>>();
            Action<short, object> add = (code, value) => result[code] = value == null ? new List<DxfTag>() : new List<DxfTag> { new DxfTag(code, value) };
            add(8, this.Layer.Name); add(6, this.Linetype.Name);
            add(62, this.Color.Index); add(420, this.Color.UseTrueColor ? (object)AciColor.ToTrueColor(this.Color) : null);
            add(370, (short)this.Lineweight); add(48, this.LinetypeScale); add(60, (short)(this.IsVisible ? 0 : 1));
            add(440, this.Transparency.StoredAlphaValue ?? Transparency.ToAlphaValue(this.Transparency));
            add(430, this.ColorName); add(284, this.ShadowMode.HasValue ? (object)(short)this.ShadowMode.Value : null);
            var graphics = new List<DxfTag>(); byte[] bytes = this.ProxyGraphics;
            if (bytes != null)
            {
                graphics.Add(this.SourceVersion >= DxfVersion.AutoCad2013 ? new DxfTag(160, (long)bytes.Length) : new DxfTag(92, bytes.Length));
                for (int offset = 0; offset < bytes.Length; offset += 127) graphics.Add(new DxfTag(310, bytes.Skip(offset).Take(127).ToArray()));
            }
            result[92] = graphics;
            return result;
        }
        private List<DxfTag> XDataValues()
        {
            var result = new List<DxfTag>();
            foreach (XData data in this.XData.Values)
            {
                result.Add(new DxfTag(1001, data.ApplicationRegistry.Name)); int depth = 0;
                foreach (XDataRecord record in data.XDataRecord)
                {
                    if (record == null || !Enum.IsDefined(typeof(XDataCode), record.Code) || record.Code == XDataCode.AppReg)
                        throw new InvalidOperationException("Invalid unknown entity XData record.");
                    object value = record.Code == XDataCode.LayerName && this.XDataTarget(record) is Layer layer ? layer.Name : record.Value;
                    var tag = new DxfTag((short)record.Code, value);
                    if (tag.Code == 1002)
                    {
                        if ((string)tag.Value == "{") depth++;
                        else if ((string)tag.Value != "}" || --depth < 0) throw new InvalidOperationException("Unbalanced unknown entity XData.");
                    }
                    result.Add(tag);
                }
                if (depth != 0) throw new InvalidOperationException("Unbalanced unknown entity XData.");
            }
            return result;
        }
        internal static bool SameTags(IList<DxfTag> a, IList<DxfTag> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Code != b[i].Code) return false;
                if (a[i].Value is byte[] bytes) { if (!(b[i].Value is byte[] other) || !bytes.SequenceEqual(other)) return false; }
                else if (!Equals(a[i].Value, b[i].Value)) return false;
            }
            return true;
        }
        internal List<DxfTag> OutputTags(Func<string, string> encode)
        {
            var common = this.CommonValues();
            var changed = new HashSet<short>(common.Keys.Where(code => !SameTags(common[code], this.initialCommon[code])));
            // ACI/true-color form one editable field; update both when either component changes.
            if (changed.Contains(62) || changed.Contains(420)) { changed.Add(62); changed.Add(420); }
            var emitted = new HashSet<short>(); var result = new List<DxfTag>();
            Action<DxfTag> add = tag => result.Add(tag.ValueType == DxfTagValueType.String && tag.Code != 1002 ? new DxfTag(tag.Code, encode((string)tag.Value)) : tag);
            Action<short> emit = code => { if (emitted.Add(code)) foreach (DxfTag tag in common[code]) add(tag); };
            for (int i = 0; i < this.XDataStart; i++)
            {
                if (this.releasedHatchReactors.Contains(i)) continue;
                if (i == this.CommonEnd) foreach (short code in common.Keys) if (changed.Contains(code)) emit(code);
                short key = this.tags[i].Code == 160 || this.tags[i].Code == 310 ? (short)92 : this.tags[i].Code;
                if (this.CommonFields.Contains(i) && changed.Contains(key)) emit(key);
                else result.Add(this.tags[i]);
            }
            List<DxfTag> xdata = this.XDataValues();
            if (SameTags(xdata, this.initialXData)) result.AddRange(this.tags.Skip(this.XDataStart));
            else
            {
                foreach (DxfTag tag in xdata) add(tag);
                result.AddRange(this.tags.Skip(this.XDataStart).Where(tag => tag.Code == 999));
            }
            return result;
        }
    }
}
