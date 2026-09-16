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
                if(item is Polyline3DRecord polylineRecord)references=polylineRecord.References;
                else if(item is DxfOpaqueEntity opaqueEntity)references=opaqueEntity.References;
                else if(item is DxfStoredSunStudy study)references=study.References;
                else if(item is DxfOpaqueObject opaqueStudy && opaqueStudy.CodeName=="SUNSTUDY")
                    references=opaqueStudy.Tags.Where(DxfObjectDatabase.IsReference).Select(tag=>this.StoredTableHandleTarget((string)tag.Value)).Where(value=>value!=null);
                else if(item is PolygonMeshRecord meshRecord)references=meshRecord.References;
                else if(item is PolyfaceMeshRecord polyfaceRecord)references=polyfaceRecord.References;
                else if(item is Polyline2DRecord legacyRecord)references=legacyRecord.References;
                else if(item is DxfStoredField field)references=field.References;
                else if(item is StoredTable table)references=table.References;
                else if(item is Section section)references=section.GeometrySettings==null?new DxfObject[0]:new DxfObject[]{section.GeometrySettings};
                else if(item is DxfSectionSettings settings)references=settings.DatabaseReferences;
                else if(item is MultiLeader leader)references=leader.Data.SelectMany(d=>d.References);
                else if(item is DxfMLeaderStyle style)references=style.DatabaseReferences;
                else if(item is DxfStoredTableContent content)references=content.References;
                else if(item is DxfStoredTableGeometry geometry)references=geometry.References;
                else if(item is DxfStoredCellStyleMap map)references=map.References;
                else if(item is DxfOpaqueObject opaqueContent && (opaqueContent.CodeName=="TABLECONTENT" || opaqueContent.CodeName=="TABLEGEOMETRY" || opaqueContent.CodeName=="CELLSTYLEMAP"))
                    references=opaqueContent.Tags.Where(DxfObjectDatabase.IsReference).Select(tag=>this.StoredTableHandleTarget((string)tag.Value)).Where(value=>value!=null);
                else if(item is DxfTableStyle tableStyle)references=tableStyle.References;
                else if(item is DxfStoredDimAssoc association)references=association.References;
                else if(item is DxfOpaqueObject opaque && opaque.CodeName=="DIMASSOC")references=opaque.Tags.Where(DxfObjectDatabase.IsReference).Select(tag=>this.StoredTableHandleTarget((string)tag.Value));
                else continue;
                int count=references.Count(r=>ReferenceEquals(r,target));
                if(count>0)result.Add(new DxfObjectReference(item,count));
            }
            return result;
        }
    }
}
