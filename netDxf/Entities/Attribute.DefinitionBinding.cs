// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Attribute
    {
        /// <summary>Prepares a reversible change to this instance's tag and definition, preserving values and appearance.</summary>
        /// <param name="tag">The instance's new tag.</param>
        /// <param name="definition">The destination definition, or null for an orphan instance.</param>
        /// <param name="allowTagMismatch">Allow restoring a stored binding that predates an independent definition rename.</param>
        /// <returns>A binding change with explicit Apply and Rollback operations.</returns>
        /// <remarks>Unless restoring a stored mismatch, the definition must match the new tag when Apply runs, so a definition rename may
        /// occur after preparation. Rollback restores the exact original binding, including bindings that
        /// preceded an independent definition rename. Changed instance bindings are refused.</remarks>
        public AttributeDefinitionBindingChange PrepareDefinitionBinding(string tag, AttributeDefinition definition, bool allowTagMismatch = false)
        {
            if (string.IsNullOrEmpty(tag)) throw new ArgumentException("An attribute tag cannot be empty.", nameof(tag));
            string previousTag = this.tag; AttributeDefinition previousDefinition = this.definition; bool applied = false;
            bool Matches(string value, AttributeDefinition target) => string.Equals(this.tag, value, StringComparison.Ordinal) && ReferenceEquals(this.definition, target);
            return new AttributeDefinitionBindingChange(() =>
            {
                if (applied || !Matches(previousTag, previousDefinition)) throw new InvalidOperationException("The prepared attribute binding is stale.");
                if (!allowTagMismatch && definition != null && !string.Equals(tag, definition.Tag, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("The destination definition tag must match the attribute tag.");
                this.tag = tag; this.definition = definition; applied = true;
            }, () =>
            {
                if (!applied) return;
                if (!Matches(tag, definition)) throw new InvalidOperationException("The applied attribute binding changed before rollback.");
                this.tag = previousTag; this.definition = previousDefinition; applied = false;
            });
        }
    }

    /// <summary>A reversible native attribute binding change that preserves the attribute object.</summary>
    public sealed class AttributeDefinitionBindingChange
    {
        private readonly Action apply;
        private readonly Action rollback;
        internal AttributeDefinitionBindingChange(Action apply, Action rollback) { this.apply = apply; this.rollback = rollback; }
        /// <summary>Validates and applies the prepared tag and definition.</summary>
        public void Apply() { this.apply(); }
        /// <summary>Restores the original binding if the applied binding is still current.</summary>
        public void Rollback() { this.rollback(); }
    }
}
