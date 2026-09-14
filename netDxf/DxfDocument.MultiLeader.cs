// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Collections.Generic;
using System.Linq;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        internal List<DxfObjectReference> MLeaderReferences(DxfObject target)
        {
            var result=new List<DxfObjectReference>();
            if(target is Block block)target=block.Record;
            if(target==null)return result;
            foreach(DxfObject item in this.AddedObjects.Values)
            {
                IEnumerable<DxfObject> references;
                if(item is MultiLeader leader)references=leader.Data.SelectMany(d=>d.References);
                else if(item is DxfMLeaderStyle style)references=style.DatabaseReferences;
                else continue;
                int count=references.Count(r=>ReferenceEquals(r,target));
                if(count>0)result.Add(new DxfObjectReference(item,count));
            }
            return result;
        }
    }
}
