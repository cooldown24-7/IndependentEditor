namespace IndependentDev;

internal sealed partial class Editor
{
    readonly BinaryEditor? binary;
    public bool IsBinary=>binary is not null;
    internal string? BinaryDigest=>binary?.Document.Digest;
    internal void SaveOriginalSkel(string path,string expectedDigest)
    {
        if(binary is null)throw new InvalidOperationException("Original save requires SKEL");
        try{binary.SaveChecked(path,expectedDigest,original:true);}
        catch(UnauthorizedAccessException error){throw new UnauthorizedAccessException("원본 폴더에 쓸 권한이 없어 저장하지 못했습니다. 편집 내용은 유지됩니다. ‘다른 이름’을 눌러 기본으로 표시되는 프로젝트 내부 폴더에 저장하세요.",error);}
    }
    internal bool BinaryRotation=>binary?.AllowsRotation??false;
    internal bool BinarySetup4117=>binary?.IsSetup4117??false;
    internal bool PreservesAnimations=>binary?.PreservesAnimations??false;
    internal uint PreservedAnimationCount=>binary?.PreservedAnimationCount??0;
    internal string? PlaybackError=>binary?.PlaybackError;
    internal bool CanEditDeform41=>binary?.CanEditDeform41??false;
    internal bool CanEditMeshStructure=>binary?.CanEditMeshStructure??true;
    public Editor(byte[] bytes,bool recordHistory=true,long historyLimitBytes=MaxHistoryBytes,bool rotation=false,bool setup4117=false,bool preserveAnimations=false)
    {
        if(historyLimitBytes is <0 or >MaxHistoryBytes)throw new ArgumentOutOfRangeException(nameof(historyLimitBytes));
        if(rotation&&setup4117||preserveAnimations&&!setup4117)throw new ArgumentException("Conflicting binary editing profiles");
        binary=setup4117?BinaryEditor.ForSetup4117(bytes,recordHistory?historyLimitBytes:0,preserveAnimations):rotation?BinaryEditor.ForAnimation(bytes,recordHistory?historyLimitBytes:0):BinaryEditor.ForDeform(bytes,recordHistory?historyLimitBytes:0);Document=binary.ReadModel();this.recordHistory=recordHistory;this.historyLimitBytes=historyLimitBytes;
    }
    Editor(Editor source){binary=source.binary!.CopyForPreview();Document=source.Document;recordHistory=false;historyLimitBytes=0;}
    internal Editor CopyForPreview()=>binary is null?new(Document.Data.ToJsonString(),recordHistory:false):new(this);
    internal bool SameData(Editor other)=>binary is null?other.binary is null&&Document.Data.ToJsonString()==other.Document.Data.ToJsonString():other.binary is not null&&binary.Document.Digest==other.binary.Document.Digest;
    internal void AcceptPreview(Editor candidate)
    {
        if(IsBinary!=candidate.IsBinary||BinaryRotation!=candidate.BinaryRotation||BinarySetup4117!=candidate.BinarySetup4117||PreservesAnimations!=candidate.PreservesAnimations)throw new InvalidOperationException("Preview document format mismatch");
        if(binary is null){Edit(root=>{root.Clear();foreach(var field in candidate.Document.Data)root[field.Key]=field.Value?.DeepClone();});return;}
        var scene=candidate.binary!.ReadModel();if(binary.Adopt(candidate.binary.Document))Document=scene;
    }
    internal void MoveAtPose(string key,int index,Point target,string? animation,double time,string skin,bool deform)
    {
        if(binary is null){if(deform)MoveDeformedVertex(key,index,target,animation!,time,skin);else MoveVertex(key,index,target,Document.Pose(animation,time,skin),Playback.Deform(Document,Document.Meshes[key],animation,time));return;}
        if(skin!="default")throw new NotSupportedException("Binary default skin editing profile only");
        if(BinarySetup4117&&deform){binary.MoveDeformedWorld41(key,index,target,animation??throw new InvalidDataException("Deform animation required"),time);Document=binary.ReadModel();return;}
        var mesh=Document.Meshes[key];var slot=Array.FindIndex(Document.Slots,s=>s.Name==mesh.Slot);if(slot<0)throw new InvalidDataException("Binary selected slot reference");
        if(!deform)binary.SetWorldVertex(slot,mesh.Name,index,target,animation,time);
        else
        {
            if(animation is null)throw new InvalidDataException("Binary Deform animation required");var body=binary.ReadBody();var timeline=body.Animations.SingleOrDefault(a=>a.Name==animation)?.Slots.SingleOrDefault(s=>s.SlotIndex==slot)?.Timelines.SingleOrDefault(t=>t.Name==mesh.Name);var frame=timeline is null?-1:Array.FindIndex(timeline.Frames,f=>(double)f.Time==time);if(frame>=0)binary.SetDeformedWorldVertex(animation,slot,mesh.Name,frame,index,target);else binary.InsertDeformedWorldVertex(animation,slot,mesh.Name,time,index,target);
        }
        Document=binary.ReadModel();
    }
    internal void MoveVerticesAtPose(string key,IReadOnlyDictionary<int,Point> targets,string? animation,double time,string skin,bool deform)
    {
        if(binary is null)
        {
            if(deform)MoveDeformedVertices(key,targets,animation!,time,skin);
            else MoveVertices(key,targets,Document.Pose(animation,time,skin),Playback.Deform(Document,Document.Meshes[key],animation,time));
            return;
        }
        if(skin!="default")throw new NotSupportedException("Binary default skin editing profile only");
        if(!deform)
        {
            var mesh=Document.Meshes[key];var slot=Array.FindIndex(Document.Slots,s=>s.Name==mesh.Slot);binary.SetWorldVertices(slot,mesh.Name,targets,animation,time);Document=binary.ReadModel();
        }
        else if(BinarySetup4117){binary.MoveDeformedWorldVertices41(key,targets,animation??throw new InvalidDataException("Deform animation required"),time);Document=binary.ReadModel();}
        else
        {
            if(animation is null)throw new InvalidDataException("Deform animation required");
            var mesh=Document.Meshes[key];var slot=Array.FindIndex(Document.Slots,s=>s.Name==mesh.Slot);var body=binary.ReadBody();var timeline=body.Animations.SingleOrDefault(a=>a.Name==animation)?.Slots.SingleOrDefault(s=>s.SlotIndex==slot)?.Timelines.SingleOrDefault(t=>t.Name==mesh.Name);var frame=timeline is null?-1:Array.FindIndex(timeline.Frames,f=>(double)f.Time==time);
            if(frame>=0)binary.SetDeformedWorldVertices(animation,slot,mesh.Name,frame,targets);else binary.InsertDeformedWorldVertices(animation,slot,mesh.Name,time,targets);
            Document=binary.ReadModel();
        }
    }
}
