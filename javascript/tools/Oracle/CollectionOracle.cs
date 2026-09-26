// Development-only scripted calls into the actual production ObservableCollection<int>.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using netDxf.Collections;
internal static class CollectionOracle
{
    internal static object Execute(JsonElement input)
    {
        var answers=new List<object>();
        foreach(var scenario in input.GetProperty("scenarios").EnumerateArray()) {
            var items=new ObservableCollection<int>();
            items.AddRange(scenario.GetProperty("initial").EnumerateArray().Select(x=>x.GetInt32()));
            var events=new List<string>();string mode="";IEnumerator<int>? iterator=null;
            items.BeforeAddItem+=(_,e)=>{events.Add("before-add:"+e.Item);if(mode=="cancel-add")e.Cancel=true;if(mode=="throw-add")throw new NotSupportedException("injected");};
            items.AddItem+=(_,e)=>events.Add("add:"+e.Item);
            items.BeforeRemoveItem+=(_,e)=>{events.Add("before-remove:"+e.Item);if(mode=="cancel-remove")e.Cancel=true;if(mode=="throw-remove")throw new NotSupportedException("injected");};
            items.RemoveItem+=(_,e)=>events.Add("remove:"+e.Item);
            var steps=new List<object>();
            foreach(var op in scenario.GetProperty("operations").EnumerateArray()) {
                events.Clear();mode=op.TryGetProperty("mode",out var m)?m.GetString()!:"";
                object? result=null;string? error=null,param=null;
                int N(string key)=>op.GetProperty(key).GetInt32();
                try { switch(op.GetProperty("method").GetString()) {
                    case "Add":items.Add(N("value"));break;
                    case "AddRange":items.AddRange(op.GetProperty("values").EnumerateArray().Select(x=>x.GetInt32()));break;
                    case "AddSelf":items.AddRange(items);break;
                    case "Insert":items.Insert(N("index"),N("value"));break;
                    case "Remove":result=items.Remove(N("value"));break;
                    case "RemoveAt":items.RemoveAt(N("index"));break;
                    case "Clear":items.Clear();break;
                    case "Get":result=items[N("index")];break;
                    case "Set":items[N("index")]=N("value");break;
                    case "Reverse":items.Reverse();break;
                    case "Sort":items.Sort();break;
                    case "SortModulo":items.Sort((a,b)=>(a%5).CompareTo(b%5));break;
                    case "SortRange":items.Sort(N("index"),N("count"),Comparer<int>.Default);break;
                    case "Contains":result=items.Contains(N("value"));break;
                    case "IndexOf":result=items.IndexOf(N("value"));break;
                    case "Enumerate":iterator=items.GetEnumerator();result=iterator.Current;break;
                    case "MoveNext":result=new {moved=iterator!.MoveNext(),current=iterator.Current};break;
                    case "Reset":iterator!.Reset();result=iterator.Current;break;
                    case "CopyTo":var output=new int[N("length")];items.CopyTo(output,N("index"));result=output;break;
                    default:throw new ArgumentException("Unknown collection operation.");
                }} catch(Exception e) {error=e.GetType().Name;param=(e as ArgumentException)?.ParamName;}
                steps.Add(new{result,error,paramName=param,items=items.ToArray(),events=events.ToArray()});
            }
            answers.Add(steps);
        }
        return answers;
    }
}
