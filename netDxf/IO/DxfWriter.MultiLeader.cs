// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Linq;
using netDxf.Collections;
using netDxf.Entities;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void ValidateMultiLeaders()
        {
            foreach(var block in this.doc.Blocks)
                foreach(var leader in block.Entities.OfType<MultiLeader>())leader.Validate(this.doc,this.doc.DrawingVariables.AcadVer);
        }
        private void PrepareMultiLeaderClasses(DxfClassCollection definitions)
        {
            int entities=this.doc.Blocks.Sum(b=>b.Entities.OfType<MultiLeader>().Count());
            int styles=this.doc.Objects.Items.Count(o=>o.CodeName=="MLEADERSTYLE");
            this.PrepareMultiLeaderClass(definitions,"MULTILEADER","AcDbMLeader","ACDB_MLEADER_CLASS",true,entities);
            this.PrepareMultiLeaderClass(definitions,"MLEADERSTYLE","AcDbMLeaderStyle","ACDB_MLEADERSTYLE_CLASS",false,styles);
        }
        private void PrepareMultiLeaderClass(DxfClassCollection definitions,string name,string cpp,string app,bool entity,int count)
        {
            if(definitions.Contains(name))
            {
                DxfClass definition=definitions[name];
                if(definition.CppClassName!=cpp||definition.IsEntity!=entity)throw new System.IO.InvalidDataException("CLASS conflicts with "+name);
                definition.InstanceCount=count;
            }
            else if(count>0)definitions.Add(new DxfClass(name,cpp,app){ProxyFlags=entity?1025:4095,IsEntity=entity,InstanceCount=count});
        }
        private void WriteMLeaderVector(short code,Vector3 value)
        {this.chunk.Write(code,value.X);this.chunk.Write((short)(code+10),value.Y);this.chunk.Write((short)(code+20),value.Z);}
        private void WriteMLeaderFields(MLeaderData data,Func<MLeaderField,bool> include=null)
        {
            foreach(var field in data.Fields)
            {
                if(include!=null&&!include(field))continue;
                object value=data.Value(field);if(value==null)continue;
                if(field.Reference)this.chunk.Write(field.Code,((DxfObject)value).Handle);
                else if(value is Vector3 vector)this.WriteMLeaderVector(field.Code,vector);
                else if(value is string text)this.chunk.Write(field.Code,this.EncodeDatabaseString(text));
                else this.chunk.Write(field.Code,value);
            }
        }
        private void WriteMultiLeader(MultiLeader leader)
        {
            this.chunk.Write(100,"AcDbMLeader");this.chunk.Write(270,leader.Version);this.WriteMLeaderContext(leader.Context);
            this.WriteMLeaderFields(leader.Properties,f=>f.Code!=294&&f.Code!=178&&f.Code!=179&&f.Code!=45&&f.Code!=271&&f.Code!=272&&f.Code!=273&&f.Code!=295);
            foreach(var arrow in leader.Properties.ArrowHeads)this.WriteMLeaderFields(arrow);
            foreach(var attribute in leader.Properties.BlockAttributes)this.WriteMLeaderFields(attribute);
            this.WriteMLeaderFields(leader.Properties,f=>f.Code==294||f.Code==178||f.Code==179||f.Code==45||f.Code==271||f.Code==272||f.Code==273||f.Code==295);
            this.WriteXData(leader.XData);
        }
        private void WriteMLeaderContext(MLeaderContext context)
        {
            this.chunk.Write(300,"CONTEXT_DATA{");
            this.WriteMLeaderFields(context,f=>f.Code!=110&&f.Code!=111&&f.Code!=112&&f.Code!=297&&f.Code!=272&&f.Code!=273);
            this.chunk.Write(290,context.MText!=null);
            if(context.MText!=null)
            {
                this.WriteMLeaderFields(context.MText,f=>f.Code!=295);
                foreach(double value in context.MText.ColumnHeights)this.chunk.Write(144,value);
                this.chunk.Write(295,context.MText.UseWordBreak);
            }
            this.chunk.Write(296,context.Block!=null);
            if(context.Block!=null)
            {this.WriteMLeaderFields(context.Block);foreach(double value in context.Block.TransformationMatrix)this.chunk.Write(47,value);}
            this.WriteMLeaderFields(context,f=>f.Code==110||f.Code==111||f.Code==112||f.Code==297);
            foreach(var node in context.Leaders)
            {
                this.chunk.Write(302,"LEADER{");this.WriteMLeaderFields(node,f=>f.Code!=90&&f.Code!=40&&f.Code!=271);
                foreach(var pair in node.Breaks){this.WriteMLeaderVector(12,pair.Start);this.WriteMLeaderVector(13,pair.End);}
                this.WriteMLeaderFields(node,f=>f.Code==90||f.Code==40);
                foreach(var line in node.Lines)
                {
                    this.chunk.Write(304,"LEADER_LINE{");foreach(var point in line.Vertices)this.WriteMLeaderVector(10,point);
                    foreach(var group in line.Breaks)
                    {this.chunk.Write(90,group.Index);foreach(var pair in group.Breaks){this.WriteMLeaderVector(11,pair.Start);this.WriteMLeaderVector(12,pair.End);}}
                    this.WriteMLeaderFields(line);this.chunk.Write(305,"}");
                }
                this.WriteMLeaderFields(node,f=>f.Code==271);this.chunk.Write(303,"}");
            }
            this.WriteMLeaderFields(context,f=>f.Code==272||f.Code==273);this.chunk.Write(301,"}");
        }
        private bool WriteMLeaderStylePayload(DxfDatabaseObject item)
        {
            if(!(item is DxfMLeaderStyle style))return false;
            this.chunk.Write(100,"AcDbMLeaderStyle");this.chunk.Write(179,(short)2);this.WriteMLeaderFields(style.Properties);return true;
        }
    }
}
