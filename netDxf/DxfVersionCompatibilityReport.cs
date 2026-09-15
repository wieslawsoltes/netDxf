// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Header;

namespace netDxf
{
    /// <summary>The known writer behavior identified by a target-version diagnostic.</summary>
    public enum DxfVersionCompatibilityKind
    {
        /// <summary>An existing writer guard rejects this stored feature for the target profile.</summary>
        WriterRejection,
        /// <summary>The current writer omits this stored property for the target profile.</summary>
        DataOmission
    }

    /// <summary>A captured description of one known target-version writer restriction or omission.</summary>
    /// <remarks>SourceObject is intentionally a live reference. All other properties are immutable
    /// snapshots and do not change when the document is subsequently edited.</remarks>
    public sealed class DxfVersionCompatibilityDiagnostic
    {
        internal DxfVersionCompatibilityDiagnostic(string code, DxfVersionCompatibilityKind kind,
            object source, string sourceCodeName, string propertyPath, string message)
        {
            this.Code = code; this.Kind = kind; this.SourceObject = source;
            this.SourceHandle = (source as DxfObject)?.Handle;
            this.SourceCodeName = sourceCodeName;
            this.PropertyPath = propertyPath; this.Message = message;
        }
        /// <summary>Gets a stable machine-readable rule identifier.</summary>
        public string Code { get; }
        /// <summary>Gets whether the known behavior is a writer rejection or a data omission.</summary>
        public DxfVersionCompatibilityKind Kind { get; }
        /// <summary>Gets the actual inspected DxfObject, HeaderVariables or DxfClass instance.</summary>
        /// <remarks>This reference remains live; no object or metadata graph is cloned.</remarks>
        public object SourceObject { get; }
        /// <summary>Gets the object handle captured during analysis, or null for header/class data.</summary>
        public string SourceHandle { get; }
        /// <summary>Gets the captured DXF code name, or HEADER/CLASS for those non-object sources.</summary>
        public string SourceCodeName { get; }
        /// <summary>Gets the public property path, including ordered collection indices when needed.</summary>
        public string PropertyPath { get; }
        /// <summary>Gets the captured explanation of the current writer behavior.</summary>
        public string Message { get; }
    }

    /// <summary>An immutable snapshot of known writer restrictions for a proposed DXF version.</summary>
    /// <remarks>This bounded report is not a complete validity check or a guarantee that Save succeeds.
    /// It does not validate arbitrary opaque data, geometry, resource registration, transport encoding,
    /// application evaluation or native CAD acceptance. Re-run analysis after editing the document.</remarks>
    public sealed class DxfVersionCompatibilityReport
    {
        internal DxfVersionCompatibilityReport(DxfVersion source, DxfVersion target,
            IEnumerable<DxfVersionCompatibilityDiagnostic> diagnostics)
        {
            this.SourceVersion = source; this.TargetVersion = target;
            this.Diagnostics = diagnostics.OrderBy(item => item.SourceHandle ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(item => item.SourceCodeName, StringComparer.Ordinal)
                .ThenBy(item => item.PropertyPath, StringComparer.Ordinal)
                .ThenBy(item => item.Code, StringComparer.Ordinal).ToList().AsReadOnly();
            this.HasKnownRejections = this.Diagnostics.Any(item => item.Kind == DxfVersionCompatibilityKind.WriterRejection);
            this.HasKnownLosses = this.Diagnostics.Any(item => item.Kind == DxfVersionCompatibilityKind.DataOmission);
        }
        /// <summary>Gets the document's current version captured without modifying it.</summary>
        public DxfVersion SourceVersion { get; }
        /// <summary>Gets the proposed target version inspected by this report.</summary>
        public DxfVersion TargetVersion { get; }
        /// <summary>Gets the ordered, immutable diagnostic snapshot.</summary>
        public IReadOnlyList<DxfVersionCompatibilityDiagnostic> Diagnostics { get; }
        /// <summary>Gets whether this report found a known version-dependent writer rejection.</summary>
        public bool HasKnownRejections { get; }
        /// <summary>Gets whether this report found a known version-dependent data omission.</summary>
        public bool HasKnownLosses { get; }
    }
}
