using System.Collections.Immutable;
using netDxf.Shadow.Symbols;

namespace netDxf.Shadow.Piping;

public static class PipingSymbols
{
    public static SymbolCatalog Catalog { get; } = new(Definitions());
    private static IEnumerable<SymbolDefinition> Definitions()
    {
        SymbolStroke L(double x,double y,double x2,double y2)=>new(SymbolStrokeKind.Line,new(x,y),new(x2,y2));
        SymbolPort P(string id,double x,double y,double dx,double dy)=>new(id,"process",new(x,y),new(dx,dy));
        SymbolDefinition D(string id,string name,ImmutableArray<SymbolStroke> geometry,ImmutableArray<SymbolPort> ports)
            =>new("piping."+id,"1.0.0",name,name,"fitting","Millimeters",Vec3.Zero,["Schematic piping convention"],
                [new("TAG","Tag",Required:true,Visible:true,Position:new(-8,-14)),new("SPEC","Specification","CS",true),
                    new("DN","Nominal size","50",true)],ports,geometry);
        yield return D("elbow90","90 degree elbow",[L(-10,0,-4,0),new(SymbolStrokeKind.Arc,new(-4,4),Radius:4,StartAngle:270,EndAngle:360),L(0,4,0,10)],
            [P("inlet",-10,0,-1,0),P("outlet",0,10,0,1)]);
        yield return D("tee","Three-way tee",[L(-10,0,10,0),L(0,0,0,10)],[P("inlet",-10,0,-1,0),P("outlet",10,0,1,0),P("branch",0,10,0,1)]);
        yield return D("reducer","Concentric reducer",[L(-10,-5,10,-2.5),L(-10,5,10,2.5),L(-10,-5,-10,5),L(10,-2.5,10,2.5)],
            [P("inlet",-10,0,-1,0),P("outlet",10,0,1,0)]);
        yield return D("flange","Flanged connection",[L(-8,0,-1,0),L(1,0,8,0),L(-1,-5,-1,5),L(1,-5,1,5)],
            [P("inlet",-8,0,-1,0),P("outlet",8,0,1,0)]);
    }
}
