using System.Collections.Immutable;
using netDxf.Shadow.Symbols;

namespace netDxf.Shadow.Electrical;

public static class ElectricalSymbols
{
    public static SymbolCatalog Catalog { get; } = new(Definitions());
    private static IEnumerable<SymbolDefinition> Definitions()
    {
        SymbolStroke L(double x,double y,double x2,double y2)=>new(SymbolStrokeKind.Line,new(x,y),new(x2,y2));
        SymbolPort P(string id,double x,double y,double dx,double dy)=>new(id,"electrical",new(x,y),new(dx,dy));
        SymbolDefinition D(string id,string name,ImmutableArray<SymbolStroke> geometry,ImmutableArray<SymbolPort> ports)
            =>new("electrical."+id,"1.0.0",name,name,"device","Millimeters",Vec3.Zero,["IEC 60617 reference; original schematic artwork"],
                [new("TAG","Reference designation",Required:true,Visible:true,Position:new(-7,-13)),new("CIRCUIT","Circuit","C1",true)],ports,geometry);
        yield return D("terminal","Terminal",[new(SymbolStrokeKind.Circle,Vec3.Zero,Radius:2),L(-7,0,-2,0),L(2,0,7,0)],
            [P("1",-7,0,-1,0),P("2",7,0,1,0)]);
        yield return D("switch","Normally open switch",[L(-10,0,-5,0),new(SymbolStrokeKind.Circle,new(-4,0),Radius:1),L(-3,0,5,5),L(5,0,10,0)],
            [P("1",-10,0,-1,0),P("2",10,0,1,0)]);
        yield return D("resistor","Resistor",[L(-12,0,-7,0),L(-7,-3,-7,3),L(-7,3,7,3),L(7,3,7,-3),L(7,-3,-7,-3),L(7,0,12,0)],
            [P("1",-12,0,-1,0),P("2",12,0,1,0)]);
        yield return D("motor","Motor",[new(SymbolStrokeKind.Circle,Vec3.Zero,Radius:8),new(SymbolStrokeKind.Text,new(-2,-2),Radius:5,Text:"M"),L(-13,0,-8,0),L(8,0,13,0)],
            [P("L",-13,0,-1,0),P("N",13,0,1,0)]);
        yield return D("earth","Protective earth",[L(0,8,0,0),L(-6,0,6,0),L(-4,-2,4,-2),L(-2,-4,2,-4)],
            [P("PE",0,8,0,1)]);
    }
}
