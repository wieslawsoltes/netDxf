// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace netDxf.Entities
{
    /// <summary>A validated, identity-bound replacement of an INSERT's attribute membership.</summary>
    /// <remarks>Attribute objects remain mutable. Apply revalidates ownership and incoming references.
    /// This plan does not reset attribute values, geometry, resources or definition bindings.</remarks>
    public sealed class InsertAttributeReplacement
    {
        private readonly Insert owner;
        private readonly Attribute[] before;
        private readonly Attribute[] after;
        private readonly DxfObject container;
        private readonly DxfDocument document;
        internal InsertAttributeReplacement(Insert owner, Attribute[] after)
        {
            this.owner = owner;
            this.container = owner.Owner;
            this.document = owner.Owner?.Record.Owner?.Owner;
            this.before = owner.Attributes.ToArray();
            this.after = after;
            this.Attributes = new ReadOnlyCollection<Attribute>(after);
        }
        /// <summary>Gets the exact ordered destination attributes.</summary>
        public IReadOnlyList<Attribute> Attributes { get; }
        /// <summary>Applies the replacement if the insertion and its original membership are unchanged.</summary>
        /// <remarks>Added/removed notifications run after native membership and resource registration.
        /// Notification exceptions are reported after the replacement is committed. Applications
        /// needing rollback must journal the original state; arbitrary callback side effects are not reversed.</remarks>
        public void Apply()
        {
            if (!ReferenceEquals(this.owner.Owner, this.container) || !ReferenceEquals(this.owner.Owner?.Record.Owner?.Owner, this.document)
                || !this.owner.Attributes.SequenceEqual(this.before))
                throw new InvalidOperationException("The INSERT ownership or attribute membership changed after preparation.");
            this.owner.ApplyAttributeReplacement(this.after);
        }
    }

    public partial class Insert
    {
        private bool replacingAttributes;

        /// <summary>Prepares an exact attribute membership replacement without changing the insertion.</summary>
        /// <remarks>Each attribute must be detached or already owned by this insertion. Object identities
        /// must be unique; duplicate tag strings remain distinct instances. No coordinates are transformed.</remarks>
        public InsertAttributeReplacement PrepareAttributeReplacement(IEnumerable<Attribute> attributes)
        {
            if (attributes == null) throw new ArgumentNullException(nameof(attributes));
            var values = attributes.ToArray();
            this.ValidateAttributeReplacement(values);
            return new InsertAttributeReplacement(this, values);
        }

        /// <summary>Replaces the attribute membership while retaining the collection and surviving attribute identities.</summary>
        /// <remarks>Uses the same validation and notification semantics as PrepareAttributeReplacement().Apply().</remarks>
        public void ReplaceAttributes(IEnumerable<Attribute> attributes)
        {
            this.PrepareAttributeReplacement(attributes).Apply();
        }

        private void ValidateAttributeReplacement(Attribute[] values)
        {
            if (this.replacingAttributes) throw new InvalidOperationException("Attribute membership cannot be changed from an attribute notification.");
            var unique = new HashSet<Attribute>();
            foreach (Attribute attribute in values)
            {
                if (attribute == null || !unique.Add(attribute) || attribute.Owner != null && !ReferenceEquals(attribute.Owner, this))
                    throw new ArgumentException("Attributes must be non-null unique objects, detached or owned by this insertion.", nameof(values));
                if (attribute.Layer == null || attribute.Linetype == null || attribute.Style == null || attribute.Color == null || attribute.Transparency == null)
                    throw new ArgumentException("Every attribute requires complete appearance resources.", nameof(values));
            }
            var document = this.Owner?.Record.Owner?.Owner;
            if (document != null)
                foreach (Attribute attribute in this.attributes)
                    if (!unique.Contains(attribute) && (attribute.ExtensionDictionary != null || document.StoredTableReferencesRemoval(attribute)
                        || document.ExposedObjectReferences(attribute).Count != 0))
                        throw new InvalidOperationException("A stored drawing reference prevents removal of an attribute.");
        }

        internal void ApplyAttributeReplacement(Attribute[] values)
        {
            this.ValidateAttributeReplacement(values);
            if (this.attributes.SequenceEqual(values)) return;
            var previous = this.attributes.ToArray();
            var retained = new HashSet<Attribute>(values);
            var existing = new HashSet<Attribute>(previous);
            var removed = previous.Where(a => !retained.Contains(a)).ToArray();
            var added = values.Where(a => !existing.Contains(a)).ToArray();
            var document = this.Owner?.Record.Owner?.Owner;
            this.replacingAttributes = true;
            try
            {
                foreach (Attribute attribute in removed)
                {
                    document?.UnregisterInsertAttribute(attribute);
                    attribute.Owner = null; attribute.Handle = null;
                }
                foreach (Attribute attribute in added)
                {
                    attribute.Owner = this;
                    document?.RegisterInsertAttribute(this, attribute);
                }
                this.attributes.ReplaceContents(values);
                this.ClearProxyGraphics();
                // Registration must not depend on whether an application subscribed before
                // or after document adoption. Callbacks observe the complete final collection.
                var failures = new List<Exception>();
                foreach (Attribute attribute in removed)
                    try { this.OnAttributeRemovedEvent(attribute); } catch (Exception error) { failures.Add(error); }
                foreach (Attribute attribute in added)
                    try { this.OnAttributeAddedEvent(attribute); } catch (Exception error) { failures.Add(error); }
                if (failures.Count != 0) throw new AggregateException("Attribute membership was committed, but notification callbacks failed.", failures);
            }
            finally { this.replacingAttributes = false; }
        }
    }
}
