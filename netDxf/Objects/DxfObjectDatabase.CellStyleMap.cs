// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using netDxf.Tables;

namespace netDxf.Objects
{
    public sealed partial class DxfObjectDatabase
    {
        private bool creatingCellStyleMap, cellStyleMapCreationReentered;

        /// <summary>Authors a source-profile-bound CELLSTYLEMAP under an existing registered dictionary.</summary>
        /// <remarks>Definitions are enumerated and disposed before validation. Explicit STYLE/LTYPE resources must already exist in this document. Exactly one map handle is allocated after preflight. This creates public map data, not a TABLESTYLE or synchronized TABLE consumer.</remarks>
        public DxfStoredCellStyleMap CreateCellStyleMap(DxfDictionary owner, string name, IEnumerable<DxfCellStyleMapEntryDefinition> entries)
        {
            if (this.creatingCellStyleMap) { this.cellStyleMapCreationReentered = true; throw new InvalidOperationException("Map creation cannot be reentered."); }
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            this.creatingCellStyleMap = true; this.cellStyleMapCreationReentered = false;
            try
            {
                var definitions = DxfStoredCellStyleMap.MaterializeDefinitions(entries);
                if (this.cellStyleMapCreationReentered) throw new InvalidOperationException("Recursive creation invalidated the request.");
                DxfDictionary.ValidateName(name); DxfStoredTableContent.CheckEditableText(name, nameof(name));
                if (owner.IsErased || owner.Database != this || !this.IsRegistered(owner))
                    throw new ArgumentException("The map owner must be a registered dictionary in this document.", nameof(owner));
                if (owner.Contains(name) || ReferenceEquals(owner, this.Root) && IsReservedName(name))
                    throw new ArgumentException("The dictionary name already exists or is managed by the document.", nameof(name));
                var errors = this.Validate();
                if (errors.Count != 0) throw new InvalidOperationException("Cannot author a map in an invalid database: " + string.Join("; ", errors));
                DxfClass definition = this.Document.Classes.Contains("CELLSTYLEMAP") ? this.Document.Classes["CELLSTYLEMAP"] : null;
                if (definition != null && (definition.CppClassName != "AcDbCellStyleMap" || definition.IsEntity))
                    throw new InvalidOperationException("Existing CELLSTYLEMAP CLASS is incompatible.");
                var map = DxfStoredCellStyleMap.BuildDefinition(this.Document, owner, definitions);
                map.PersistentReactors.Add(owner);
                long handle = this.Document.NumHandles;
                if (handle <= 0) throw new InvalidOperationException("The document handle range is exhausted.");
                while (handle < long.MaxValue && this.Document.StoredTableHandleTarget(handle.ToString("X", CultureInfo.InvariantCulture)) != null) handle++;
                if (handle == long.MaxValue) throw new InvalidOperationException("The document handle range is exhausted.");
                map.Handle = handle.ToString("X", CultureInfo.InvariantCulture);
                // All caller code and validation completed; only internal registration and the checked dictionary slot remain.
                this.Register(map, true); owner.AddLoaded(name, map, true);
                if (owner.Owner is DxfTableStyle style && ReferenceEquals(style.ExtensionDictionary, owner) && name == "ACAD_ROUNDTRIP_2008_TABLESTYLE_CELLSTYLEMAP")
                    style.BindAuthoredCellStyleMap(map);
                return map;
            }
            finally { this.creatingCellStyleMap = false; this.cellStyleMapCreationReentered = false; }
        }
    }

    public sealed partial class DxfTableStyle
    {
        internal void BindAuthoredCellStyleMap(DxfStoredCellStyleMap map) { this.CellStyleMap = map; }
    }
}
