using System;
using System.Collections.Generic;
using netDxf.Blocks;

namespace netDxf.Entities
{
    public partial class Insert
    {
        /// <summary>Creates a detached INSERT adopting explicit detached attributes without regenerating their values or geometry.</summary>
        /// <remarks>The caller supplies the complete attribute set in world coordinates. Definitions may be registered in the block later in the same authoring transaction.</remarks>
        public static Insert CreateWithAttributes(Block block, IEnumerable<Attribute> attributes)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));
            if (attributes == null) throw new ArgumentNullException(nameof(attributes));
            var values = new List<Attribute>();
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var attribute in attributes)
            {
                if (attribute == null || attribute.Owner != null || !tags.Add(attribute.Tag))
                    throw new ArgumentException("Attributes must be detached, non-null and have unique tags.", nameof(attributes));
                values.Add(attribute);
            }
            return new Insert(values) { Block = block };
        }
    }
}
