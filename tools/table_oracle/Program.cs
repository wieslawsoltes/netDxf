using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;

CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
var reports=new List<object>();
int failures=0;
foreach(var path in args) {
 var messages=new List<object>();
 try {
 var doc=DxfReader.Read(path,new DxfReaderConfiguration { Failsafe=false, KeepUnknownNonGraphicalObjects=true, KeepUnknownEntities=true, CreateDefaults=false }, (_,e)=>messages.Add(new { kind=e.NotificationType.ToString(), e.Message, exception=e.Exception?.ToString() }));
 var tables=doc.Entities.OfType<TableEntity>().Select(t=>new {
  handle=t.Handle.ToString("X"), owner=t.Owner?.Handle.ToString("X"), block=t.Block?.Handle.ToString("X"), blockName=t.Block?.Name, style=t.Style?.Handle.ToString("X"), styleName=t.Style?.Name,
  version=t.Version, position=t.InsertPoint.ToString(), direction=t.HorizontalDirection.ToString(),
  content=Content(t), columnWidths=t.Columns.Select(c=>c.Width).ToArray(), rowHeights=t.Rows.Select(r=>r.Height).ToArray(),
  cells=t.Rows.Select(r=>r.Cells.Select(c=>new { type=c.Type.ToString(), state=c.StateFlags.ToString(), c.MergedValue, c.HasLinkedData, c.ToolTip, style=c.Style?.Name, contents=c.Contents.Select(v=>new { contentType=v.ContentType.ToString(), valueType=v.CadValue.ValueType.ToString(), v.CadValue.Flags, units=v.CadValue.Units.ToString(), v.CadValue.Format, v.CadValue.FormattedValue, value=Value(v.CadValue.Value), textStyle=v.Format.TextStyle?.Handle.ToString("X"), v.Format.TextHeight, v.Format.ValueFormatString }).ToArray() }).ToArray()).ToArray()
 }).ToArray();
 if(tables.Length==0) throw new Exception("No typed TABLE records.");
 reports.Add(new { path, sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), version=doc.Header.Version.ToString(), tables, backingContents=Backing(doc,path), messages, passed=true });
 Console.Error.WriteLine($"PASS {Path.GetFileName(path)} typedTables={tables.Length} notifications={messages.Count}");
 } catch(Exception ex) { failures++; reports.Add(new {path,messages,passed=false,error=ex.ToString()}); Console.Error.WriteLine($"FAIL {Path.GetFileName(path)}: {ex.Message}"); }
}
File.WriteAllText("snapshots.json",JsonSerializer.Serialize(reports,new JsonSerializerOptions {WriteIndented=true}));
return failures==0?0:1;
static object? Value(object? value) => value switch { null=>null, string s=>s, int i=>i, double d=>d, IHandledCadObject c=>new {handle=c.Handle.ToString("X"),type=c.GetType().Name}, _=>value.ToString() };
static object? Content(TableEntity table) {
 var content=(TableContent?)typeof(TableEntity).GetProperty("Content",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(table);
 return content==null?null:new {handle=content.Handle.ToString("X"),owner=content.Owner?.Handle.ToString("X"),kind=content.GetType().Name};
}

static object[] Backing(CadDocument doc,string path) {
 var lines=File.ReadAllLines(path); var handles=new List<ulong>();
 for(int i=0;i+1<lines.Length;i+=2) if(lines[i].Trim()=="0" && lines[i+1].Trim()=="TABLECONTENT") {
  for(int j=i+2;j+1<lines.Length && lines[j].Trim()!="0";j+=2) if(lines[j].Trim()=="5") { handles.Add(Convert.ToUInt64(lines[j+1].Trim(),16)); break; }
 }
 return handles.Select(h=> { var c=doc.GetCadObject<TableContent>(h); return (object)new {handle=h.ToString("X"),found=c!=null, owner=c?.Owner?.Handle.ToString("X"),style=c?.Style?.Handle.ToString("X"), columnWidths=c?.Columns.Select(x=>x.Width).ToArray(),rowHeights=c?.Rows.Select(x=>x.Height).ToArray(), values=c?.Rows.Select(r=>r.Cells.Select(cell=>cell.Contents.Select(v=>new {contentType=v.ContentType.ToString(),valueType=v.CadValue.ValueType.ToString(),v.CadValue.Flags,units=v.CadValue.Units.ToString(),v.CadValue.Format,v.CadValue.FormattedValue,value=Value(v.CadValue.Value)}).ToArray()).ToArray()).ToArray()}; }).ToArray();
}
