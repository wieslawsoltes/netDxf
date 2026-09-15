using System.Collections;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

if (args.Length != 2) { Console.Error.WriteLine("Usage: probe.dll INPUT_NATIVE_CARRIER.dxf RESULT.json"); return 2; }
var source = Path.GetFullPath(args[0]);
var bytes = File.ReadAllBytes(source);
var results = new List<object>();
int failures = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
DxfDocument Load() { using var stream=new MemoryStream(bytes); return DxfDocument.Load(stream)??throw new Exception("Native carrier refused"); }
DxfTableStyle Style(DxfDocument d) => d.Objects.Items.OfType<DxfTableStyle>().Single();
string Value(DxfTag t) => t.Code+":"+(t.Value is double n?BitConverter.DoubleToInt64Bits(n).ToString("X16"):t.Value is byte[] a?Convert.ToHexString(a):t.Value?.ToString());
string[] Tags(DxfTableStyle s)=>s.Tags.Select(Value).ToArray();
void Run(string name, Action test) { try { test(); results.Add(new {name, passed=true}); }catch(Exception e){failures++;results.Add(new{name,passed=false,error=e.ToString()});Console.WriteLine("FAIL "+name+" "+e.Message);} }
DxfTableStyleRowValues Values(int row, double scalar)=>new(scalar,(short)(row==0?short.MinValue:short.MaxValue),(short)(row==1?short.MinValue:short.MaxValue),(short)(row==2?short.MinValue:short.MaxValue),row%2==0);
DxfTableStyleHeader Header(string text,double scalar)=>new(text,1,short.MinValue,scalar,scalar,true,false);
string[] descriptions={"", "\\", "\\U+005C", "\\U+0041\\u+03A9", "\\U+005CU+0041", "Ω😀字", "\\U+0000", "\\U+D800", new string('Ω',255), new string('\\',255)};
double[] scalars={0.0,-0.0,double.Epsilon,Math.BitIncrement(double.Epsilon),0.125,1e-280,1e280,double.MaxValue};
int[][] orders={new[]{0,1,2},new[]{2,1,0},new[]{1,2,0}};
foreach(var culture in new[]{"en-US","pl-PL","tr-TR"})
foreach(var text in descriptions)
foreach(var scalar in scalars)
{
  var label=$"metamorphic/{culture}/{Array.IndexOf(descriptions,text)}/{BitConverter.DoubleToInt64Bits(scalar):X16}";
  Run(label,()=>{
    CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture);
    var all=Load(); var sa=Style(all); var oldTags=sa.Tags; var oldRows=sa.Rows; var oldHeader=sa.Header; var oldRowValues=sa.Rows.Select(r=>r.Tags.Select(Value).ToArray()).ToArray(); var original=Tags(sa); var refs=sa.References.ToArray();
    sa.ReplaceStyle(Header(text,scalar),sa.Rows.Select((row,i)=>row.WithValues(Values(i,scalar))));
    Check(original.SequenceEqual(oldTags.Select(Value)),"old tags changed");Check(oldRows.Select((r,i)=>r.Tags.Select(Value).SequenceEqual(oldRowValues[i])).All(x=>x),"old rows unexpectedly mutated");
    Check(sa.Header.Description==text,"memory decoded text differs");Check(refs.SequenceEqual(sa.References),"dependency inventory changed");
    foreach(var order in orders){var split=Load();var ss=Style(split);foreach(int i in order)ss.ReplaceStyle(null,new[]{ss.Rows[i].WithValues(Values(i,scalar))});ss.ReplaceStyle(Header(text,scalar),Array.Empty<DxfTableStyleRowEdit>());Check(Tags(sa).SequenceEqual(Tags(ss)),"commutative edit differs");}
    foreach(bool binary in new[]{false,true}) {
      using var output=new MemoryStream();Check(all.Save(output,binary),"save refused");output.Position=0;var loaded=DxfDocument.Load(output)??throw new Exception("reload refused");var again=Style(loaded);
      Check(again.Header.Description==text,"serialized description differs");Check(BitConverter.DoubleToInt64Bits(again.Header.HorizontalCellMargin)==BitConverter.DoubleToInt64Bits(scalar),"serialized header double changed bits");
      for(int i=0;i<3;i++){var v=Values(i,scalar);Check(BitConverter.DoubleToInt64Bits(again.Rows[i].Values.TextHeight)==BitConverter.DoubleToInt64Bits(scalar),"serialized row double changed bits");Check(again.Rows[i].Values.CellAlignment==v.CellAlignment&&again.Rows[i].Values.StoredTextColor==v.StoredTextColor&&again.Rows[i].Values.StoredFillColor==v.StoredFillColor,"serialized signed values differ");}
      Check(again.Rows.All(r=>r.TextStyle!=null&&ReferenceEquals(loaded.GetObjectByHandle(r.TextStyle.Handle),r.TextStyle)),"reload text style identity wrong");
    }
  });
}
foreach(var phase in new[]{"get","move","current","dispose"}) Run("callback/"+phase,()=>{
  var doc=Load();var style=Style(doc);var original=Tags(style);var rows=style.Rows;var header=style.Header;var version=doc.DrawingVariables.AcadVer;
  var callback=new CallbackRows(style.Rows[0].WithValues(Values(0,2)),phase,()=>{try{style.ReplaceStyle(null,Array.Empty<DxfTableStyleRowEdit>());}catch(InvalidOperationException){}});
  bool refused=false;try{style.ReplaceStyle(Header("new",0),callback);}catch(InvalidOperationException){refused=true;}
  Check(refused,"caught callback reentry accepted");Check(original.SequenceEqual(Tags(style))&&ReferenceEquals(rows,style.Rows)&&ReferenceEquals(header,style.Header),"callback rejection mutated snapshot");
});
CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
var summary=new{source=Path.GetFileName(source),source_sha256=Convert.ToHexString(SHA256.HashData(bytes)),profile=Style(Load()).SourceVersion.ToString(),library_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DxfDocument).Assembly.Location))),cases=results.Count,failures,results};
File.WriteAllText(args[1],JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"CASES {results.Count} FAILURES {failures}");return failures==0?0:1;
class CallbackRows(DxfTableStyleRowEdit edit,string phase,Action callback):IEnumerable<DxfTableStyleRowEdit>{public IEnumerator<DxfTableStyleRowEdit> GetEnumerator(){if(phase=="get")callback();return new Iterator(edit,phase,callback);}IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();class Iterator(DxfTableStyleRowEdit edit,string phase,Action callback):IEnumerator<DxfTableStyleRowEdit>{int n;public DxfTableStyleRowEdit Current{get{if(phase=="current")callback();return edit;}}object IEnumerator.Current=>Current;public bool MoveNext(){if(phase=="move")callback();return n++==0;}public void Reset()=>throw new NotSupportedException();public void Dispose(){if(phase=="dispose")callback();}}}
