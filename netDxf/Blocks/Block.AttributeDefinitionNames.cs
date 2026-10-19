// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.Blocks
{
    public partial class Block
    {
        /// <summary>Prepares simultaneous definition tag changes, including swaps, without replacing definitions.</summary>
        /// <param name="renames">Current tags mapped to their new tags; omitted definitions keep their tags.</param>
        /// <returns>A single-use plan that refuses changed definition membership, order or tags when applied.</returns>
        /// <remarks>Preserves handles, values, resource registrations and order. Existing INSERT attributes
        /// are unchanged; callers may explicitly update their bindings while retaining assigned values.</remarks>
        public AttributeDefinitionRenamePlan PrepareAttributeDefinitionRenames(IEnumerable<KeyValuePair<string, string>> renames)
        {
            return this.AttributeDefinitions.PrepareRenames(this, renames);
        }

        /// <summary>Changes definition tags simultaneously without changing existing INSERT attributes.</summary>
        /// <param name="renames">Current tags mapped to new tags.</param>
        public void RenameAttributeDefinitions(IEnumerable<KeyValuePair<string, string>> renames)
        {
            this.PrepareAttributeDefinitionRenames(renames).Apply();
        }
    }

    /// <summary>A single-use validated rename bound to one block's current definition membership and order.</summary>
    public sealed class AttributeDefinitionRenamePlan
    {
        private readonly Action apply;
        internal AttributeDefinitionRenamePlan(Action apply) { this.apply = apply; }
        /// <summary>Applies the prepared tags, or refuses a changed block without modifying it.</summary>
        public void Apply() { this.apply(); }
    }
}
