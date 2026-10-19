// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Attribute
    {
        /// <summary>Creates a detached attribute from a definition in the specified insertion frame.</summary>
        /// <param name="definition">The source definition; it is not modified.</param>
        /// <param name="transformation">The insertion matrix, including block-to-owner unit conversion.</param>
        /// <param name="translation">The insertion position minus the transformed block origin.</param>
        /// <param name="mirrorText">The owning drawing's MIRRTEXT policy.</param>
        /// <returns>A detached attribute with definition defaults, transformed geometry and no proxy graphics.</returns>
        /// <remarks>Uses the same geometry rules as native attribute transformation. Unsupported embedded
        /// text transformations are rejected before returning. Table resources follow the normal Attribute
        /// constructor sharing rules. No document, global default, or original attribute is mutated.</remarks>
        public static Attribute CreateTransformed(AttributeDefinition definition, Matrix3 transformation,
            Vector3 translation, bool mirrorText)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            Insert.FiniteInsert(translation);
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) Insert.FiniteInsert(transformation[i, j]);
            var result = new Attribute(definition);
            result.CheckInsertGeometry();
            if (transformation == Matrix3.Identity)
                result.CommitInsertTransform(result.PrepareInsertTransform(transformation, translation, true));
            else result.TransformBy(transformation, translation, mirrorText);
            result.CheckInsertGeometry();
            result.ClearProxyGraphics();
            return result;
        }
    }
}
