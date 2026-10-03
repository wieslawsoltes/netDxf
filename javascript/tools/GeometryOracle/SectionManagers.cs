// Test-only enumerator/loader fixtures and observations of the unchanged C# APIs.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.IO;
internal static partial class Program
{
    private static object ManagerSnapshot(DxfStoredSectionManager manager) {
        object? Reference(DxfObject? item)=>item==null?null:new {type=item.GetType().Name,handle=item.Handle,owner=item.Owner?.Handle};
        return new {common=Reference(manager),version=(int)manager.SourceVersion,update=manager.RequiresFullUpdate,
            erased=manager.IsErased,registered=manager.Database!=null,sections=manager.Sections.Select(Reference).ToArray(),
            tags=manager.Tags.Select(tag=>new {code=tag.Code,value=tag.Value}).ToArray(),reactors=manager.PersistentReactors.Select(Reference).ToArray()};
    }
    private sealed class ManagerEnumerable : IEnumerable<Section> {
        private readonly Section[] members;private readonly int count;private readonly int[] counters;private readonly Action<string> hook;
        public ManagerEnumerable(Section[] members,int count,int[] counters,Action<string> hook){this.members=members;this.count=count;this.counters=counters;this.hook=hook;}
        public IEnumerator<Section> GetEnumerator(){counters[0]++;hook("get");return new Enumerator(this);}
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        private sealed class Enumerator : IEnumerator<Section> {
            private readonly ManagerEnumerable owner;private int at=-1;
            internal Enumerator(ManagerEnumerable owner){this.owner=owner;}
            public bool MoveNext(){owner.counters[1]++;owner.hook("move");return ++at<owner.count;}
            public Section Current {get{owner.counters[2]++;owner.hook("current");return owner.members[at%owner.members.Length];}}
            object IEnumerator.Current=>Current;
            public void Reset(){throw new NotSupportedException();}
            public void Dispose(){owner.counters[3]++;owner.hook("dispose");}
        }
    }
    private static object? ManagerCall(JsonElement step,object target) {
        var input=step.GetProperty("members");Section[]? members=input.ValueKind==JsonValueKind.Null?null:input.EnumerateArray().Select(v=>(Section)Read(v)!).ToArray();
        int count=step.TryGetProperty("repeat",out var repeat)?repeat.GetInt32():members?.Length??0;var counters=new int[5];
        if(step.TryGetProperty("log",out var log))Values[log.GetString()!]=counters;
        bool flag=step.GetProperty("flag").GetBoolean();
        object? Invoke(IEnumerable<Section>? values){if(step.GetProperty("action").GetString()=="replace"){((DxfStoredSectionManager)target).ReplaceSections(values!,flag);return null;}return ((DxfObjectDatabase)target).CreateSectionManager(values!,flag);}
        void Hook(string stage){
            if(!step.TryGetProperty("hooks",out var hooks))return;
            foreach(var action in hooks.EnumerateArray()){
                if(action.GetProperty("stage").GetString()!=stage)continue;
                string kind=action.GetProperty("kind").GetString()!;
                if(kind=="throw")throw new InvalidOperationException("Injected caller failure.");
                if(kind=="reenter"||kind=="catch-reenter"){try{Invoke(Array.Empty<Section>());}catch{if(kind=="reenter")throw;counters[4]++;}}
                else{object subject=Read(action.GetProperty("target"))!;string member=action.GetProperty("member").GetString()!;
                    if(kind=="set")RetainedPut(subject,member,Read(action.GetProperty("value")));
                    else{var args=Arguments(action);subject.GetType().GetMethods().Where(m=>m.Name==member).Single(m=>Matches(m.GetParameters(),args)).Invoke(subject,args);}
                }
            }
        }
        return Invoke(members==null?null:new ManagerEnumerable(members,count,counters,Hook));
    }
    private static object ManagerLoad(JsonElement step,DxfDocument document){
        var members=step.GetProperty("members").EnumerateArray().Select(v=>(Section)Read(v)!).ToArray();
        string[] handles=step.TryGetProperty("handles",out var hs)?hs.EnumerateArray().Select(v=>v.GetString()!).ToArray():members.Select(s=>s.Handle).ToArray();
        bool flag=step.TryGetProperty("flag",out var fl)&&fl.GetBoolean();
        var tags=new List<DxfTag>{new DxfTag(100,"AcDbSectionManager"),new DxfTag(70,flag?(short)1:(short)0),new DxfTag(90,handles.Length)};tags.AddRange(handles.Select(h=>new DxfTag(330,h)));
        var manager=(DxfStoredSectionManager)Activator.CreateInstance(typeof(DxfStoredSectionManager),BindingFlags.Instance|BindingFlags.NonPublic,null,
            new object[]{document,step.TryGetProperty("code",out var code)?code.GetString()!:"SECTIONMANAGER",tags,flag,handles},null)!;
        var db=document.Objects;RetainedPut(manager,"Owner",db.Root);manager.PersistentReactors.Add(db.Root);
        typeof(DxfObjectDatabase).GetMethod("Register",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(db,new object[]{manager,false});
        typeof(DxfDictionary).GetMethod("AddLoaded",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(db.Root,new object[]{step.TryGetProperty("anchor",out var anchor)?anchor.GetString()!:"ACAD_SECTION_MANAGER",manager,step.TryGetProperty("hardOwner",out var hard)&&hard.GetBoolean()});
        typeof(DxfStoredSectionManager).GetMethod("Resolve",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(manager,new object[]{(Func<string,DxfObject>)(h=>document.GetObjectByHandle(h))});
        return manager;
    }
}
