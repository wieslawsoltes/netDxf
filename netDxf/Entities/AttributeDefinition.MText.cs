// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
namespace netDxf.Entities
{
    public partial class AttributeDefinition
    {
        internal AttributeTextState TextState;
        /// <summary>Gets whether this attribute contains DXF 2018 embedded MTEXT.</summary>
        public bool HasMText { get { return this.TextState?.Content != null; } }
        /// <summary>Gets or sets optional position locking, retaining absent versus false.</summary>
        public bool? IsPositionLocked { get { return this.TextState?.Locked; } set { (this.TextState ?? (this.TextState = new AttributeTextState())).Locked = value; } }
        /// <summary>Returns an independent embedded-content snapshot, or null. Edit and call SetMText to publish.</summary>
        public AttributeMText GetMText() { return this.TextState?.Content?.Copy(true); }
        /// <summary>Copies and replaces embedded MTEXT. Null explicitly removes it without regenerating fallback text.</summary>
        /// <remarks>Parent Value, placement and Style remain independent. Caller content is never adopted.
        /// A registered host resolves the copied embedded style to its owning document.</remarks>
        public void SetMText(AttributeMText content)
        {
            content?.Validate();
            AttributeMText next = content?.Copy(true); next?.Validate();
            DxfDocument document = this.Owner?.Record.Owner?.Owner;
            if (document != null) document.ReplaceAttributeMTextStyle(this, this.TextState?.Content, next);
            if (this.TextState == null) this.TextState = new AttributeTextState();
            this.TextState.Content = next; this.TextState.Kind = next == null ? (short?)null : (short)4;
            if (next == null) { this.TextState.SecondaryFlag = null; this.TextState.SecondaryAlignment = null; }
            this.ClearProxyGraphics();
        }
        internal void CopyAttributeTextTo(AttributeDefinition target) { target.TextState = this.TextState?.Copy(true); }
        internal AttributeTextState PrepareAttributeTextTransform(Matrix3 matrix, Vector3 translation)
        {
            if (this.TextState == null) return null;
            if (matrix == Matrix3.Identity && translation == Vector3.Zero) return this.TextState;
            if (this.TextState.Auxiliary.Count != 0) throw new NotSupportedException("Auxiliary attribute data requires an explicit transform policy.");
            var next = this.TextState.Copy(false); next.Content = this.TextState.Content?.Transformed(matrix, translation);
            if (next.SecondaryAlignment.HasValue)
            {
                Vector3 world = MathHelper.Transform(next.SecondaryAlignment.Value, this.Normal, CoordinateSystem.Object, CoordinateSystem.World);
                Vector3 normal = Vector3.NormalizeFiniteDirection(matrix * this.Normal, nameof(matrix));
                next.SecondaryAlignment = MathHelper.Transform(InfiniteLineTransform.TransformPoint(matrix, world, translation), normal, CoordinateSystem.World, CoordinateSystem.Object);
            }
            return next;
        }
    }
}
