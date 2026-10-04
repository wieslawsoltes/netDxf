// Copyright (c) netDxf contributors. Licensed under the MIT License.
namespace netDxf.Entities
{
    public partial class Attribute
    {
        internal Attribute PrepareInsertTransform(Matrix3 matrix, Vector3 translation, bool translationOnly)
        {
            // Numeric-only carrier: unlike MemberwiseClone it cannot share common
            // proxy data, and unlike Clone it does not clone resources/definitions.
            var next = new Attribute(this.tag)
            {
                Owner = this.Owner?.Owner?.Record.Owner?.Owner == null ? null : this.Owner,
                position = this.position, normal = this.normal, height = this.height,
                width = this.width, widthFactor = this.widthFactor, obliqueAngle = this.obliqueAngle,
                rotation = this.rotation, alignment = this.alignment,
                TextState = this.TextState?.Copy(false),
                isBackward = this.isBackward, isUpsideDown = this.isUpsideDown
            };
            next.CheckInsertGeometry();
            if (translationOnly) { next.TextState = this.PrepareAttributeTextTransform(matrix, translation); next.position = InfiniteLineTransform.TransformPoint(matrix, next.position, translation); }
            else next.TransformBy(matrix, translation);
            next.CheckInsertGeometry();
            return next;
        }

        internal Attribute PrepareDefinitionTransform(AttributeDefinition definition, Matrix3 matrix, Vector3 translation)
        {
            Attribute next = this.PrepareInsertTransform(Matrix3.Identity, Vector3.Zero, true);
            next.TextState = this.TextState?.Copy(false);
            next.position = definition.Position; next.normal = definition.Normal;
            next.height = definition.Height; next.width = definition.Width; next.widthFactor = definition.WidthFactor;
            next.obliqueAngle = definition.ObliqueAngle; next.rotation = definition.Rotation;
            next.isBackward = definition.IsBackward; next.isUpsideDown = definition.IsUpsideDown;
            if(next.TextState?.Content != null && definition.TextState?.Content != null)
                next.TextState.Content.CopyLayoutFrom(definition.TextState.Content);
            return next.PrepareInsertTransform(matrix, translation, matrix == Matrix3.Identity);
        }
        private void CheckInsertGeometry()
        {
            Insert.FiniteInsert(this.position); Insert.FiniteInsert(this.normal);
            Insert.FiniteInsert(this.height); Insert.FiniteInsert(this.width); Insert.FiniteInsert(this.widthFactor);
            Insert.FiniteInsert(this.obliqueAngle); Insert.FiniteInsert(this.rotation);
        }

        // All validation and calculations precede this non-throwing field copy.
        // Preserve live attribute identity, ownership, metadata and resources.
        internal bool CommitInsertTransform(Attribute next)
        {
            bool changed = !Insert.SameInsert(this.position, next.position) || !Insert.SameInsert(this.normal, next.normal)
                || !Insert.SameInsert(this.height, next.height) || !Insert.SameInsert(this.width, next.width)
                || !Insert.SameInsert(this.widthFactor, next.widthFactor) || !Insert.SameInsert(this.obliqueAngle, next.obliqueAngle)
                || !Insert.SameInsert(this.rotation, next.rotation) || this.alignment != next.alignment
                || this.isBackward != next.isBackward || this.isUpsideDown != next.isUpsideDown;
            changed |= this.TextState?.Content != null;
            this.TextState = next.TextState;
            this.position = next.position; this.normal = next.normal; this.height = next.height;
            this.width = next.width; this.widthFactor = next.widthFactor; this.obliqueAngle = next.obliqueAngle;
            this.rotation = next.rotation; this.alignment = next.alignment;
            this.isBackward = next.isBackward; this.isUpsideDown = next.isUpsideDown;
            if (changed) this.ClearProxyGraphics();
            return changed;
        }
    }
}
