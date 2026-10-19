// Copyright (c) netDxf contributors. Licensed under the MIT License.
namespace netDxf.Entities
{
    public partial class AttributeDefinition
    {
        // Only the owning block's prepared dictionary operation can change an indexed tag.
        internal void SetTagFromOwner(string value) { this.tag = value; }
    }
}
