// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using System.Reflection;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] QuadMutationProperties = {
        "FirstVertex", "SecondVertex", "ThirdVertex", "FourthVertex", "Elevation", "Thickness" };
    private static readonly byte[] QuadMutationProxy = { 1, 3, 7, 11 };
    private static EntityObject QuadMutationEntity(bool trace)
    {
        EntityObject entity=trace ? new Trace() : new Solid();var type=entity.GetType();
        var points=RawQuadSourcePoints(0);
        for(int i=0;i<4;i++)type.GetProperty(QuadMutationProperties[i])!.SetValue(entity,new Vector2(points[i].X,points[i].Y));
        type.GetProperty("Elevation")!.SetValue(entity,3.0);type.GetProperty("Thickness")!.SetValue(entity,-2.5);
        entity.Normal=new Vector3(0,.6,.8);return entity;
    }
    private static void CheckQuadMutationValue(object expected,object actual)
    {
        if(expected is Vector2 vector)
        {var p=(Vector2)actual;SameDoubleBits(vector.X,p.X,"Quad corner X");SameDoubleBits(vector.Y,p.Y,"Quad corner Y");}
        else SameDoubleBits((double)expected,(double)actual,"Quad scalar");
    }
    private static void RegisterQuadMutationTests()
    {
        foreach(bool trace in new[]{false,true})for(int index=0;index<6;index++)
            foreach(bool owned in new[]{false,true})for(int mode=0;mode<5;mode++)
            {
                int i=index,m=mode;
                Run($"quad-mutation/set/{trace}/{i}/{owned}/{m}",()=>
                {
                    var entity=QuadMutationEntity(trace);PropertyInfo property=entity.GetType().GetProperty(QuadMutationProperties[i])!;
                    if(m>=3)property.SetValue(entity,i<4 ? (object)new Vector2(m==4 ? -0.0 : 0.0,2) : m==4 ? -0.0 : 0.0);
                    var doc=new DxfDocument();if(owned)doc.Entities.Add(entity);
                    var owner=entity.Owner;var handle=entity.Handle;var color=entity.Color;var normal=entity.Normal;
                    object[] original=QuadMutationProperties.Select(n=>entity.GetType().GetProperty(n)!.GetValue(entity)!).ToArray();
                    object value=original[i];
                    if(m==1)value=i<4 ? (object)new Vector2(-8,9) : -9.0;
                    else if(m==2)value=i<4 ? (object)new Vector2(Math.BitIncrement(((Vector2)value).X),((Vector2)value).Y) : Math.BitIncrement((double)value);
                    else if(m==3)value=i<4 ? (object)new Vector2(-0.0,2) : -0.0;
                    entity.ProxyGraphics=QuadMutationProxy;property.SetValue(entity,value);
                    CheckQuadMutationValue(value,property.GetValue(entity)!);
                    bool changed=m is 1 or 2 or 3;
                    Check(changed ? entity.ProxyGraphics==null : entity.ProxyGraphics!=null && entity.ProxyGraphics.SequenceEqual(QuadMutationProxy),"Quad proxy invalidation differs");
                    for(int k=0;k<6;k++)if(k!=i)CheckQuadMutationValue(original[k],entity.GetType().GetProperty(QuadMutationProperties[k])!.GetValue(entity)!);
                    RawLinePointBits(normal,entity.Normal);Check(ReferenceEquals(color,entity.Color),"Color identity changed");
                    Check(ReferenceEquals(owner,entity.Owner) && handle==entity.Handle,"Ownership changed");
                });
            }
        foreach(bool trace in new[]{false,true})for(int index=0;index<4;index++)
        {
            int i=index;
            Run($"quad-mutation/vector-cache/{trace}/{i}",()=>
            {
                var entity=QuadMutationEntity(trace);var p=entity.GetType().GetProperty(QuadMutationProperties[i])!;
                Vector2 normalized=Vector2.Normalize(new Vector2(3,4));p.SetValue(entity,new Vector2(normalized.X,normalized.Y));entity.ProxyGraphics=QuadMutationProxy;
                p.SetValue(entity,normalized);var stored=(Vector2)p.GetValue(entity)!;
                Equal(normalized.IsNormalized,stored.IsNormalized,"Bit-identical assignment lost normalization cache");
                Check(entity.ProxyGraphics!=null && entity.ProxyGraphics.SequenceEqual(QuadMutationProxy),"Cache-only assignment invalidated geometry");
            });
        }
        foreach(bool trace in new[]{false,true})for(int index=0;index<6;index++)
        {
            int i=index;
            Run($"quad-mutation/clone/{trace}/{i}",()=>
            {
                var source=QuadMutationEntity(trace);source.ProxyGraphics=QuadMutationProxy;var copy=(EntityObject)source.Clone();
                Check(copy.ProxyGraphics!=null && copy.ProxyGraphics.SequenceEqual(QuadMutationProxy),"Clone initialization lost source proxy");
                var p=source.GetType().GetProperty(QuadMutationProperties[i])!;object original=p.GetValue(source)!;
                p.SetValue(copy,i<4 ? (object)new Vector2(-8,9) : -9.0);
                CheckQuadMutationValue(original,p.GetValue(source)!);
                Check(copy.ProxyGraphics==null,"Clone mutation retained proxy");Check(source.ProxyGraphics!=null && source.ProxyGraphics.SequenceEqual(QuadMutationProxy),"Clone mutation changed source proxy");
            });
        }
        foreach(DxfVersion version in SupportedVersions)foreach(bool binary in new[]{false,true})foreach(bool trace in new[]{false,true})for(int index=0;index<6;index++)
        {
            int i=index;
            Run($"quad-mutation/wire/{version}/{binary}/{trace}/{i}",()=>
            {
                var entity=QuadMutationEntity(trace);entity.ProxyGraphics=QuadMutationProxy;
                var property=entity.GetType().GetProperty(QuadMutationProperties[i])!;property.SetValue(entity,i<4 ? (object)new Vector2(-8,9) : -9.0);
                Check(entity.ProxyGraphics==null,"Edited quad retained proxy");var doc=new DxfDocument(version);doc.Comments.Clear();doc.Entities.Add(entity);
                using var stream=new MemoryStream();Check(doc.Save(stream,binary),"Quad mutation save");byte[] bytes=stream.ToArray();
                var raw=LoadRaw(bytes);var record=RawQuadRecord(raw,trace);Check(!record.Tags.Any(t=>t.Code is 92 or 160 or 310),"Stale quad proxy emitted");
                stream.Position=0;var loaded=DxfDocument.Load(stream)!;EntityObject restored=trace ? loaded.Entities.Traces.Single() : loaded.Entities.Solids.Single();
                foreach(string name in QuadMutationProperties){var p=entity.GetType().GetProperty(name)!;CheckQuadMutationValue(p.GetValue(entity)!,p.GetValue(restored)!);}
                Check(restored.ProxyGraphics==null,"Stale proxy restored");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"quad-mutation-{version}-{binary}-{trace}-{i}.dxf"),bytes);
            });
        }
    }
}
