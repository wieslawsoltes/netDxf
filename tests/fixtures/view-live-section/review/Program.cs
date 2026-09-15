using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var documentType = assembly.GetType("netDxf.DxfDocument")!;
var load = documentType.GetMethod("Load", new[] { typeof(Stream) })!;
var save = documentType.GetMethod("Save", new[] { typeof(Stream), typeof(bool) })!;
var entityType = assembly.GetType("netDxf.Entities.EntityObject")!;
var sectionType = assembly.GetType("netDxf.Entities.Section")!;
object Read(string path) => load.Invoke(null, new object[] { new MemoryStream(File.ReadAllBytes(path)) }) ?? throw new Exception("Load returned null");
object Get(object o, string property) => o.GetType().GetProperty(property)!.GetValue(o)!;
object Section(object doc) => ((IEnumerable)Get(Get(doc,"Entities"),"All")).Cast<object>().Single(x => Get(x,"CodeName").Equals("SECTIONOBJECT"));
var result = new List<object>();
foreach (string path in Directory.GetFiles(args[1],"ezdxf-view-live-section-*.dxf").Order())
{
    object doc = Read(path); bool binary = path.Contains("binary");
    using var output = new MemoryStream();
    if (!(bool)save.Invoke(doc,new object[]{output,binary})!) throw new Exception("Save failed");
    var rawType = assembly.GetType("netDxf.IO.DxfRawDocument")!;
    output.Position = 0;
    var rawLoad = rawType.GetMethods().Single(m => m.Name == "Load" && m.GetParameters()[0].ParameterType == typeof(Stream));
    object?[] rawArgs = Enumerable.Repeat<object?>(Type.Missing,rawLoad.GetParameters().Length).ToArray(); rawArgs[0] = output;
    object raw = rawLoad.Invoke(null,rawArgs)!;
    int references = ((IEnumerable)Get(raw,"Tags")).Cast<object>().Count(t => Convert.ToInt32(Get(t,"Code")) == 334);
    object entities = Get(doc,"Entities"); bool removed = (bool)entities.GetType().GetMethod("Remove",new[]{entityType})!.Invoke(entities,new[]{Section(doc)})!;
    doc = Read(path); object database = Get(doc,"Objects"); bool erased = false;
    try { database.GetType().GetMethod("EraseSection",new[]{sectionType})!.Invoke(database,new[]{Section(doc)}); erased = true; }
    catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
    result.Add(new { file=Path.GetFileName(path), binary, roundtrip_public_334_fields=references, referenced_section_removed=removed, referenced_section_erased=erased });
}
File.WriteAllText(args[2],JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(result));
