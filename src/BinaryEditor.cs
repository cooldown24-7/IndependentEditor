using System.Text.Json.Nodes;
namespace IndependentDev;

internal sealed partial class BinaryEditor
{
    public BinaryDocument Document {get;private set;}
    readonly LinkedList<(BinaryDocument Document,long Revision)> undo=new(),redo=new();
    readonly long historyLimit;
    readonly bool deformProfile;
    readonly bool rotationProfile;
    readonly bool setup4117Profile;
    BinaryAnimationBody41? playback41;
    Scene? cachedModel;
    internal BinaryEditor CopyForPreview()=>new(this);
    BinaryEditor(BinaryEditor source)
    {
        Document=source.Document;deformProfile=source.deformProfile;rotationProfile=source.rotationProfile;setup4117Profile=source.setup4117Profile;PreservesAnimations=source.PreservesAnimations;PreservedAnimationCount=source.PreservedAnimationCount;PlaybackError=source.PlaybackError;playback41=source.playback41;cachedModel=source.cachedModel;historyLimit=0;
    }
    internal string? PlaybackError {get;}
    internal bool PreservesAnimations {get;}
    internal uint PreservedAnimationCount {get;}
    internal bool AllowsRotation=>rotationProfile;
    internal bool IsSetup4117=>setup4117Profile;
    internal BinaryDeformBody3855 ReadBody()=>Body(Document);
    internal Scene ReadModel()=>Model(Document);
    long revision,savedRevision,nextRevision;
    public long Revision=>revision;
    public long SavedRevision=>savedRevision;
    public bool Dirty=>revision!=savedRevision;
    public int UndoCount=>undo.Count;
    public int RedoCount=>redo.Count;
    public long HistoryBytes {get;private set;}
    public bool HistoryTruncated {get;private set;}
    public BinaryEditor(byte[] bytes,long historyLimitBytes=Editor.MaxHistoryBytes):this(bytes,historyLimitBytes,false){}
    public static BinaryEditor ForDeform(byte[] bytes,long historyLimitBytes=Editor.MaxHistoryBytes)=>new(bytes,historyLimitBytes,true);
    public static BinaryEditor ForAnimation(byte[] bytes,long historyLimitBytes=Editor.MaxHistoryBytes)=>new(bytes,historyLimitBytes,true,true);
    public static BinaryEditor ForSetup4117(byte[] bytes,long historyLimitBytes=Editor.MaxHistoryBytes,bool preserveAnimations=false)=>new(bytes,historyLimitBytes,deform:false,setup4117:true,preserveAnimations:preserveAnimations);
    BinaryEditor(byte[] bytes,long historyLimitBytes,bool deform,bool rotation=false,bool setup4117=false,bool preserveAnimations=false)
    {
        if(historyLimitBytes is <0 or >Editor.MaxHistoryBytes)throw new ArgumentOutOfRangeException(nameof(historyLimitBytes));
        deformProfile=deform;rotationProfile=rotation;setup4117Profile=setup4117;PreservesAnimations=preserveAnimations;Document=new(bytes);Validate(Document);historyLimit=historyLimitBytes;PreservedAnimationCount=preserveAnimations?Document.ReadSetup4117().AnimationCount:0;
        if(PreservedAnimationCount>0)
        {
            try{playback41=Document.ReadAnimations41();}
            catch(Exception error)when(error is InvalidDataException or NotSupportedException or EndOfStreamException or ArgumentException){PlaybackError=error.Message;}
        }
    }
    static BinarySetup3855 Complete(BinaryDocument document)
    {var setup=document.ReadSetup3855();if(!setup.Complete)throw new NotSupportedException("Binary local editing requires complete nonanimated 3.8.55 profile");return setup;}
    BinarySetup4117 Complete4117(BinaryDocument document)
    {var setup=document.ReadSetup4117();if(!setup.Complete&&!PreservesAnimations)throw new NotSupportedException("Binary local editing requires complete nonanimated 4.1.17 profile");return setup;}
    BinaryDeformBody3855 Body(BinaryDocument document)=>setup4117Profile?throw new NotSupportedException("4.1.17 Deform editing pending"):rotationProfile?document.ReadAnimation3855():document.ReadDeform3855();
    Scene Model(BinaryDocument document)
    {
        if(ReferenceEquals(document,Document)&&cachedModel is not null)return cachedModel;
        var scene=playback41 is not null?BinaryScene.ReadAnimations41(document,ReferenceEquals(document,Document)?playback41:document.ReadAnimations41()):setup4117Profile?(PreservesAnimations?BinaryScene.ReadSetupPose41(document):BinaryScene.ReadStatic4117(document)):rotationProfile?BinaryScene.ReadAnimation(document):BinaryScene.Read(document);
        if(ReferenceEquals(document,Document))cachedModel=scene;return scene;
    }
    void Validate(BinaryDocument document){if(setup4117Profile)Complete4117(document);else if(deformProfile)Body(document);else Complete(document);}
    internal static float Float32(double value)
    {
        var actual=(float)value;if(!float.IsFinite(actual))throw new InvalidDataException("Binary coordinate float32 range");if(value!=0&&actual==0)throw new InvalidDataException("Binary coordinate float32 underflow");return actual;
    }
    static void UInt(Stream stream,uint value){do{var part=(byte)(value&127);value>>=7;if(value!=0)part|=128;stream.WriteByte(part);}while(value!=0);}
    static void Float(Stream stream,float value){Span<byte> scalar=stackalloc byte[4];System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(scalar,value);stream.Write(scalar);}
    static void DeformValues(Stream stream,uint offset,float[] values){UInt(stream,(uint)values.Length);if(values.Length>0){UInt(stream,offset);foreach(var value in values)Float(stream,value);}}
    static (uint Offset,float[] Values) SparseValues(double[] source)
    {
        var values=source.Select(Float32).ToArray();var start=0;var end=values.Length;bool Zero(int at)=>BitConverter.SingleToInt32Bits(values[at])==0;while(start<end&&Zero(start)&&Zero(start+1))start+=2;while(end>start&&Zero(end-2)&&Zero(end-1))end-=2;return(start==end?0:(uint)start,values[start..end]);
    }
    void Commit(BinaryDocument candidate,bool meshStructure=false)
    {
        // Setup edits preserve every animation byte; Deform edits validate the unchanged setup and other tracks.
        if(PreservesAnimations)
        {
            var before=Document.ReadSetup4117();var after=candidate.ReadSetup4117();
            if(Document.Header.Version!=candidate.Header.Version||before.AnimationCount!=after.AnimationCount)throw new InvalidDataException("Preserved animation count/version changed");
            if(!meshStructure&&!Document.UnchangedBytes().AsSpan(before.AnimationCountSpan.Offset+before.AnimationCountSpan.Length).SequenceEqual(candidate.UnchangedBytes().AsSpan(after.AnimationCountSpan.Offset+after.AnimationCountSpan.Length)))ValidateDeformOnly41(candidate);
        }
        Validate(candidate);var nextPlayback=playback41 is null?null:candidate.ReadAnimations41();foreach(var entry in redo)HistoryBytes-=entry.Document.Length;redo.Clear();Remember(undo,Document,revision);Document=candidate;playback41=nextPlayback;cachedModel=null;revision=++nextRevision;
    }
    internal bool Adopt(BinaryDocument candidate){if(Document.UnchangedBytes().AsSpan().SequenceEqual(candidate.UnchangedBytes()))return false;Commit(candidate);return true;}
    void Remember(LinkedList<(BinaryDocument Document,long Revision)> history,BinaryDocument document,long at)
    {
        history.AddLast((document,at));HistoryBytes+=document.Length;
        while(HistoryBytes>historyLimit){var oldest=undo.First??redo.First!;HistoryBytes-=oldest.Value.Document.Length;oldest.List!.Remove(oldest);HistoryTruncated=true;}
    }
    public void SetSlotAttachment4117(string name,string? placeholder)
    {
        if(!setup4117Profile)throw new NotSupportedException("Open the explicit 4.1.17 setup editing profile");
        var skin=Complete4117(Document).DefaultSkin;var slots=skin.PathSection.TransformSection.IkSection.SlotSection.Slots;var index=Array.FindIndex(slots,item=>string.Equals(item.Name,name,StringComparison.Ordinal));if(index<0)throw new ArgumentException("Binary slot selection");
        var reference=placeholder is null?0:skin.SlotGroups.SingleOrDefault(group=>group.SlotIndex==index)?.Attachments.SingleOrDefault(item=>string.Equals(item.Placeholder,placeholder,StringComparison.Ordinal))?.PlaceholderIndex??throw new ArgumentException("Binary default attachment selection");var slot=slots[index];if(slot.Attachment==placeholder)return;
        using var payload=new MemoryStream();UInt(payload,reference);var encoded=payload.ToArray();var before=Document.UnchangedBytes();var span=slot.AttachmentSpan;var length=checked(before.Length-span.Length+encoded.Length);if(length>Input.MaxBytes)throw new InvalidDataException("Binary edit size limit");var after=new byte[length];before.AsSpan(0,span.Offset).CopyTo(after);encoded.CopyTo(after.AsSpan(span.Offset));before.AsSpan(span.Offset+span.Length).CopyTo(after.AsSpan(span.Offset+encoded.Length));Commit(new(after));
    }
    public void SetAttachmentColor4117(string name,string placeholder,uint color)
    {
        if(!setup4117Profile)throw new NotSupportedException("Open the explicit 4.1.17 setup editing profile");
        var skin=Complete4117(Document).DefaultSkin;var slots=skin.PathSection.TransformSection.IkSection.SlotSection.Slots;var index=Array.FindIndex(slots,item=>string.Equals(item.Name,name,StringComparison.Ordinal));if(index<0)throw new ArgumentException("Binary slot selection");
        var attachment=skin.SlotGroups.SingleOrDefault(group=>group.SlotIndex==index)?.Attachments.SingleOrDefault(item=>string.Equals(item.Placeholder,placeholder,StringComparison.Ordinal))??throw new ArgumentException("Binary default attachment selection");
        var (current,span)=attachment.Region is{} region?(region.Color,region.ColorSpan):attachment.Mesh is{} mesh?(mesh.Color,mesh.ColorSpan):throw new NotSupportedException("Binary attachment color profile");if(current==color)return;
        var bytes=Document.UnchangedBytes();System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(span.Offset,4),color);Commit(new(bytes));
    }
    public void SetSlotColor4117(string name,uint color)
    {
        if(!setup4117Profile)throw new NotSupportedException("Open the explicit 4.1.17 setup editing profile");
        var slot=Complete4117(Document).DefaultSkin.PathSection.TransformSection.IkSection.SlotSection.Slots.SingleOrDefault(item=>string.Equals(item.Name,name,StringComparison.Ordinal))??throw new ArgumentException("Binary slot selection");
        if(slot.Color==color)return;var bytes=Document.UnchangedBytes();System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(slot.ColorSpan.Offset,4),color);Commit(new(bytes));
    }
    public float SetIkSetup4117(string name,double mix,bool bendPositive)
    {
        if(!setup4117Profile)throw new NotSupportedException("Open the explicit 4.1.17 setup editing profile");
        if(!double.IsFinite(mix)||mix is <0 or >1)throw new InvalidDataException("Binary IK mix range");var actual=Float32(mix);
        var constraint=Complete4117(Document).DefaultSkin.PathSection.TransformSection.IkSection.Constraints.SingleOrDefault(item=>string.Equals(item.Name,name,StringComparison.Ordinal))??throw new ArgumentException("Binary IK selection");
        if(constraint.Mix==actual&&constraint.BendPositive==bendPositive)return actual;
        var bytes=Document.UnchangedBytes();System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(constraint.MixSpan.Offset,4),actual);bytes[constraint.BendSpan.Offset]=bendPositive?(byte)1:(byte)255;Commit(new(bytes));return actual;
    }
    public Point SetLocalVertex(int slot,string placeholder,int index,Point target)
    {
        target.Validate();var x=Float32(target.X);var y=Float32(target.Y);
        var groups=setup4117Profile?Complete4117(Document).DefaultSkin.SlotGroups:Complete(Document).DefaultSkin.SlotGroups;var attachment=groups.SingleOrDefault(group=>group.SlotIndex==slot)?.Attachments.SingleOrDefault(item=>string.Equals(item.Placeholder,placeholder,StringComparison.Ordinal))??throw new ArgumentException("Binary attachment selection");
        var mesh=attachment.Mesh??throw new NotSupportedException("Binary local editing requires a mesh");if(mesh.Weights.Length!=0)throw new NotSupportedException("Weighted binary editing pending");
        if(index<0||index>=mesh.Vertices.Length)throw new ArgumentOutOfRangeException(nameof(index));var actual=new Point(x,y);if(mesh.Vertices[index]==actual)return actual;
        var bytes=Document.UnchangedBytes();System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(mesh.VertexSpans[index*2].Offset,4),x);System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(mesh.VertexSpans[index*2+1].Offset,4),y);
        Commit(new(bytes));return actual;
    }
    public Point SetWorldVertex(int slot,string placeholder,int index,Point target,string? animation=null,double time=0)
        =>SetWorldVertices(slot,placeholder,new Dictionary<int,Point>{{index,target}},animation,time)[index];
    internal Dictionary<int,Point> SetWorldVertices(int slot,string placeholder,IReadOnlyDictionary<int,Point> targets,string? animation=null,double time=0)
    {
        if(setup4117Profile&&animation is not null)throw new NotSupportedException("4.1.17 animation editing pending");
        foreach(var target in targets.Values)target.Validate();if(!double.IsFinite(time)||time<0)throw new ArgumentOutOfRangeException(nameof(time));var setup41=setup4117Profile?Complete4117(Document):null;var skin=setup4117Profile?null:(deformProfile?Body(Document).Setup:Complete(Document)).DefaultSkin;var groups=setup41?.DefaultSkin.SlotGroups??skin!.SlotGroups;var slots=setup41?.DefaultSkin.PathSection.TransformSection.IkSection.SlotSection.Slots??skin!.TransformSection.IkSection.SlotSection.Slots;if(slot<0||slot>=slots.Length)throw new ArgumentOutOfRangeException(nameof(slot));var binding=groups.SingleOrDefault(group=>group.SlotIndex==slot)?.Attachments.SingleOrDefault(item=>string.Equals(item.Placeholder,placeholder,StringComparison.Ordinal))??throw new ArgumentException("Binary attachment selection");var raw=binding.Mesh??throw new NotSupportedException("Binary world editing requires a mesh");if(targets.Count==0||targets.Keys.Any(index=>index<0||index>=raw.Uvs.Length))throw new ArgumentOutOfRangeException(nameof(targets));
        var model=setup4117Profile&&PreservesAnimations?BinaryScene.ReadSetupPose41(Document):Model(Document);var key=Scene.Key("default",slots[slot].Name,placeholder);var transient=new Editor(model.Data.ToJsonString(),recordHistory:false).CopyGeometryPreview();var pose=transient.Document.Pose(animation,time);var deform=Playback.Deform(transient.Document,transient.Document.Meshes[key],animation,time);var current=transient.Document.Vertices(transient.Document.Meshes[key],pose,deform);
        if(targets.All(p=>p.Value==current[p.Key].Position))return targets.ToDictionary();
        transient.MoveVertices(key,targets,pose,deform);
        var moved=transient.Document.Meshes[key];var before=Document.UnchangedBytes();var bytes=before.ToArray();
        void Store(ByteSpan x,ByteSpan y,Point local){System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(x.Offset,4),Float32(local.X));System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(y.Offset,4),Float32(local.Y));}
        foreach(var index in targets.Keys)
        {
            if(raw.Weights.Length==0)Store(raw.VertexSpans[2*index],raw.VertexSpans[2*index+1],moved.Local[index]);else for(var i=0;i<raw.Weights[index].Influences.Length;i++){var influence=raw.Weights[index].Influences[i];Store(influence.XSpan,influence.YSpan,moved.Weights[index][i].Local);}
        }
        var candidate=new BinaryDocument(bytes);var actualModel=setup4117Profile&&PreservesAnimations?BinaryScene.ReadSetupPose41(candidate):Model(candidate);var actual=actualModel.Vertices(actualModel.Meshes[key],actualModel.Pose(animation,time),Playback.Deform(actualModel,actualModel.Meshes[key],animation,time));
        foreach(var (index,target) in targets)RequireRoundedTarget(actual[index].Position,target,current[index].Position,WorldRoundingBound(moved,actualModel.Meshes[key],index,pose,model.Slots[slot].Bone));
        ValidateMeshPrecision(model,transient.Document,actualModel);
        if(!before.AsSpan().SequenceEqual(bytes))Commit(candidate);return targets.Keys.ToDictionary(index=>index,index=>actual[index].Position);
    }
    public Point SetDeformedWorldVertex(string animation,int slot,string placeholder,int frameIndex,int vertex,Point target)
        =>SetDeformedWorldVertices(animation,slot,placeholder,frameIndex,new Dictionary<int,Point>{{vertex,target}})[vertex];
    internal Dictionary<int,Point> SetDeformedWorldVertices(string animation,int slot,string placeholder,int frameIndex,IReadOnlyDictionary<int,Point> targets)
    {
        if(!deformProfile)throw new NotSupportedException("Open the explicit binary Deform editing profile");foreach(var target in targets.Values)target.Validate();
        var body=Body(Document);var timeline=body.Animations.SingleOrDefault(a=>a.Name==animation)?.Slots.SingleOrDefault(s=>s.SlotIndex==slot)?.Timelines.SingleOrDefault(t=>t.Name==placeholder)??throw new ArgumentException("Binary Deform selection");if(frameIndex<0||frameIndex>=timeline.Frames.Length)throw new ArgumentOutOfRangeException(nameof(frameIndex));
        var slotName=body.Setup.DefaultSkin.TransformSection.IkSection.SlotSection.Slots[slot].Name;var key=Scene.Key("default",slotName,placeholder);var model=Model(Document);var mesh=model.Meshes[key];if(targets.Count==0||targets.Keys.Any(vertex=>vertex<0||vertex>=mesh.UV.Length))throw new ArgumentOutOfRangeException(nameof(targets));var time=(double)timeline.Frames[frameIndex].Time;var current=model.Vertices(mesh,model.Pose(animation,time),Playback.Deform(model,mesh,animation,time));if(targets.All(p=>p.Value==current[p.Key].Position))return targets.ToDictionary();
        var transient=new Editor(model.Data.ToJsonString(),recordHistory:false);transient.MoveDeformedVertices(key,targets,animation,time);var moved=transient.Document;var sparse=SparseValues(Playback.Deform(moved,moved.Meshes[key],animation,time)??throw new InvalidDataException("Binary Deform values missing"));
        var candidate=new BinaryEditor(Document.UnchangedBytes(),0,true,rotationProfile);candidate.ReplaceDeformValues(animation,slot,placeholder,frameIndex,sparse.Offset,sparse.Values);var actualModel=Model(candidate.Document);var actualMesh=actualModel.Meshes[key];var actual=actualModel.Vertices(actualMesh,actualModel.Pose(animation,time),Playback.Deform(actualModel,actualMesh,animation,time));foreach(var (vertex,target) in targets)Editor.RequireTarget(actual[vertex].Position,target,current[vertex].Position);Commit(candidate.Document);return targets.Keys.ToDictionary(vertex=>vertex,vertex=>actual[vertex].Position);
    }
    public (float Time,Point World) InsertDeformedWorldVertex(string animation,int slot,string placeholder,double requestedTime,int vertex,Point target)
    {
        var result=InsertDeformedWorldVertices(animation,slot,placeholder,requestedTime,new Dictionary<int,Point>{{vertex,target}});return(result.Time,result.World[vertex]);
    }
    internal (float Time,Dictionary<int,Point> World) InsertDeformedWorldVertices(string animation,int slot,string placeholder,double requestedTime,IReadOnlyDictionary<int,Point> targets)
    {
        if(!deformProfile)throw new NotSupportedException("Open the explicit binary Deform editing profile");foreach(var target in targets.Values)target.Validate();if(!double.IsFinite(requestedTime)||requestedTime<0)throw new ArgumentOutOfRangeException(nameof(requestedTime));var time=Float32(requestedTime);
        var body=Body(Document);var animationBody=body.Animations.SingleOrDefault(a=>a.Name==animation)??throw new ArgumentException("Binary animation selection");var binding=body.Setup.DefaultSkin.SlotGroups.SingleOrDefault(g=>g.SlotIndex==slot)?.Attachments.SingleOrDefault(a=>a.Placeholder==placeholder)??throw new ArgumentException("Binary attachment selection");if(binding.Mesh is null)throw new NotSupportedException("Binary Deform requires mesh");var group=animationBody.Slots.SingleOrDefault(s=>s.SlotIndex==slot);var timeline=group?.Timelines.SingleOrDefault(t=>t.Name==placeholder);var frames=timeline?.Frames??[];if(frames.Any(f=>f.Time==time))throw new InvalidOperationException("Binary Deform key time exists");var position=Array.FindIndex(frames,f=>f.Time>time);if(position<0)position=frames.Length;
        var slotName=body.Setup.DefaultSkin.TransformSection.IkSection.SlotSection.Slots[slot].Name;var key=Scene.Key("default",slotName,placeholder);var model=Model(Document);var mesh=model.Meshes[key];if(targets.Count==0||targets.Keys.Any(vertex=>vertex<0||vertex>=mesh.UV.Length))throw new ArgumentOutOfRangeException(nameof(targets));var current=model.Vertices(mesh,model.Pose(animation,time),Playback.Deform(model,mesh,animation,time));var transient=new Editor(model.Data.ToJsonString(),recordHistory:false);transient.MoveDeformedVertices(key,targets,animation,time,createKey:true);
        JsonArray? Keys(Scene scene)=>Scene.Array(scene.Data["animations"]?[animation]?["deform"]?["default"]?[slotName]?[placeholder]);var source=Keys(model)??[];var generated=Keys(transient.Document)??throw new InvalidDataException("Binary generated keys missing");if(generated.Count<=frames.Length||!generated.Any(f=>Scene.Number(f?["time"])==time))throw new InvalidDataException("Binary inserted key missing");var originals=frames.Select((frame,index)=>(frame,index)).ToDictionary(item=>(double)item.frame.Time);var seen=0;
        var before=Document.UnchangedBytes();using var payload=new MemoryStream();void Curve(JsonNode? curve){if(curve is JsonArray){payload.WriteByte(2);foreach(var value in Scene.Values(curve))Float(payload,Float32(value));}else if(curve is null)payload.WriteByte(0);else if(Scene.Text(curve)=="stepped")payload.WriteByte(1);else throw new NotSupportedException("Binary generated curve kind");}UInt(payload,(uint)generated.Count);
        for(var at=0;at<generated.Count;at++)
        {
            var node=generated[at]!.AsObject();var frameTime=Scene.Number(node["time"]);var curve=Timeline.NormalizedCurve(node);
            if(originals.TryGetValue(frameTime,out var original))
            {
                seen++;var frame=original.frame;var changed=!JsonNode.DeepEquals(Timeline.NormalizedCurve(source[original.index]!.AsObject()),curve);if(changed&&original.index!=position-1)throw new InvalidDataException("Binary unrelated curve changed");var frameEnd=changed&&frame.Curve is{} oldCurve?oldCurve.Span.Offset:frame.Span.Offset+frame.Span.Length;payload.Write(before.AsSpan(frame.Span.Offset,frameEnd-frame.Span.Offset));if(changed||frame.Curve is null&&at+1<generated.Count)Curve(curve);
            }
            else
            {
                var storedTime=Float32(frameTime);if((double)storedTime!=frameTime)throw new NotSupportedException("Binary support key time float32 precision");Float(payload,storedTime);var sparse=SparseValues(Scene.Values(node["vertices"]));DeformValues(payload,sparse.Offset,sparse.Values);if(at+1<generated.Count)Curve(curve);
            }
        }
        if(seen!=frames.Length)throw new InvalidDataException("Binary original key missing");
        var encoded=payload.ToArray();int start,end;
        if(timeline is not null){start=timeline.CountSpan.Offset;end=timeline.Span.Offset+timeline.Span.Length;}
        else
        {
            using var added=new MemoryStream();UInt(added,binding.PlaceholderIndex);added.Write(encoded);var newTimeline=added.ToArray();using var scope=new MemoryStream();
            if(group is not null)
            {
                start=group.CountSpan.Offset;end=group.Span.Offset+group.Span.Length;UInt(scope,(uint)group.Timelines.Length+1);var inserted=false;foreach(var item in group.Timelines){if(!inserted&&item.NameIndex>binding.PlaceholderIndex){scope.Write(newTimeline);inserted=true;}scope.Write(before.AsSpan(item.Span.Offset,item.Span.Length));}if(!inserted)scope.Write(newTimeline);
            }
            else
            {
                start=animationBody.SlotCountSpan?.Offset??animationBody.SkinCountSpan.Offset;end=animationBody.DrawOrderSpan.Offset;if(animationBody.SlotCountSpan is null){UInt(scope,1);UInt(scope,0);}UInt(scope,(uint)animationBody.Slots.Length+1);using var addedGroup=new MemoryStream();UInt(addedGroup,(uint)slot);UInt(addedGroup,1);addedGroup.Write(newTimeline);var newGroup=addedGroup.ToArray();var inserted=false;foreach(var item in animationBody.Slots){if(!inserted&&item.SlotIndex>slot){scope.Write(newGroup);inserted=true;}scope.Write(before.AsSpan(item.Span.Offset,item.Span.Length));}if(!inserted)scope.Write(newGroup);
            }
            encoded=scope.ToArray();
        }var length=checked(before.Length-(end-start)+encoded.Length);if(length>Input.MaxBytes)throw new InvalidDataException("Binary edit size limit");var after=new byte[length];before.AsSpan(0,start).CopyTo(after);encoded.CopyTo(after.AsSpan(start));before.AsSpan(end).CopyTo(after.AsSpan(start+encoded.Length));var candidate=new BinaryDocument(after);var actualModel=Model(candidate);var actualMesh=actualModel.Meshes[key];var actual=actualModel.Vertices(actualMesh,actualModel.Pose(animation,time),Playback.Deform(actualModel,actualMesh,animation,time));foreach(var (vertex,target) in targets)Editor.RequireTarget(actual[vertex].Position,target,current[vertex].Position);Commit(candidate);return(time,targets.Keys.ToDictionary(vertex=>vertex,vertex=>actual[vertex].Position));
    }
    public void ReplaceDeformValues(string animation,int slot,string placeholder,int index,uint offset,float[] values)
    {
        if(!deformProfile)throw new NotSupportedException("Open the explicit binary Deform editing profile");ArgumentNullException.ThrowIfNull(values);
        var body=Body(Document);var timeline=body.Animations.SingleOrDefault(a=>string.Equals(a.Name,animation,StringComparison.Ordinal))?.Slots.SingleOrDefault(s=>s.SlotIndex==slot)?.Timelines.SingleOrDefault(t=>string.Equals(t.Name,placeholder,StringComparison.Ordinal))??throw new ArgumentException("Binary Deform selection");
        if(index<0||index>=timeline.Frames.Length)throw new ArgumentOutOfRangeException(nameof(index));if(offset%2!=0||values.Length%2!=0)throw new NotSupportedException("Odd binary Deform ranges pending");if(offset>timeline.Domain||values.Length>timeline.Domain-offset||values.Length==0&&offset!=0)throw new ArgumentOutOfRangeException(nameof(offset));
        var data=values.ToArray();if(data.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Nonfinite binary Deform value");var frame=timeline.Frames[index];if(frame.Offset==offset&&frame.Values.AsSpan().SequenceEqual(data))return;
        using var payload=new MemoryStream();
        DeformValues(payload,offset,data);
        var before=Document.UnchangedBytes();var start=frame.CountSpan.Offset;var end=frame.Curve?.Span.Offset??frame.Span.Offset+frame.Span.Length;var encoded=payload.ToArray();var length=checked(before.Length-(end-start)+encoded.Length);if(length>Input.MaxBytes)throw new InvalidDataException("Binary edit size limit");
        var after=new byte[length];before.AsSpan(0,start).CopyTo(after);encoded.AsSpan().CopyTo(after.AsSpan(start));before.AsSpan(end).CopyTo(after.AsSpan(start+encoded.Length));Commit(new(after));
    }
    void Travel(LinkedList<(BinaryDocument Document,long Revision)> source,LinkedList<(BinaryDocument Document,long Revision)> destination)
    {
        if(source.Last is null)return;var entry=source.Last.Value;var nextPlayback=playback41 is null?null:entry.Document.ReadAnimations41();source.RemoveLast();HistoryBytes-=entry.Document.Length;Remember(destination,Document,revision);Document=entry.Document;playback41=nextPlayback;cachedModel=null;revision=entry.Revision;
    }
    public void Undo()=>Travel(undo,redo);
    public void Redo()=>Travel(redo,undo);
    public void SaveNew(string path)
    {
        Experiment.CheckOutput(path);if(File.Exists(path)||Directory.Exists(path))throw new IOException("Binary save destination exists");SaveChecked(path,null);
    }
    internal void SaveChecked(string path,string? expectedDigest,bool original=false)
    {
        Action<string> checkPath=original?Experiment.CheckSavePath:Experiment.CheckOutput;
        checkPath(path);if(!string.Equals(Path.GetExtension(path),".skel",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Binary save extension");
        if(original&&expectedDigest is null)throw new InvalidOperationException("Original SKEL digest required");
        if(Directory.Exists(path)||Editor.Digest(path)!=expectedDigest)throw new IOException("External modification or destination conflict");
        Validate(Document);var temporary=Path.Combine(Path.GetDirectoryName(path)!,Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(Document.UnchangedBytes());stream.Flush(true);}
            Validate(BinaryDocument.Load(temporary));
            if(expectedDigest is null)File.Move(temporary,path,false);else Editor.ReplaceChecked(temporary,path,expectedDigest,checkPath:checkPath);
            savedRevision=revision;
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
