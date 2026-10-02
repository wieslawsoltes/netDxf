// Copyright (c) netDxf contributors. Licensed under the MIT License.
namespace netDxf.Objects
{
    public partial class LayerStateProperties
    {
        // The owning Layer operation has already validated the new resource name.
        // No user callback or reconstruction of captured values occurs here.
        internal void FollowLayerRename(string newName) { this.name = newName; }
    }
}
