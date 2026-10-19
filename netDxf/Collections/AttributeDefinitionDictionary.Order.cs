// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using netDxf.Blocks;
using netDxf.Entities;

namespace netDxf.Collections
{
    public sealed partial class AttributeDefinitionDictionary
    {
        /// <summary>Changes definition enumeration and insertion prompt order without changing membership.</summary>
        /// <param name="definitions">Every current definition exactly once, in the desired order.</param>
        /// <remarks>Retains definition objects, handles, owners and resource registrations. It does not
        /// raise membership events or change the attributes of existing block references.
        /// Constant definitions may also be ordered; user interfaces can impose their own prompt policy.</remarks>
        public void Reorder(IEnumerable<AttributeDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            var ordered = new List<AttributeDefinition>(definitions);
            if (ordered.Count != this.Count)
                throw new ArgumentException("The order must include every current definition exactly once.", nameof(definitions));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AttributeDefinition definition in ordered)
            {
                if (definition == null || !seen.Add(definition.Tag) ||
                    !this.innerDictionary.TryGetValue(definition.Tag, out AttributeDefinition current) ||
                    !ReferenceEquals(current, definition))
                    throw new ArgumentException("The order must contain unique current definition objects.", nameof(definitions));
                if (definition.Owner != null && definition.Owner.Flags.HasFlag(BlockTypeFlags.ExternallyDependent))
                    throw new InvalidOperationException("Externally dependent block definitions cannot be reordered.");
            }
            bool changed = false;
            for (int i = 0; i < ordered.Count; i++)
                if (!ReferenceEquals(ordered[i], this.orderedDefinitions[i])) { changed = true; break; }
            if (!changed) return;
            this.orderedDefinitions.Clear();
            this.orderedDefinitions.AddRange(ordered);
            this.orderedTags.Clear();
            foreach (AttributeDefinition definition in ordered) this.orderedTags.Add(definition.Tag);
        }

        private static void ValidateCopy<T>(T[] array, int arrayIndex, int count)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0 || arrayIndex > array.Length) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            if (array.Length - arrayIndex < count) throw new ArgumentException("The destination array is too small.", nameof(array));
        }

        // Keep the public read-only view in the framework assembly, like the previous
        // Dictionary.KeyCollection, and preserve its case-insensitive Contains contract.
        private sealed class OrderedTagList : IList<string>
        {
            private readonly AttributeDefinitionDictionary owner;
            internal OrderedTagList(AttributeDefinitionDictionary owner) { this.owner = owner; }
            public string this[int index]
            {
                get { return this.owner.orderedTags[index]; }
                set { throw new NotSupportedException(); }
            }
            public int Count { get { return this.owner.Count; } }
            public bool IsReadOnly { get { return true; } }
            public bool Contains(string item) { return this.owner.ContainsTag(item); }
            public int IndexOf(string item)
            {
                for (int i = 0; i < this.Count; i++)
                    if (string.Equals(item, this[i], StringComparison.OrdinalIgnoreCase)) return i;
                return -1;
            }
            public void CopyTo(string[] array, int arrayIndex) { this.owner.orderedTags.CopyTo(array, arrayIndex); }
            public IEnumerator<string> GetEnumerator() { return this.owner.orderedTags.GetEnumerator(); }
            IEnumerator IEnumerable.GetEnumerator() { return this.GetEnumerator(); }
            public void Add(string item) { throw new NotSupportedException(); }
            public void RemoveAt(int index) { throw new NotSupportedException(); }
            public void Insert(int index, string item) { throw new NotSupportedException(); }
            public bool Remove(string item) { throw new NotSupportedException(); }
            public void Clear() { throw new NotSupportedException(); }
        }

    }
}
