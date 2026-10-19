using System.Collections.Immutable;
using netDxf.Shadow.Symbols;

namespace netDxf.Shadow.Pid;

/// <summary>Original schematic artwork with cited symbol conventions; not a licensed standards database.</summary>
public static class PidSymbols
{
    public static SymbolCatalog Catalog { get; } = new(Definitions());
    private static IEnumerable<SymbolDefinition> Definitions()
    {
        SymbolStroke L(double x,double y,double x2,double y2)=>new(SymbolStrokeKind.Line,new(x,y),new(x2,y2));
        SymbolPort P(string id,double x,double y,double dx,double dy)=>new(id,"process",new(x,y),new(dx,dy));
        SymbolDefinition D(string id,string name,string kind,ImmutableArray<SymbolStroke> geometry,ImmutableArray<SymbolPort> ports,Vec3 tag)
            =>new("pid."+id,"1.0.0",name,name,kind,"Millimeters",Vec3.Zero,["EN ISO 10628-2"],[new("TAG","Tag",Required:true,Visible:true,Position:tag)],ports,geometry);
        yield return D("tank.storage","Storage tank","equipment",
            [L(-25,0,-25,52),L(-25,58,-25,65),L(-25,65,-3,76),L(3,76,25,65),L(25,65,25,13),L(25,7,25,0),L(25,0,3,0),L(-3,0,-25,0),
             L(-30,55,-25,55),L(25,10,30,10),L(0,76,0,82),L(0,0,0,-5)],
            [P("inlet",-30,55,-1,0),P("outlet",30,10,1,0),P("vent",0,82,0,1),P("drain",0,-5,0,-1),new("level","signal",new(25,41),new(1,0))],new(-7,30));
        yield return D("pump.liquid","Liquid pump","equipment",
            [new(SymbolStrokeKind.Circle,Vec3.Zero,Radius:11),L(-5,-6,7,0),L(7,0,-5,6),L(-5,6,-5,-6),L(-16,0,-11,0),L(11,0,16,0)],
            [P("inlet",-16,0,-1,0),P("outlet",16,0,1,0)],new(-7,-20));
        yield return D("valve.manual","Manual isolation valve","valve",
            [L(-6,-4,-6,4),L(-6,4,6,-4),L(6,-4,6,4),L(6,4,-6,-4),L(0,0,0,8),L(-3,8,3,8)],
            [P("inlet",-6,0,-1,0),P("outlet",6,0,1,0)],new(-7,-13));
        yield return D("valve.check","Check valve","valve",
            [L(-6,-4,-6,4),L(-6,4,3,0),L(3,0,-6,-4),L(3,-5,3,5),L(3,0,6,0)],
            [P("inlet",-6,0,-1,0),P("outlet",6,0,1,0)],new(-9,-13));
        yield return new("pid.instrument.pce","1.0.0","PCE request","Process control engineering request","instrument","Millimeters",Vec3.Zero,["EN 62424"],
            [new("TAG","Tag",Required:true),new("FUNCTION","Function","PI",true,true,new(-3,2.5),3),new("LOOP","Loop","101",true,true,new(-3,-3),3)],
            [new("measurement","signal",new(0,-7.5),new(0,-1))],[new(SymbolStrokeKind.Ellipse,Vec3.Zero,new(7.5,0),11)]);
    }
}
