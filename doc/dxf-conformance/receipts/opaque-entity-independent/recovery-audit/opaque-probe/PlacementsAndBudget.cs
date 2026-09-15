using System.Globalization;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

internal static partial class Program
{
    private static void PlacementsAndBudget()
    {
        foreach(bool binary in new[]{false,true})foreach(bool paper in new[]{false,true})
            Run("placement-"+paper+"-"+binary,()=>
            {
                var doc=Load(RawFixture(DxfVersion.AutoCad2018),false);Block block;
                if(paper)block=doc.Layouts.Add(new Layout("IndependentPaper")).AssociatedBlock;
                else{block=new Block("INDEPENDENT_UNUSED");doc.Blocks.Add(block);}
                var raw=Raw(Save(doc,false));var packet=Packet(raw);var tags=packet.Tags.ToList();tags[2]=new DxfTag(330,block.Record.Handle);int body=tags.FindIndex(t=>t.Code==100&&Equals(t.Value,"AcDbQualifiedFutureCurve"));
                tags.Insert(body,new DxfTag(67,(short)(paper?1:0)));if(paper)tags.Insert(body,new DxfTag(410,"IndependentPaper"));
                if(paper)raw=raw.WithRecord(packet,tags);
                else
                {
                    raw=raw.WithoutRecord(packet);var begin=raw.Sections.Single(s=>s.Name=="BLOCKS").Records.Single(r=>r.Name=="BLOCK"&&r.Tags.Any(t=>t.Code==2&&Equals(t.Value,"INDEPENDENT_UNUSED")));
                    var all=raw.Tags.ToList();all.InsertRange(begin.EndTagIndex,tags);raw=DxfRawDocument.Create(all);
                }
                doc=Load(raw,binary);var entity=doc.Blocks.SelectMany(b=>b.Entities).OfType<DxfOpaqueEntity>().Single();Check(entity.Owner.Name==block.Name&&Same(tags,entity.SourceTags),"Placement source owner or packet changed.");
                long seed=Seed(doc);foreach(Action action in new Action[]{()=>entity.Owner.Clone("FORBIDDEN"),()=>new Insert(entity.Owner).Explode()})
                {bool rejected=false;try{action();}catch(NotSupportedException){rejected=true;}Check(rejected&&Seed(doc)==seed,"Block geometry failed to reject before mutation.");}
                var report=doc.AnalyzeVersionCompatibility(DxfVersion.AutoCad2004);Check(report.Diagnostics.Any(d=>ReferenceEquals(d.SourceObject,entity)&&d.Code=="STORED_SOURCE_PROFILE"),"Non-model source omitted from compatibility report.");
                var bytes=Save(doc,!binary);Check(Same(tags,Packet(Raw(bytes)).Tags),"Placed unknown source packet changed on output.");Check(Load(bytes).Blocks.SelectMany(b=>b.Entities).OfType<DxfOpaqueEntity>().Single().Owner.Name==block.Name,"Placed source changed owner after reload.");File.WriteAllBytes(Path.Combine(Output,"placement-"+paper+"-"+binary+".dxf"),bytes);
            });
        Run("aggregate-budget-after-xdata-growth",()=>
        {
            var raw=RawFixture(DxfVersion.AutoCad2018);var packet=Packet(raw);var copies=new List<DxfTag>();
            for(int i=0;i<17;i++){var tags=packet.Tags.ToList();tags[1]=new DxfTag(5,(0xF100+i).ToString("X",CultureInfo.InvariantCulture));copies.AddRange(tags);}
            var all=raw.Tags.ToList();all.RemoveRange(packet.StartTagIndex,packet.EndTagIndex-packet.StartTagIndex);all.InsertRange(packet.StartTagIndex,copies);raw=DxfRawDocument.Create(all);
            var declaration=raw.Sections.Single(s=>s.Name=="CLASSES").Records.Single(r=>r.Tags.Any(t=>t.Code==1&&Equals(t.Value,Name)));raw=raw.WithRecord(declaration,declaration.Tags.Select(t=>t.Code==91?new DxfTag(91,17):t));
            var doc=Load(raw,true);var entities=doc.Entities.OpaqueEntities.ToArray();Check(entities.Length==17,"Aggregate fixture inventory differs.");
            for(int i=0;i<entities.Length;i++){int total=i==16?8576:65000;int added=total-(entities[i].SourceTags.Count-1);for(int k=0;k<added;k++)entities[i].XData["OPAQUE_TEST"].XDataRecord.Add(new XDataRecord(XDataCode.Int16,(short)1));}
            byte[] bytes=Save(doc,true);var reloaded=Load(bytes);var retained=reloaded.Entities.OpaqueEntities.ToArray();Check(retained.Length==17&&retained.Sum(e=>e.SourceTags.Count-1)==1048576&&retained.All(e=>e.SourceTags.Count<=65536),"Exact aggregate output did not reload at the reader boundary.");
            entities[^1].XData["OPAQUE_TEST"].XDataRecord.Add(new XDataRecord(XDataCode.Int16,(short)2));Refuse(doc,false,"aggregate-text");Refuse(doc,true,"aggregate-binary");
            File.WriteAllBytes(Path.Combine(Output,"aggregate-exact-binary.dxf"),bytes);
        });
    }
}
