// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Collections;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private sealed class BoundaryEdgeSequence : IEnumerable<HatchBoundaryPath.Edge>
    {
        private readonly HatchBoundaryPath.Edge[] items;
        private readonly int mode;
        internal int Starts, Moves, Disposals, ThrowAt = -1;
        internal readonly Exception Failure = new InvalidOperationException("edge producer failed");
        internal BoundaryEdgeSequence(HatchBoundaryPath.Edge[] items, int mode)
        { this.items = items; this.mode = mode; }
        public IEnumerator<HatchBoundaryPath.Edge> GetEnumerator()
        {
            Starts++;
            if (mode == 0 && Starts != 1) throw new InvalidOperationException("Edge sequence consumed twice");
            return Enumerate(Starts).GetEnumerator();
        }
        private IEnumerable<HatchBoundaryPath.Edge> Enumerate(int pass)
        {
            try
            {
                int count = mode == 2 && pass > 1 ? 0 : items.Length;
                for (int i = 0; i <= count; i++)
                {
                    Moves++; if (i == ThrowAt) throw Failure;
                    if (i == count) yield break;
                    yield return items[i];
                }
            }
            finally { Disposals++; }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private static HatchBoundaryPath.Edge[] BoundaryInput(int kind)
    {
        var p = new HatchBoundaryPath.Polyline { IsClosed = kind != 3,
            Vertexes = kind == 3 ? new[] { new Vector3(0,0,0), new Vector3(4,0,0), new Vector3(4,3,0) }
                : new[] { new Vector3(0,0,0), new Vector3(4,0,0), new Vector3(4,3,0), new Vector3(0,3,0) } };
        return kind switch
        {
            0 => Array.Empty<HatchBoundaryPath.Edge>(),
            1 => new HatchBoundaryPath.Edge[] { p },
            2 => new HatchBoundaryPath.Edge[] {
                new HatchBoundaryPath.Line { Start=new(0,0), End=new(4,0) },
                new HatchBoundaryPath.Line { Start=new(4,0), End=new(4,3) },
                new HatchBoundaryPath.Line { Start=new(4,3), End=new(0,3) },
                new HatchBoundaryPath.Line { Start=new(0,3), End=new(0,0) } },
            3 => new HatchBoundaryPath.Edge[] { p, new HatchBoundaryPath.Line { Start=new(4,3), End=new(0,0) } },
            _ => new HatchBoundaryPath.Edge[] { new HatchBoundaryPath.Arc { Center=new(1,2), Radius=3, StartAngle=0, EndAngle=360, IsCounterclockwise=true } }
        };
    }
    private static void BoundaryInputCheck(HatchBoundaryPath path, int kind)
    {
        Equal(kind switch {0=>0,1=>1,2=>4,3=>3,_=>1},path.Edges.Count,"Boundary edge count");
        Equal(kind==1,(path.PathType&HatchBoundaryPathTypeFlags.Polyline)!=0,"Polyline classification");
        if(kind==1)Check(path.Edges.Single() is HatchBoundaryPath.Polyline,"Lone polyline must not explode");
        if(kind==2||kind==3)Check(path.Edges.All(e=>e is HatchBoundaryPath.Line),"Mixed path line expansion");
        Equal(0,path.Entities.Count,"Raw edges unexpectedly gained source associations");
    }
    private static void RegisterHatchEdgeInputTests()
    {
        for(int kind=0;kind<5;kind++) for(int mode=0;mode<3;mode++)
        {
            int k=kind,m=mode; Run($"hatch-edge-input/sequence/{k}/{m}",()=>
            {
                var items=BoundaryInput(k);var input=new BoundaryEdgeSequence(items,m);var path=new HatchBoundaryPath(input);
                BoundaryInputCheck(path,k);Equal(1,input.Starts,"One input traversal");Equal(items.Length+1,input.Moves,"Exact move count");Equal(1,input.Disposals,"Disposal count");
                if(k!=3)for(int i=0;i<items.Length;i++)Check(ReferenceEquals(items[i],path.Edges[i]),"Ordinary edge identity changed");
                else Check(ReferenceEquals(items[1],path.Edges[^1]),"Mixed ordinary edge identity changed");
                if(items.Length>0){var first=path.Edges[0];items[0]=null!;Check(ReferenceEquals(first,path.Edges[0]),"Input array aliases path list");}
                var clone=(HatchBoundaryPath)path.Clone();BoundaryInputCheck(clone,k);
                for(int i=0;i<path.Edges.Count;i++)Check(!ReferenceEquals(path.Edges[i],clone.Edges[i]),"Clone edge alias");
            });
        }
        Run("hatch-edge-input/null-sequence",()=>
        {
            try{_=new HatchBoundaryPath((IEnumerable<HatchBoundaryPath.Edge>)null!);throw new Exception("Accepted null");}
            catch(ArgumentNullException e){Equal("edges",e.ParamName,"Null argument");}
        });
        foreach(int index in new[]{0,1,3}) Run($"hatch-edge-input/null-item/{index}",()=>
        {
            var items=BoundaryInput(2);items[index]=null!;var input=new BoundaryEdgeSequence(items,1);
            try{_=new HatchBoundaryPath(input);throw new Exception("Accepted null edge");}
            catch(ArgumentException e){Equal("edges",e.ParamName,"Null edge argument");}
            Equal(1,input.Starts,"Null edge traversal");Equal(1,input.Disposals,"Null edge disposal");
        });
        foreach(int at in new[]{0,2,4})Run($"hatch-edge-input/producer-failure/{at}",()=>
        {
            var input=new BoundaryEdgeSequence(BoundaryInput(2),0){ThrowAt=at};Exception? error=null;
            try{_=new HatchBoundaryPath(input);}catch(Exception e){error=e;}
            Check(ReferenceEquals(error,input.Failure),"Changed producer exception");Equal(1,input.Starts,"Failure traversal");Equal(1,input.Disposals,"Failure disposal");
        });
        for(int kind=1;kind<=3;kind++)foreach(var version in SupportedVersions)foreach(bool binary in new[]{false,true})
        {
            int k=kind;Run($"hatch-edge-input/wire/{k}/{version}/{binary}",()=>
            {
                var input=new BoundaryEdgeSequence(BoundaryInput(k),0);var path=new HatchBoundaryPath(input);
                var hatch=new Hatch(HatchPattern.Solid,new[]{path},false);var document=new DxfDocument(version);document.Entities.Add(hatch);
                using var stream=new MemoryStream();Check(document.Save(stream,binary),"Boundary save");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"hatch-edge-input-{k}-{version}-{binary}.dxf"),stream.ToArray());
                stream.Position=0;var loaded=DxfDocument.Load(stream)??throw new Exception("Boundary load");
                BoundaryInputCheck(loaded.Entities.Hatches.Single().BoundaryPaths.Single(),k);Equal(1,input.Starts,"Saving re-enumerated caller");
            });
        }
    }
}
