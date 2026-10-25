using System.Collections.Immutable;
namespace IndependentDev;

internal readonly record struct CanvasFrame(Point Origin,Point Opposite)
{
    public double Width=>Opposite.X-Origin.X;
    public double Height=>Origin.Y-Opposite.Y;
}

internal sealed class CanvasCamera
{
    public double Scale { get; private set; } = 1;
    public Point Offset { get; private set; }
    public double FitScale { get; private set; } = 1;
    public Point Viewport { get; private set; }
    public Affine Matrix => new(Scale,0,0,-Scale,Offset.X,Offset.Y);
    public Point ToWorld(Point screen)
    {
        screen.Validate();
        double Component(double a,double b){var difference=a-b;return double.IsFinite(difference)?difference/Scale:a/Scale-b/Scale;}
        var result=new Point(Component(screen.X,Offset.X),Component(Offset.Y,screen.Y));result.Validate();return result;
    }
    public void Fit(IEnumerable<Point> points,Point viewport)
    {
        viewport.Validate();if(viewport.X<=0||viewport.Y<=0)throw new InvalidDataException("Viewport dimensions");
        var list=points.ToArray();foreach(var p in list)p.Validate();
        var minX=list.Length==0?0:list.Min(p=>p.X);var maxX=list.Length==0?1:list.Max(p=>p.X);
        var minY=list.Length==0?0:list.Min(p=>p.Y);var maxY=list.Length==0?1:list.Max(p=>p.Y);
        var width=maxX-minX;var height=maxY-minY;
        var availableWidth=Math.Max(1,viewport.X-60);var availableHeight=Math.Max(1,viewport.Y-60);
        var xFit=double.IsFinite(width)?availableWidth/(width==0?1:width):(availableWidth/2)/(maxX/2-minX/2);
        var yFit=double.IsFinite(height)?availableHeight/(height==0?1:height):(availableHeight/2)/(maxY/2-minY/2);var fit=Math.Min(xFit,yFit);
        var centerX=double.IsFinite(width)?minX+width/2:minX/2+maxX/2;var centerY=double.IsFinite(height)?minY+height/2:minY/2+maxY/2;
        var offset=new Point(viewport.X/2-centerX*fit,viewport.Y/2+centerY*fit);
        offset.Validate();if(!double.IsFinite(fit)||fit<=0)throw new InvalidDataException("Camera numeric range");
        Viewport=viewport;FitScale=Scale=fit;Offset=offset;
    }
    public void Pan(Point delta) { delta.Validate();var next=Offset+delta;next.Validate();Offset=next; }
    public void Resize(Point viewport)
    {viewport.Validate();if(viewport.X<=0||viewport.Y<=0)throw new InvalidDataException("Viewport dimensions");var offset=Offset+(viewport-Viewport)*.5;offset.Validate();Offset=offset;Viewport=viewport;}
    void SetScaleAt(Point screen,double requested)
    {
        screen.Validate();if(!double.IsFinite(requested)||requested<=0)throw new InvalidDataException("Camera scale");
        var world=ToWorld(screen);var next=Math.Clamp(requested,Math.Min(FitScale*.05,1),Math.Max(FitScale*50,1));
        var offset=screen-new Point(world.X*next,-world.Y*next);
        offset=new(double.IsFinite(offset.X)?offset.X:Math.FusedMultiplyAdd(-world.X,next,screen.X),double.IsFinite(offset.Y)?offset.Y:Math.FusedMultiplyAdd(world.Y,next,screen.Y));
        offset.Validate();Scale=next;Offset=offset;
    }
    public void ZoomAt(Point screen,double wheelDelta)
    {
        if(!double.IsFinite(wheelDelta))throw new InvalidDataException("Camera wheel");
        var exponent=Math.Clamp(wheelDelta*Math.Log(1.15),-700,700);
        SetScaleAt(screen,Math.Clamp(Scale*Math.Exp(exponent),double.Epsilon,double.MaxValue));
    }
    public void ActualSize() => SetScaleAt(Viewport*.5,1);
}

internal sealed class EditingSession
{
    public Editor Editor { get; private set; }
    public CanvasCamera Camera { get; private set; }
    public CanvasFrame Canvas { get; private set; }
    Editor? preview;
    bool geometryPreview;
    Point grabOffset, startPoint,lastTarget;
    public Scene Document => (preview??Editor).Document;
    public string? SelectedKey { get; private set; }
    public string? SelectedSlot { get; private set; }
    public int SelectedVertex { get; private set; } = -1;
    public ImmutableHashSet<int> SelectedVertices { get; private set; } = [];
    public string? Animation { get; private set; }
    public double Time { get; private set; }
    public bool DeformMode { get; private set; }
    public string Skin { get; private set; }="default";
    public bool IsDragging => preview is not null;
    public bool Modified => Editor.Dirty || preview is not null && (geometryPreview?lastTarget!=startPoint:!preview.SameData(Editor));
    public bool CanRedo => !IsDragging && Editor.RedoCount>0;
    public bool PoseReadOnly=>Editor.PreservesAnimations&&Animation is not null&&!DeformMode;
    void RequireEditablePose(){if(PoseReadOnly)throw new NotSupportedException("애니메이션 미리보기는 읽기 전용입니다. 설정 자세를 선택한 뒤 편집하세요.");}
    public EditingSession(string json,Point viewport):this(new Editor(json),viewport){}
    public EditingSession(byte[] bytes,Point viewport,bool rotation=false,bool setup4117=false,bool preserveAnimations=false):this(new Editor(bytes,rotation:rotation,setup4117:setup4117,preserveAnimations:preserveAnimations),viewport){}
    EditingSession(Editor editor,Point viewport)
    {
        Editor=editor;Camera=CameraFor(Editor.Document,viewport);Canvas=CanvasFor(Editor.Document);
    }
    static CanvasFrame CanvasFor(Scene scene)
    {
        var header=scene.Data["skeleton"];var width=Scene.Number(header?["width"]);var height=Scene.Number(header?["height"]);
        double left,right,top,bottom;
        if(width>0&&height>0)
        {left=Scene.Number(header?["x"]);bottom=Scene.Number(header?["y"]);right=left+width;top=bottom+height;}
        else
        {
            var points=Playback.Triangles(scene,"default").SelectMany(t=>t.Select(v=>v.Position)).ToArray();
            left=points.Length==0?0:points.Min(p=>p.X);right=points.Length==0?1:points.Max(p=>p.X);bottom=points.Length==0?0:points.Min(p=>p.Y);top=points.Length==0?1:points.Max(p=>p.Y);
        }
        left=Math.Floor(left);right=Math.Ceiling(right);bottom=Math.Floor(bottom);top=Math.Ceiling(top);
        right=Math.Clamp(right,-double.MaxValue,double.MaxValue);top=Math.Clamp(top,-double.MaxValue,double.MaxValue);
        return new(new(left,top),new(right==left?left+1:right,top==bottom?top-1:bottom));
    }
    static CanvasCamera CameraFor(Scene scene,Point viewport)
    {
        var camera=new CanvasCamera();camera.Fit(Playback.Triangles(scene,"default").SelectMany(t=>t.Select(v=>v.Position)),viewport);return camera;
    }
    public void Select(string? key,int vertex=-1)
    {
        if(key is not null && (!Editor.Document.Meshes.TryGetValue(key,out var mesh)||vertex< -1||vertex>=mesh.UV.Length))throw new InvalidDataException("Selection reference");
        FinishDrag(false);SelectedKey=key;SelectedSlot=null;SelectedVertex=key is null?-1:vertex;SelectedVertices=SelectedVertex<0?[]:[SelectedVertex];
    }
    public void SelectVertices(string key,IEnumerable<int> vertices,bool additive=false)
    {
        var selected=vertices.ToImmutableHashSet();
        if(!Editor.Document.Meshes.TryGetValue(key,out var mesh)||Scene.Text(mesh.Data["type"])!="mesh"||selected.Any(i=>i<0||i>=mesh.UV.Length))throw new InvalidDataException("Vertex selection reference");
        FinishDrag(false);if(additive&&SelectedKey==key)selected=SelectedVertices.Union(selected);
        SelectedKey=key;SelectedSlot=null;SelectedVertices=selected;SelectedVertex=selected.Contains(SelectedVertex)?SelectedVertex:selected.DefaultIfEmpty(-1).Min();
    }
    public void SelectVertex(string key,int vertex,bool toggle=false)
    {
        var selected=SelectedKey==key?SelectedVertices:ImmutableHashSet<int>.Empty;
        if(toggle)selected=selected.Contains(vertex)?selected.Remove(vertex):selected.Add(vertex);
        else if(!selected.Contains(vertex))selected=[vertex];
        SelectVertices(key,selected);
        if(SelectedVertices.Contains(vertex))SelectedVertex=vertex;
    }
    public void SelectSlot(string name)
    {
        if(!Editor.Document.Slots.Any(slot=>slot.Name==name))throw new InvalidDataException("Slot selection reference");Select(null);SelectedSlot=name;
    }
    public void SetSelectedSlotAttachment(string? placeholder,Action<Scene>? validate=null)
    {
        RequireEditablePose();
        if(SelectedSlot is null)throw new InvalidOperationException("Slot selection/state");FinishDrag(true);Editor.SetSlotAttachment(SelectedSlot,placeholder,next=>{Playback.Parts(next,Skin,Animation,Time);validate?.Invoke(next);});
    }
    public void SetSelectedSlotColor(uint color)
    {
        RequireEditablePose();
        if(SelectedSlot is null)throw new InvalidOperationException("Slot selection/state");FinishDrag(true);Editor.SetSlotColor(SelectedSlot,color);
    }
    public void BeginDrag(Point screen)
    {
        RequireEditablePose();
        if(IsDragging||SelectedKey is null||SelectedVertex<0)throw new InvalidOperationException("Drag selection/state");
        var mesh=Editor.Document.Meshes[SelectedKey];if(Scene.Text(mesh.Data["type"])!="mesh")throw new NotSupportedException("Only mesh vertex dragging");var pose=Editor.Document.Pose(Animation,Time,Skin);
        startPoint=Editor.Document.Vertices(mesh,pose,Playback.Deform(Editor.Document,mesh,Animation,Time))[SelectedVertex].Position;
        grabOffset=startPoint-Camera.ToWorld(screen);lastTarget=startPoint;geometryPreview=Editor.IsBinary&&Editor.BinarySetup4117&&Animation is null&&!DeformMode;preview=geometryPreview?Editor.CopyGeometryPreview():Editor.CopyForPreview();
    }
    public Point? SelectedPosition()
    {
        if(SelectedKey is null||SelectedVertex<0)return null;
        var mesh=Document.Meshes[SelectedKey];if(Scene.Text(mesh.Data["type"])!="mesh")return null;
        return Document.Vertices(mesh,Document.Pose(Animation,Time,Skin),Playback.Deform(Document,mesh,Animation,Time))[SelectedVertex].Position;
    }
    void MoveSelected(Editor candidate,Point target)
    {
        if(SelectedVertices.Count==1){candidate.MoveAtPose(SelectedKey!,SelectedVertex,target,Animation,Time,Skin,DeformMode);return;}
        var mesh=Editor.Document.Meshes[SelectedKey!];var points=Editor.Document.Vertices(mesh,Editor.Document.Pose(Animation,Time,Skin),Playback.Deform(Editor.Document,mesh,Animation,Time));
        var delta=target-points[SelectedVertex].Position;
        var targets=new Dictionary<int,Point>();
        foreach(var index in SelectedVertices.Order())
        {
            var next=points[index].Position+delta;next.Validate();if(Math.Abs(next.X)>100_000||Math.Abs(next.Y)>100_000)throw new InvalidDataException("Edit target range");
            targets.Add(index,next);
        }
        candidate.MoveVerticesAtPose(SelectedKey!,targets,Animation,Time,Skin,DeformMode);
    }
    public void SetSelectedAttachmentColor(uint color)
    {
        RequireEditablePose();
        FinishDrag(true);if(SelectedKey is null)throw new InvalidOperationException("Attachment selection/state");Editor.SetAttachmentColor(SelectedKey,color);
    }
    public void MoveSelectedVertex(Point target)
    {
        RequireEditablePose();
        if(IsDragging||SelectedKey is null||SelectedVertex<0)throw new InvalidOperationException("Vertex selection/state");
        target.Validate();
        if(Math.Abs(target.X)>100_000||Math.Abs(target.Y)>100_000)throw new InvalidDataException("Edit target range");
        if(SelectedPosition()==target)return;
        MoveSelected(Editor,target);
    }
    public void DragTo(Point screen)
    {
        if(preview is null||SelectedKey is null)throw new InvalidOperationException("No active drag");
        var target=Camera.ToWorld(screen)+grabOffset;target.Validate();
        if(Math.Abs(target.X)>100_000||Math.Abs(target.Y)>100_000)throw new InvalidDataException("Drag target range");
        if(target==lastTarget)return;
        // Animation/Deform previews retain full validation; setup vertex previews commit binary changes only on release.
        var candidate=target==startPoint?(geometryPreview?Editor.CopyGeometryPreview():Editor.CopyForPreview()):preview;
        if(target!=startPoint)
        {
            MoveSelected(candidate,target);
        }
        preview=candidate;lastTarget=target;
    }
    public void Nudge(Point screen,Point delta)
    {
        BeginDrag(screen);
        try{DragTo(screen+delta);FinishDrag(true);}
        finally{FinishDrag(false);}
    }
    public void FinishDrag(bool commit)
    {
        if(preview is null)return;
        if(commit)
        {
            if(geometryPreview){if(lastTarget!=startPoint)MoveSelected(Editor,lastTarget);}else Editor.AcceptPreview(preview);
        }
        preview=null;geometryPreview=false;
    }
    public void Escape() { if(IsDragging)FinishDrag(false);else Select(null); }
    void ReconcileSelection()
    {
        if(SelectedKey is not{} key)return;
        if(!Document.Meshes.TryGetValue(key,out var mesh)){SelectedKey=null;SelectedVertex=-1;SelectedVertices=[];}
        else{SelectedVertices=SelectedVertices.Where(i=>i<mesh.UV.Length).ToImmutableHashSet();if(!SelectedVertices.Contains(SelectedVertex))SelectedVertex=SelectedVertices.DefaultIfEmpty(-1).Min();}
    }
    public void Undo() { FinishDrag(true);if(PoseReadOnly)SetPose(null,0,false);Editor.Undo();ReconcileSelection(); }
    public void Redo() { if(!IsDragging){if(PoseReadOnly)SetPose(null,0,false);Editor.Redo();ReconcileSelection();} }
    void RequireStructureEdit()
    {
        if(!Editor.CanEditMeshStructure)throw new NotSupportedException("이 SKEL의 애니메이션 판독이 불완전해 메시 구조 편집을 지원하지 않습니다.");
        if(Animation is not null||DeformMode)throw new InvalidOperationException("메시 구조는 설정 자세에서 편집하세요.");
        FinishDrag(true);
    }
    public void AddMesh(string name,AtlasRegion region,Action<Scene>? validate=null)
    {
        RequireStructureEdit();var slot=SelectedSlot??(SelectedKey is{} key?Document.Meshes[key].Slot:null)??throw new InvalidOperationException("메시를 추가할 슬롯을 선택하세요.");
        var added=Editor.AddMesh(Skin,slot,name,region,validate);Select(added,0);
    }
    public void DeleteSelectedMesh(Action<Scene>? validate=null)
    {
        RequireStructureEdit();var key=SelectedKey??throw new InvalidOperationException("삭제할 메시를 선택하세요.");var slot=Document.Meshes[key].Slot;
        Editor.DeleteMesh(key,validate);SelectSlot(slot);
    }
    public void AddMeshVertex(Point world,Action<Scene>? validate=null)
    {
        RequireStructureEdit();var key=SelectedKey??throw new InvalidOperationException("메시를 선택하세요.");
        var mesh=Document.Meshes[key];if(!MeshTopology.InsideHull(world,Document.Vertices(mesh,Document.Pose(skin:Skin)).Select(v=>v.Position).ToArray(),mesh.Triangles))throw new InvalidOperationException("외곽 변을 선택하고 ‘외곽 정점 추가’를 사용하세요.");
        var index=Editor.AddMeshVertex(key,world,validate,Skin);Select(key,index);
    }
    public void AddHullVertex(int a,int b,Point world,Action<Scene>? validate=null)
    {
        RequireStructureEdit();var key=SelectedKey??throw new InvalidOperationException("메시를 선택하세요.");var index=Editor.InsertHullVertex(key,a,b,world,validate,Skin);Select(key,index);
    }
    public void IncludeHullVertex(int a,int b,Action<Scene>? validate=null)
    {
        RequireStructureEdit();var key=SelectedKey??throw new InvalidOperationException("메시를 선택하세요.");if(SelectedVertices.Count!=1)throw new InvalidOperationException("외곽에 포함할 내부 정점을 하나 선택하세요.");var index=Editor.IncludeHullVertex(key,a,b,SelectedVertex,validate,Skin);Select(key,index);
    }
    public void ExcludeHullVertices(Action<Scene>? validate=null)
    {
        RequireStructureEdit();var key=SelectedKey??throw new InvalidOperationException("메시를 선택하세요.");Editor.ExcludeHullVertices(key,SelectedVertices,validate,Skin);Select(key);
    }
    public void DeleteSelectedVertex(Action<Scene>? validate=null)
    {
        RequireStructureEdit();var key=SelectedKey??throw new InvalidOperationException("메시 정점을 선택하세요.");
        Editor.DeleteMeshVertices(key,SelectedVertices,validate);Select(key);
    }
    public void ZoomAt(Point screen,double wheel) { if(!IsDragging)Camera.ZoomAt(screen,wheel); }
    public void SetPose(string? animation,double time,bool deform)
    {
        FinishDrag(true);
        if(!double.IsFinite(time)||time<0||time>3600||deform&&animation is null)throw new InvalidDataException("Pose selection/time");
        if(Editor.PreservesAnimations&&deform&&!Editor.CanEditDeform41)throw new NotSupportedException("Deform editing requires fully decoded animation data");
        if(Editor.IsBinary)time=(double)BinaryEditor.Float32(time);
        Playback.Triangles(Editor.Document,Skin,animation,time);
        Animation=animation;Time=time;DeformMode=deform;
    }
    public void SetSkin(string skin)
    {FinishDrag(true);Playback.Triangles(Editor.Document,skin,Animation,Time);Skin=skin;Select(null);}
    public void Save(string path,string root,string? digest) { FinishDrag(true);Editor.Save(path,root,digest); }
    public bool RequestClose(bool discardEdits) { FinishDrag(true);return !Editor.Dirty||discardEdits; }
    public bool TryOpen(string json,bool discardEdits,Action<Scene,CanvasCamera>? validate=null)=>TryOpen(()=>new Editor(json),discardEdits,validate);
    public bool TryOpen(byte[] bytes,bool discardEdits,Action<Scene,CanvasCamera>? validate=null,bool rotation=false,bool setup4117=false,bool preserveAnimations=false)=>TryOpen(()=>new Editor(bytes,rotation:rotation,setup4117:setup4117,preserveAnimations:preserveAnimations),discardEdits,validate);
    bool TryOpen(Func<Editor> create,bool discardEdits,Action<Scene,CanvasCamera>? validate)
    {
        FinishDrag(true);if(Editor.Dirty&&!discardEdits)return false;
        var next=create();var camera=CameraFor(next.Document,Camera.Viewport);
        var canvas=CanvasFor(next.Document);validate?.Invoke(next.Document,camera);
        Editor=next;Camera=camera;Canvas=canvas;SelectedKey=null;SelectedSlot=null;SelectedVertex=-1;SelectedVertices=[];Animation=null;Time=0;DeformMode=false;Skin="default";return true;
    }
}

