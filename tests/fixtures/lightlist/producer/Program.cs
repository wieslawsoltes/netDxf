using IxMilia.Dxf;
using IxMilia.Dxf.Objects;
using IxMilia.Dxf.Entities;
var output = args.Length == 0 ? "." : args[0];
Directory.CreateDirectory(output);
foreach(var year in new[]{2007,2010,2013,2018})
foreach(var text in new[]{true,false}) {
    var file=new DxfFile();
    file.Header.Version=Enum.Parse<DxfAcadVersion>("R"+year);
    var a=new DxfLight { Name="Actual light A", Position=new DxfPoint(1.25,-2.5,4), TargetLocation=new DxfPoint(5,7,-1), LightType=DxfLightType.Point, ShadowMapSize=512, HotspotAngle=30, FalloffAngle=60, Transparency=0x02000000 };
    var b=new DxfLight { Name="Actual light B", Position=new DxfPoint(-8,9,2), TargetLocation=new DxfPoint(2,4,6), LightType=DxfLightType.Spot, ShadowMapSize=1024, HotspotAngle=35, FalloffAngle=70, Transparency=0x02000000 };
    file.Entities.Add(a); file.Entities.Add(b);
    file.Entities.Add(new DxfLine(new DxfPoint(21,22,23),new DxfPoint(31,32,33)) { Transparency=0x02000000 });
    var root=file.NamedObjectDictionary;
    var lists=new DxfDictionary { IsHardOwner=true };
    root.Add("QA_LIGHTLISTS",lists); file.Objects.Add(lists);
    foreach(var version in new[]{int.MinValue,-7,0,42,int.MaxValue}) {
        var list=new DxfLightList { Version=version };
        if(version != 0) { list.Lights.Add(a);list.Lights.Add(b);list.Lights.Add(a); }
        lists.Add("VERSION_"+version,list); file.Objects.Add(list);
    }
    file.Save(Path.Combine(output,$"ixmilia-lightlist-R{year}-{(text?"ascii":"binary")}.dxf"),text);
}
