using System;
using System.Collections.ObjectModel;

namespace netDxf.Collections
{
    /// <summary>Ordered CLASS definitions with unique, immutable DXF and C++ names.</summary>
    public sealed class DxfClassCollection : KeyedCollection<string, DxfClass>
    {
        /// <summary>Creates an empty collection using ordinal, case-sensitive identity.</summary>
        public DxfClassCollection() : base(StringComparer.Ordinal) { }

        /// <inheritdoc />
        protected override string GetKeyForItem(DxfClass item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            return item.Name;
        }
        /// <inheritdoc />
        protected override void InsertItem(int index, DxfClass item)
        {
            this.ValidateCppIdentity(item, -1);
            base.InsertItem(index, item);
        }
        /// <inheritdoc />
        protected override void SetItem(int index, DxfClass item)
        {
            this.ValidateCppIdentity(item, index);
            base.SetItem(index, item);
        }
        private void ValidateCppIdentity(DxfClass item, int replacedIndex)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            for (int i = 0; i < this.Count; i++)
                if (i != replacedIndex && string.Equals(this[i].CppClassName, item.CppClassName, StringComparison.Ordinal))
                    throw new ArgumentException("A CLASS C++ name must be unique within the collection.", nameof(item));
        }
    }
}
