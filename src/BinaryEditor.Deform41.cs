using System.Text.Json.Nodes;
namespace IndependentDev;

internal sealed partial class BinaryEditor
{
    internal bool CanEditDeform41=>PreservesAnimations&&playback41 is not null;
    void ValidateDeformOnly41(BinaryDocument candidate)
    {
        if(playback41 is null)throw new NotSupportedException("Opaque animation bytes cannot be edited");
        var next=candidate.ReadAnimations41();var before=Document.UnchangedBytes();var after=candidate.UnchangedBytes();
        bool Same(int start,int length,int other,int otherLength)=>length==otherLength&&before.AsSpan(start,length).SequenceEqual(after.AsSpan(other,otherLength));
        if(!Same(0,playback41.Span.Offset,0,next.Span.Offset)||next.Items.Length!=playback41.Items.Length)throw new InvalidDataException("Deform edit changed setup");
        for(var i=0;i<next.Items.Length;i++)
        {
            var a=playback41.Items[i];var b=next.Items[i];var aEnd=a.CountSpan.Offset+a.CountSpan.Length;var bEnd=b.CountSpan.Offset+b.CountSpan.Length;var aTail=a.AttachmentSpan.Offset+a.AttachmentSpan.Length;var bTail=b.AttachmentSpan.Offset+b.AttachmentSpan.Length;
            if(a.Name!=b.Name||b.TimelineCount-a.TimelineCount!=b.Deforms.Length-a.Deforms.Length||!Same(a.Span.Offset,a.CountSpan.Offset-a.Span.Offset,b.Span.Offset,b.CountSpan.Offset-b.Span.Offset)||!Same(aEnd,a.AttachmentSpan.Offset-aEnd,bEnd,b.AttachmentSpan.Offset-bEnd)||!Same(aTail,a.Span.Offset+a.Span.Length-aTail,bTail,b.Span.Offset+b.Span.Length-bTail))throw new InvalidDataException("Deform edit changed another animation channel");
            foreach(var track in a.Deforms)if(!b.Deforms.Any(t=>t.Slot==track.Slot&&t.Name==track.Name))throw new InvalidDataException("Deform edit removed a timeline");
        }
    }
    internal Point MoveDeformedWorld41(string key,int vertex,Point target,string animation,double requestedTime)
        =>MoveDeformedWorldVertices41(key,new Dictionary<int,Point>{{vertex,target}},animation,requestedTime)[vertex];
    internal Dictionary<int,Point> MoveDeformedWorldVertices41(string key,IReadOnlyDictionary<int,Point> targets,string animation,double requestedTime)
    {
        if(!CanEditDeform41)throw new NotSupportedException("4.1 Deform requires a fully decoded animation body");foreach(var target in targets.Values)target.Validate();var time=(double)Float32(requestedTime);
        var model=ReadModel();var mesh=Playback.DeformTarget(model,model.Meshes[key]);if(targets.Count==0||targets.Keys.Any(vertex=>vertex<0||vertex>=mesh.UV.Length))throw new ArgumentOutOfRangeException(nameof(targets));
        var slot=Array.FindIndex(model.Slots,item=>item.Name==mesh.Slot);var info=playback41!.Items.Single(item=>item.Name==animation);var current=model.Vertices(mesh,model.Pose(animation,time),Playback.Deform(model,mesh,animation,time));if(targets.All(p=>p.Value==current[p.Key].Position))return targets.ToDictionary();
        var transient=new Editor(model.Data.ToJsonString(),recordHistory:false);transient.MoveDeformedVertices(key,targets,animation,time,createKey:true);
        var keys=transient.Document.Data["animations"]![animation]!["attachments"]![mesh.Skin]![mesh.Slot]![mesh.Name]!["deform"]!.AsArray();
        var binding=Document.ReadSetup4117().DefaultSkin.SlotGroups.Single(g=>g.SlotIndex==slot).Attachments.Single(item=>item.Placeholder==mesh.Name);var raw=Document.UnchangedBytes();using var timeline=new MemoryStream();UInt(timeline,binding.PlaceholderIndex);timeline.WriteByte(0);UInt(timeline,(uint)keys.Count);UInt(timeline,(uint)keys.Count(node=>node!["curve"] is JsonArray));
        for(var i=0;i<keys.Count;i++)
        {
            var frame=keys[i]!.AsObject();var at=Float32(Scene.Number(frame["time"]));if((double)at!=Scene.Number(frame["time"]))throw new NotSupportedException("Deform support key time is not representable as float32");if(i==0)Float(timeline,at);
            var values=Scene.Values(frame["vertices"]);DeformValues(timeline,values.Length==0?0:checked((uint)Scene.Number(frame["offset"])),values.Select(Float32).ToArray());
            if(i+1==keys.Count)continue;Float(timeline,Float32(Scene.Number(keys[i+1]!["time"])));var curve=Timeline.NormalizedCurve(frame);if(curve is JsonArray controls)
            {
                timeline.WriteByte(2);var numbers=Scene.Values(controls);var end=Scene.Number(keys[i+1]!["time"]);Float(timeline,Float32(at+numbers[0]*(end-at)));Float(timeline,Float32(numbers[1]));Float(timeline,Float32(at+numbers[2]*(end-at)));Float(timeline,Float32(numbers[3]));
            }
            else timeline.WriteByte(curve is null?(byte)0:(byte)1);
        }
        var tracks=info.Deforms.Select(track=>(track.Slot,track.Name,track.NameIndex,Bytes:raw.AsSpan(track.Span.Offset,track.Span.Length).ToArray())).ToList();var found=tracks.FindIndex(track=>track.Slot==slot&&track.Name==mesh.Name);var changed=(slot,mesh.Name,binding.PlaceholderIndex,timeline.ToArray());if(found>=0)tracks[found]=changed;else tracks.Add(changed);
        using var section=new MemoryStream();UInt(section,1);UInt(section,0);var groups=tracks.GroupBy(track=>track.Slot).OrderBy(group=>group.Key).ToArray();UInt(section,(uint)groups.Length);foreach(var group in groups){UInt(section,(uint)group.Key);UInt(section,(uint)group.Count());foreach(var track in group.OrderBy(track=>track.NameIndex))section.Write(track.Bytes);}
        using var count=new MemoryStream();UInt(count,(uint)(info.TimelineCount+(found<0?1:0)));using var output=new MemoryStream();output.Write(raw.AsSpan(0,info.CountSpan.Offset));output.Write(count.ToArray());var countEnd=info.CountSpan.Offset+info.CountSpan.Length;output.Write(raw.AsSpan(countEnd,info.AttachmentSpan.Offset-countEnd));output.Write(section.ToArray());output.Write(raw.AsSpan(info.AttachmentSpan.Offset+info.AttachmentSpan.Length));if(output.Length>Input.MaxBytes)throw new InvalidDataException("Deform edit size limit");
        var candidate=new BinaryDocument(output.ToArray());var actualModel=Model(candidate);var actualMesh=actualModel.Meshes[key];var actual=actualModel.Vertices(actualMesh,actualModel.Pose(animation,time),Playback.Deform(actualModel,actualMesh,animation,time));foreach(var (vertex,target) in targets)Editor.RequireTarget(actual[vertex].Position,target,current[vertex].Position);ValidateDeformOnly41(candidate);Commit(candidate);return targets.Keys.ToDictionary(vertex=>vertex,vertex=>actual[vertex].Position);
    }
}
