// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Blocks;
using netDxf.Entities;

namespace netDxf.Collections
{
    public sealed partial class AttributeDefinitionDictionary
    {
        internal AttributeDefinitionRenamePlan PrepareRenames(Block owner, IEnumerable<KeyValuePair<string, string>> renames)
        {
            if (renames == null) throw new ArgumentNullException(nameof(renames));
            var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in renames)
            {
                if (string.IsNullOrEmpty(pair.Key) || !this.innerDictionary.ContainsKey(pair.Key))
                    throw new ArgumentException("Every source tag must exist in the block.", nameof(renames));
                if (string.IsNullOrEmpty(pair.Value)) throw new ArgumentException("A definition tag cannot be empty.", nameof(renames));
                mapping.Add(pair.Key, pair.Value);
            }
            var definitions = this.orderedDefinitions.ToArray();
            var tags = this.orderedTags.ToArray(); var nextTags = new string[tags.Length];
            var next = new Dictionary<string, AttributeDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (string tag in mapping.Keys)
                if (!this.innerDictionary.ContainsKey(tag)) throw new ArgumentException("A source tag changed during enumeration.", nameof(renames));
            bool changed = false;
            for (int i = 0; i < definitions.Length; i++)
            {
                nextTags[i] = mapping.TryGetValue(tags[i], out string name) ? name : tags[i];
                next.Add(nextTags[i], definitions[i]); // Validate the complete destination, including unedited tags.
                changed |= !string.Equals(tags[i], nextTags[i], StringComparison.Ordinal);
            }
            void Validate()
            {
                if (!ReferenceEquals(owner.AttributeDefinitions, this) || owner.Flags.HasFlag(BlockTypeFlags.ExternallyDependent))
                    throw new InvalidOperationException("The owning block cannot be renamed.");
                if (this.Count != definitions.Length) throw new InvalidOperationException("Definition membership changed after preparation.");
                for (int i = 0; i < definitions.Length; i++)
                    if (!ReferenceEquals(this.orderedDefinitions[i], definitions[i]) || !ReferenceEquals(definitions[i].Owner, owner) ||
                        !string.Equals(definitions[i].Tag, tags[i], StringComparison.Ordinal))
                        throw new InvalidOperationException("Definition membership, order or tags changed after preparation.");
            }
            Validate(); bool applied = false;
            return new AttributeDefinitionRenamePlan(() =>
            {
                if (applied) throw new InvalidOperationException("A definition rename plan can only be applied once.");
                Validate();
                if (!changed) { applied = true; return; }
                for (int i = 0; i < definitions.Length; i++)
                {
                    definitions[i].SetTagFromOwner(nextTags[i]); this.orderedTags[i] = nextTags[i];
                    this.orderedDefinitions[i] = definitions[i]; // Invalidate active enumeration of renamed entries.
                }
                this.innerDictionary = next;
                applied = true;
            });
        }
    }
}
