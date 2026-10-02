// Copyright (c) netDxf contributors. Licensed under the MIT License.
namespace netDxf.Collections
{
    public sealed partial class ObservableDictionary<TKey, TValue>
    {
        // Only used by the prevalidated, callback-free saved-layer rename publication.
        // A key rename preserves membership; it is not a public remove/add operation.
        // Keep the actual dictionary (and its live Keys/Values views), not a replacement.
        internal void RekeyWithoutNotifications(TKey oldKey, TKey newKey)
        {
            TValue value = this.innerDictionary[oldKey];
            this.innerDictionary.Remove(oldKey);
            this.innerDictionary.Add(newKey, value);
        }
    }
}
