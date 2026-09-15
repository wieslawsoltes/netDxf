// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using netDxf.Entities;
using netDxf.Header;

namespace netDxf.Tables
{
    public partial class View
    {
        private Section liveSection;
        private bool hasStoredLiveSection;

        /// <summary>Gets or sets the non-owning live section reference (group 334, R2007 and later).</summary>
        /// <remarks>A registered view requires an already registered section in the same document. Setting null retains an explicit null field. Clones retain the target identity; replace it explicitly before cross-document adoption. This property does not activate or regenerate a section.</remarks>
        public Section LiveSection
        {
            get { return this.liveSection; }
            set
            {
                if (value != null && value.IsErased) throw new ArgumentException("An erased section cannot be referenced.", nameof(value));
                if (this.Owner != null) ValidateLiveSectionTarget(this.Owner.Owner, value, true);
                this.liveSection = value;
                this.hasStoredLiveSection = true;
            }
        }

        /// <summary>Gets whether group 334 is present, including an explicitly null reference.</summary>
        public bool HasStoredLiveSection { get { return this.hasStoredLiveSection; } }

        /// <summary>Clears the live section reference and omits group 334 from output.</summary>
        public void ClearLiveSectionReference()
        {
            this.liveSection = null;
            this.hasStoredLiveSection = false;
        }

        internal void ValidateLiveSection(DxfDocument document)
        { ValidateLiveSectionTarget(document, this.liveSection, this.hasStoredLiveSection); }

        private static void ValidateLiveSectionTarget(DxfDocument document, Section target, bool present)
        {
            if (present && document.DrawingVariables.AcadVer < DxfVersion.AutoCad2007)
                throw new NotSupportedException("A VIEW live section reference requires R2007 or later.");
            if (target != null && (target.IsErased || target.Handle == null || target.Owner == null
                || target.Owner.Record.Owner != document.Blocks || !target.Owner.Entities.Contains(target)
                || !ReferenceEquals(document.GetObjectByHandle(target.Handle), target)))
                throw new ArgumentException("A live section must already be registered in the same document.", nameof(target));
        }
    }
}
