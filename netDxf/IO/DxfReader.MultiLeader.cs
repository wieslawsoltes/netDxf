// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using netDxf.Entities;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly List<Tuple<MLeaderData,short,string>> mleaderReferences=new List<Tuple<MLeaderData,short,string>>();
        private readonly List<MultiLeader> loadedMLeaders=new List<MultiLeader>();
        private MultiLeader ReadMultiLeader()
        {
            if(this.doc.DrawingVariables.AcadVer<netDxf.Header.DxfVersion.AutoCad2007)throw new NotSupportedException("Typed MULTILEADER input requires the qualified R2007 or later profile.");
            if(this.chunk.Code!=100||this.chunk.ReadString()!="AcDbMLeader")throw new InvalidDataException("MULTILEADER requires AcDbMLeader subclass data.");
            var leader=new MultiLeader{PendingInputReferences=true};var tags=new List<DxfTag>();
            bool xdataStarted=false;this.chunk.Next();
            while(this.chunk.Code!=0)
            {
                if(this.chunk.Code==1001)
                {
                    xdataStarted=true;string app=this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString());
                    leader.XData.Add(this.ReadXDataRecord(this.GetApplicationRegistry(app)));continue;
                }
                if(xdataStarted)throw new InvalidDataException("MULTILEADER XData must follow the complete entity payload.");
                tags.Add(new DxfTag(this.chunk.Code,this.chunk.Value));this.chunk.Next();
            }
            var parser=new MLeaderParser(this,tags);
            parser.ReadEntity(leader);
            this.loadedMLeaders.Add(leader);return leader;
        }
        private bool ReadMLeaderStylePayload(DatabaseRecord record,string name,List<DxfTag> tags,int start)
        {
            if(name!="MLEADERSTYLE"||this.doc.DrawingVariables.AcadVer<netDxf.Header.DxfVersion.AutoCad2007)return false;
            var style=new DxfMLeaderStyle();record.Object=style;
            if(start>=tags.Count||tags[start].Code!=100||(string)tags[start].Value!="AcDbMLeaderStyle")throw new InvalidDataException("MLEADERSTYLE requires AcDbMLeaderStyle subclass data.");
            int end=tags.FindIndex(start,t=>t.Code==1001);
            if(end<0)end=tags.Count;else this.ReadDatabaseXData(style,tags,end);
            var parser=new MLeaderParser(this,tags.GetRange(start+1,end-start-1));parser.ReadStyle(style);return true;
        }
        private void ResolveMultiLeaderReferences()
        {
            foreach(var pending in this.mleaderReferences)
            {
                DxfObject target=pending.Item3=="0"?null:this.doc.GetObjectByHandle(pending.Item3);
                if(target==null && pending.Item3!="0")throw new InvalidDataException("Unresolved MULTILEADER reference: "+pending.Item3);
                try{pending.Item1.Set(pending.Item2,target);}catch(ArgumentException error){throw new InvalidDataException("Invalid MULTILEADER reference target type or ownership.",error);}
            }
            foreach(var leader in this.loadedMLeaders){leader.PendingInputReferences=false;leader.Validate(this.doc,this.doc.DrawingVariables.AcadVer);}
            foreach(var style in this.doc.Objects.Items.OfType<DxfMLeaderStyle>())style.ValidateValues(this.doc.DrawingVariables.AcadVer);
        }
        private sealed class MLeaderParser
        {
            private readonly DxfReader reader;
            private readonly List<DxfTag> tags;
            private int at;
            internal MLeaderParser(DxfReader reader,List<DxfTag> tags){this.reader=reader;this.tags=tags;}
            private bool End {get{return this.at>=this.tags.Count;}}
            private short Code {get{return this.End?(short)-1:this.tags[this.at].Code;}}
            private InvalidDataException Error(string message){return new InvalidDataException(message+" (MULTILEADER payload tag "+this.at+").");}
            private DxfTag Take(short code)
            {if(this.Code!=code)throw this.Error("Expected group "+code+", found "+this.Code);return this.tags[this.at++];}
            private void Marker(short code,string value){DxfTag tag=this.Take(code);if(!(tag.Value is string text)||text!=value)throw this.Error("Invalid context delimiter");}
            private bool Flag(short code){return (bool)this.Take(code).Value;}
            private Vector3 Point(short code)
            {
                double x=(double)this.Take(code).Value,y=(double)this.Take((short)(code+10)).Value,z=(double)this.Take((short)(code+20)).Value;
                return new Vector3(x,y,z);
            }
            private bool Scalar(MLeaderData data,HashSet<short> seen)
            {
                short code=this.Code;
                MLeaderField field=data.Fields.FirstOrDefault(f=>f.Code==code||f.Type==typeof(Vector3)&&(f.Code+10==code||f.Code+20==code));
                if(field==null)return false;
                if(!seen.Add(code))throw this.Error("Duplicate group "+code);
                object value=this.tags[this.at++].Value;
                try
                {
                    if(field.Reference)this.reader.mleaderReferences.Add(Tuple.Create(data,field.Code,(string)value));
                    else if(field.Type==typeof(Vector3))
                    {
                        Vector3 vector=data.Get<Vector3>(field.Code);
                        if(code==field.Code)vector.X=(double)value;else if(code==field.Code+10)vector.Y=(double)value;else vector.Z=(double)value;
                        data.Set(field.Code,vector);
                    }
                    else
                    {
                        if(value is string text)value=this.reader.DecodeEncodedNonAsciiCharacters(text);
                        data.Set(code,value);
                    }
                }
                catch(ArgumentException error){throw new InvalidDataException("Invalid MULTILEADER group "+code,error);}
                return true;
            }
            private void Complete(MLeaderData data,HashSet<short> seen)
            {
                foreach(var field in data.Fields.Where(f=>f.Type==typeof(Vector3)))
                {
                    int count=(seen.Contains(field.Code)?1:0)+(seen.Contains((short)(field.Code+10))?1:0)+(seen.Contains((short)(field.Code+20))?1:0);
                    if(count!=0&&count!=3)throw this.Error("Incomplete vector starting at group "+field.Code);
                }
            }
            internal void ReadEntity(MultiLeader leader)
            {
                if((short)this.Take(270).Value!=2)throw this.Error("Only AcDbMLeader version 2 is qualified");
                bool context=false;var seen=new HashSet<short>();
                while(!this.End)
                {
                    if(this.Code==300)
                    {if(context)throw this.Error("Duplicate context");this.ReadContext(leader.Context);context=true;}
                    else if(this.Code==94)
                    {
                        var arrow=new MLeaderArrowHead();var fields=new HashSet<short>();this.Scalar(arrow,fields);
                        if(this.Code!=345)throw this.Error("Arrow index requires an arrow block handle");this.Scalar(arrow,fields);leader.Properties.ArrowHeads.Add(arrow);
                    }
                    else if(this.Code==330)
                    {
                        var attribute=new MLeaderBlockAttribute();var fields=new HashSet<short>();
                        foreach(short code in new short[]{330,177,44,302}){if(this.Code!=code)throw this.Error("Incomplete block attribute packet");this.Scalar(attribute,fields);}
                        leader.Properties.BlockAttributes.Add(attribute);
                    }
                    else if(!this.Scalar(leader.Properties,seen))throw this.Error("Unsupported entity group "+this.Code);
                }
                if(!context)throw this.Error("Missing context");this.Complete(leader.Properties,seen);
            }
            internal void ReadStyle(DxfMLeaderStyle style)
            {
                var seen=new HashSet<short>();
                if(this.Code==179 && (short)this.Take(179).Value!=2)throw this.Error("Only MLEADERSTYLE envelope value 179=2 is qualified");
                while(!this.End)if(!this.Scalar(style.Properties,seen))throw this.Error("Unsupported MLEADERSTYLE group "+this.Code);
                this.Complete(style.Properties,seen);
            }
            private void ReadContext(MLeaderContext context)
            {
                this.Marker(300,"CONTEXT_DATA{");var seen=new HashSet<short>();bool mtext=false,block=false;
                while(this.Code!=301)
                {
                    if(this.Code==290)
                    {
                        if(mtext||block)throw this.Error("Duplicate or misplaced text-content flag");mtext=true;
                        if(this.Flag(290))
                        {
                            var content=new MLeaderMTextContent();var fields=new HashSet<short>();
                            while(this.Code!=296)
                            {
                                if(this.Code==144)content.ColumnHeights.Add((double)this.Take(144).Value);
                                else if(!this.Scalar(content,fields))throw this.Error("Unsupported or unterminated embedded text");
                            }
                            this.Complete(content,fields);context.MText=content;
                        }
                    }
                    else if(this.Code==296)
                    {
                        if(!mtext||block)throw this.Error("Duplicate or misplaced block-content flag");block=true;
                        if(this.Flag(296))
                        {
                            if(context.MText!=null)throw this.Error("Text and block content are mutually exclusive");
                            var content=new MLeaderBlockContent();var fields=new HashSet<short>();
                            while(true)
                            {
                                if(this.Code==47)content.TransformationMatrix.Add((double)this.Take(47).Value);
                                else if(!this.Scalar(content,fields))break;
                            }
                            this.Complete(content,fields);
                            if(content.TransformationMatrix.Count!=0 && content.TransformationMatrix.Count!=16)throw this.Error("Incomplete block transformation matrix");
                            context.Block=content;
                        }
                    }
                    else if(this.Code==302){if(!block)throw this.Error("Leader precedes content flags");context.Leaders.Add(this.ReadNode());}
                    else if(!this.Scalar(context,seen))throw this.Error("Unsupported or unterminated context");
                }
                if(!mtext||!block)throw this.Error("Context requires both content-presence flags");
                this.Marker(301,"}");this.Complete(context,seen);
            }
            private MLeaderNode ReadNode()
            {
                this.Marker(302,"LEADER{");var node=new MLeaderNode();var seen=new HashSet<short>();
                while(this.Code!=303)
                {
                    if(this.Code==304)node.Lines.Add(this.ReadLine());
                    else if(this.Code==12)node.Breaks.Add(new MLeaderBreak(this.Point(12),this.Point(13)));
                    else if(!this.Scalar(node,seen))throw this.Error("Unsupported or unterminated leader branch");
                }
                this.Marker(303,"}");this.Complete(node,seen);return node;
            }
            private MLeaderLine ReadLine()
            {
                this.Marker(304,"LEADER_LINE{");var line=new MLeaderLine();var seen=new HashSet<short>();
                while(this.Code!=305)
                {
                    if(this.Code==10)line.Vertices.Add(this.Point(10));
                    else if(this.Code==90)
                    {
                        var group=new MLeaderLineBreaks{Index=(int)this.Take(90).Value};
                        while(this.Code==11)group.Breaks.Add(new MLeaderBreak(this.Point(11),this.Point(12)));
                        if(group.Breaks.Count==0)throw this.Error("A break index requires at least one complete endpoint pair");line.Breaks.Add(group);
                    }
                    else if(!this.Scalar(line,seen))throw this.Error("Unsupported or unterminated leader line");
                }
                this.Marker(305,"}");this.Complete(line,seen);return line;
            }
        }
    }
}
