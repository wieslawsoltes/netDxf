using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
string output=args.Length==0?"producer-output":args[0];Directory.CreateDirectory(output);
foreach(int year in new[]{2000,2004,2007,2010,2013,2018}) foreach(bool ascii in new[]{true,false})
{
 var file=new DxfFile();file.Header.Version=Enum.Parse<DxfAcadVersion>("R"+year);
 var parent=new DxfUcs("SURVEY_BASE"){Handle=new DxfHandle(0xD100),Origin=new DxfPoint(12.5,-4.25,7.75),XAxis=new DxfVector(2,0,0),YAxis=new DxfVector(0,3,0),Elevation=4.5};
 var child=new DxfUcs("LEFT_FROM_SURVEY"){Handle=new DxfHandle(0xD101),Origin=new DxfPoint(100,200,300),XAxis=new DxfVector(0,2,0),YAxis=new DxfVector(0,0,3),OrthographicViewType=(DxfOrthographicViewType)5,BaseUcsHandle=parent.Handle,Elevation=-8.5,OrthographicType=(DxfOrthographicViewType)6,OrthographicOrigin=new DxfPoint(7,8,9)};
 var world=new DxfUcs("BOTTOM_FROM_WORLD"){Handle=new DxfHandle(0xD102),YAxis=DxfVector.YAxis,OrthographicViewType=(DxfOrthographicViewType)2};
 file.UserCoordinateSystems.Add(child);file.UserCoordinateSystems.Add(parent);file.UserCoordinateSystems.Add(world);file.Entities.Add(new DxfLine(new DxfPoint(1,2,3),new DxfPoint(4,5,6)));
 string path=Path.Combine(output,$"ixmilia-ucs-base-R{year}-{(ascii?"ascii":"binary")}.dxf");file.Save(path,ascii);
 // The producer allocates table handles during Save; bind the relationship after that allocation.
 child.BaseUcsHandle=parent.Handle; file.Save(path,ascii);
 var loaded=DxfFile.Load(path);var copy=loaded.UserCoordinateSystems.Single(x=>x.Name==child.Name);if(copy.BaseUcsHandle.Value!=parent.Handle.Value || (int)copy.OrthographicViewType!=5)throw new Exception("Producer lost stored relationship.");Console.WriteLine(path);
}
