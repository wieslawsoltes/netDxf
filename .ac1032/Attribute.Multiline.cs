// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Tables;

namespace netDxf.Entities
{
    public partial class Attribute
    {
        private AttributeMText multilineText;
        /// <summary>Gets or sets the independently placed AC1032 multiline body, or null for ordinary TEXT.</summary>
        /// <remarks>Changing this definition does not rewrite Value or other fallback TEXT fields.</remarks>
        public AttributeMText MultilineText
        {
            get { return this.multilineText; }
            set
            {
                if (ReferenceEquals(value, this.multilineText)) return;
                this.multilineText = AttributeMTextBinding.Bind(this, value, null);
                this.ClearProxyGraphics();
            }
        }
        internal void NormalizeMultiline(DxfDocument document)
        { this.multilineText = AttributeMTextBinding.Bind(this, this.multilineText, document); }
        private void CopyMultilineTo(Attribute target)
        {
            target.multilineText = this.multilineText == null ? null : this.multilineText.WithStyle(
                ReferenceEquals(this.multilineText.Style, this.Style) ? target.Style : (TextStyle)this.multilineText.Style.Clone());
        }
    }

    public partial class AttributeDefinition
    {
        private AttributeMText multilineText;
        /// <summary>Gets or sets the independently placed AC1032 multiline default, or null for ordinary TEXT.</summary>
        /// <remarks>Prompt, Value and all fallback TEXT fields remain independent. Output requires AC1032.</remarks>
        public AttributeMText MultilineText
        {
            get { return this.multilineText; }
            set
            {
                if (ReferenceEquals(value, this.multilineText)) return;
                this.multilineText = AttributeMTextBinding.Bind(this, value, null);
                this.ClearProxyGraphics();
            }
        }
        internal void NormalizeMultiline(DxfDocument document)
        { this.multilineText = AttributeMTextBinding.Bind(this, this.multilineText, document); }
        private void CopyMultilineTo(AttributeDefinition target)
        {
            target.multilineText = this.multilineText == null ? null : this.multilineText.WithStyle(
                ReferenceEquals(this.multilineText.Style, this.Style) ? target.Style : (TextStyle)this.multilineText.Style.Clone());
        }
    }

    internal static class AttributeMTextBinding
    {
        internal static DxfDocument Document(DxfObject host)
        {
            var seen = new HashSet<DxfObject>();
            for (DxfObject current = host; current != null && seen.Add(current); current = current.Owner)
                if (current is DxfDocument document) return document;
            return null;
        }
        internal static void Validate(AttributeMText value, DxfDocument document)
        {
            if (value != null && value.Style.Owner != null && !ReferenceEquals(value.Style.Owner, document == null ? null : document.TextStyles))
                throw new InvalidOperationException("Embedded MTEXT style belongs to another drawing; clone it explicitly.");
        }
        internal static AttributeMText Bind(DxfObject host, AttributeMText value, DxfDocument destination)
        {
            if (value == null) return null;
            destination = destination ?? Document(host);
            // Detached objects may reference registered resources until adoption; validate against
            // the destination before it is mutated. Attached replacements validate immediately.
            if (destination == null) return value;
            Validate(value, destination);
            return value.WithStyle(destination.TextStyles.Add(value.Style));
        }
    }
}
