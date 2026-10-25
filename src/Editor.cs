using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace IndependentDev;

internal sealed partial class Editor
{
    public Scene Document { get; private set; }
    readonly LinkedList<(string Json,long Revision)> undo = new(), redo = new();
    readonly bool recordHistory;
    readonly bool geometryPreview;
    Editor(Scene scene){Document=scene;geometryPreview=true;recordHistory=false;historyLimitBytes=0;}
    internal Editor CopyGeometryPreview()=>new(Document);
    public const long MaxHistoryBytes=128L*1024*1024;
    readonly long historyLimitBytes;
    long historyBytes;
    bool historyTruncated;
    public long HistoryBytes {get=>binary?.HistoryBytes??historyBytes;private set=>historyBytes=value;}
    public bool HistoryTruncated {get=>binary?.HistoryTruncated??historyTruncated;private set=>historyTruncated=value;}
    long revision, savedRevision, nextRevision;
    public bool Dirty => binary?.Dirty??revision != savedRevision;
    public long Revision => binary?.Revision??revision;
    public long SavedRevision => binary?.SavedRevision??savedRevision;
    public int UndoCount => binary?.UndoCount??undo.Count;
    public int RedoCount => binary?.RedoCount??redo.Count;
    public Editor(string json,bool recordHistory=true,long historyLimitBytes=MaxHistoryBytes)
    {
        if(historyLimitBytes is <0 or >MaxHistoryBytes)throw new ArgumentOutOfRangeException(nameof(historyLimitBytes));
        Document=Scene.Parse(json);this.recordHistory=recordHistory;this.historyLimitBytes=historyLimitBytes;
    }
    void Remember(LinkedList<(string Json,long Revision)> history,string json,long entryRevision)
    {
        history.AddLast((json,entryRevision));HistoryBytes+=2L*json.Length;
        while(HistoryBytes>historyLimitBytes)
        {
            var oldest=undo.First??redo.First!;HistoryBytes-=2L*oldest.Value.Json.Length;oldest.List!.Remove(oldest);HistoryTruncated=true;
        }
    }
    public void Edit(Action<JsonObject> operation)=>Edit(operation,null);
    void Edit(Action<JsonObject> operation,Action<Scene>? validate)
    {
        if(IsBinary)throw new NotSupportedException("Binary backing cannot accept arbitrary JSON edits");
        var candidate = (JsonObject)Document.Data.DeepClone(); operation(candidate);
        var parsed = Scene.Parse(candidate.ToJsonString());
        validate?.Invoke(parsed);
        string? before=null;
        // Real changes need no text comparison; exact comparison still preserves the existing no-op contract.
        if(JsonNode.DeepEquals(Document.Data,parsed.Data)){before=Document.Data.ToJsonString();if(parsed.Data.ToJsonString()==before)return;}
        if(recordHistory)before??=Document.Data.ToJsonString();
        foreach(var entry in redo)HistoryBytes-=2L*entry.Json.Length;redo.Clear();
        if(recordHistory)Remember(undo,before!,revision);Document=parsed;revision=++nextRevision;
    }
    void Travel(LinkedList<(string Json,long Revision)> source,LinkedList<(string Json,long Revision)> destination)
    {
        if(source.Last is null)return;
        var entry=source.Last.Value;var next=Scene.Parse(entry.Json);var current=Document.Data.ToJsonString();
        source.RemoveLast();HistoryBytes-=2L*entry.Json.Length;Remember(destination,current,revision);Document=next;revision=entry.Revision;
    }
    public void Undo(){if(binary is null)Travel(undo,redo);else if(binary.UndoCount>0){binary.Undo();Document=binary.ReadModel();}}
    public void Redo(){if(binary is null)Travel(redo,undo);else if(binary.RedoCount>0){binary.Redo();Document=binary.ReadModel();}}
    static JsonObject SlotData(JsonObject root,string name)
        =>(Scene.Array(root["slots"])??[]).SingleOrDefault(node=>Scene.Text(node?["name"])==name)?.AsObject()??throw new ArgumentException("Slot selection");
    public string? SlotAttachment(string name)=>SlotData(Document.Data,name)["attachment"]?.GetValue<string>();
    public void SetSlotAttachment(string name,string? placeholder,Action<Scene>? validate=null)
    {
        if(binary is not null&&!BinarySetup4117)throw new NotSupportedException("Binary slot attachment requires explicit 4.1.17 setup profile");var current=SlotAttachment(name);if(placeholder is not null&&!Document.HasAttachment(name,placeholder))throw new ArgumentException("Slot attachment selection");if(current==placeholder)return;
        if(binary is not null)
        {
            var candidate=BinaryEditor.ForSetup4117(binary.Document.UnchangedBytes(),0,PreservesAnimations);candidate.SetSlotAttachment4117(name,placeholder);var next=candidate.ReadModel();validate?.Invoke(next);if(binary.Adopt(candidate.Document))Document=next;
        }
        else Edit(root=>SlotData(root,name)["attachment"]=placeholder,validate);
    }
    public string SlotColor(string name)
    {var text=Scene.Text(SlotData(Document.Data,name)["color"],"ffffffff");return(text.Length==6?text+"ff":text).ToLowerInvariant();}
    public void SetSlotColor(string name,uint color)
    {
        if(binary is not null&&!BinarySetup4117)throw new NotSupportedException("Binary slot color requires explicit 4.1.17 setup profile");var text=color.ToString("x8",System.Globalization.CultureInfo.InvariantCulture);if(Rgba.Parse(SlotColor(name))==Rgba.Parse(text))return;
        if(binary is not null){binary.SetSlotColor4117(name,color);Document=binary.ReadModel();}else Edit(root=>SlotData(root,name)["color"]=text);
    }
    JsonObject AttachmentData(JsonObject root,string key)
    {
        if(!Document.Meshes.TryGetValue(key,out var mesh))throw new ArgumentException("Attachment selection");
        return root["skins"]!.AsArray().Single(node=>Scene.Text(node?["name"])==mesh.Skin)!["attachments"]![mesh.Slot]![mesh.Name]!.AsObject();
    }
    public string AttachmentColor(string key)
    {var text=Scene.Text(AttachmentData(Document.Data,key)["color"],"ffffffff");return(text.Length==6?text+"ff":text).ToLowerInvariant();}
    public void SetAttachmentColor(string key,uint color)
    {
        if(binary is not null&&!BinarySetup4117)throw new NotSupportedException("Binary attachment color requires explicit 4.1.17 setup profile");
        var source=AttachmentData(Document.Data,key);if(Scene.Text(source["type"],"region") is not("region" or "mesh" or "linkedmesh"))throw new NotSupportedException("Attachment color editing profile");var text=color.ToString("x8",System.Globalization.CultureInfo.InvariantCulture);if(Rgba.Parse(AttachmentColor(key))==Rgba.Parse(text))return;
        if(binary is not null){var mesh=Document.Meshes[key];binary.SetAttachmentColor4117(mesh.Slot,mesh.Name,color);Document=binary.ReadModel();}
        else Edit(root=>AttachmentData(root,key)["color"]=text);
    }
    static Point ShiftLocal(Point value,Point delta,Affine linear,Affine inverse,Point current,Point target)
    {
        var result=value+delta;
        // Reproject the old value when the intermediate local shift cannot be represented.
        if(!double.IsFinite(result.X)||!double.IsFinite(result.Y))
        {
            var projected=inverse.Apply(target-(current-linear.Apply(value)));
            if((linear.A!=0||linear.C!=0)&&(linear.B!=0||linear.D!=0)&&DependentColumns(linear))
            {
                // Complementary rank1 projector preserves the original invisible component without subtracting near-equal diagonals.
                var p=inverse*linear;result=new Affine(p.D,-p.B,-p.C,p.A,projected.X,projected.Y).Apply(value);
            }
            else result=new(inverse.A==0&&inverse.B==0?value.X:projected.X,inverse.C==0&&inverse.D==0?value.Y:projected.Y);
        }
        result.Validate();return result;
    }
    internal static Point TargetTolerance(Point target,Point current)
    {
        var scale=Math.Min(1,Math.Max(Math.Max(Math.Abs(current.X),Math.Abs(current.Y)),Math.Max(Math.Abs(target.X),Math.Abs(target.Y))));var floor=1e-9*scale;
        return new(floor+1e-7*Math.Abs(target.X),floor+1e-7*Math.Abs(target.Y));
    }
    internal static void RequireTarget(Point actual,Point target,Point current)
    {
        actual.Validate();var tolerance=TargetTolerance(target,current);
        if(Math.Abs(actual.X-target.X)>tolerance.X||Math.Abs(actual.Y-target.Y)>tolerance.Y)throw new InvalidOperationException("Edit target numeric precision");
    }
    static Affine EditLinear(Mesh mesh,int index,Affine[] pose,int bone)
    {
        if(mesh.Weights.Length==0)return pose[bone] with{X=0,Y=0};
        var influences=mesh.Weights[index];double a=0,b=0,c=0,d=0;var nonzero=0;
        foreach(var w in influences)
        {
            var wa=pose[w.Bone].A*w.Weight;var wb=pose[w.Bone].B*w.Weight;var wc=pose[w.Bone].C*w.Weight;var wd=pose[w.Bone].D*w.Weight;
            nonzero|=(wa!=0?1:0)|(wb!=0?2:0)|(wc!=0?4:0)|(wd!=0?8:0);a+=wa;b+=wb;c+=wc;d+=wd;
        }
        double Retry(double value,int bit,Func<Affine,double> component)=>double.IsFinite(value)&&(value!=0||(nonzero&bit)==0)?value:Numeric.RetrySum(value,influences.Select(w=>component(pose[w.Bone])*w.Weight).ToArray());
        return new(Retry(a,1,static p=>p.A),Retry(b,2,static p=>p.B),Retry(c,4,static p=>p.C),Retry(d,8,static p=>p.D),0,0);
    }
    static bool DependentColumns(Affine m)=>((m.A==0||m.D==0)&&(m.B==0||m.C==0))||Numeric.OppositeProducts(m.A,m.D,-m.B,m.C);
    static Affine EditInverse(Affine linear,Point delta)
    {
        if(!DependentColumns(linear))return linear.Inverse();
        linear.Validate();var scale=Math.Max(Math.Max(Math.Abs(linear.A),Math.Abs(linear.B)),Math.Max(Math.Abs(linear.C),Math.Abs(linear.D)));if(scale==0)throw new InvalidOperationException("Singular edit transform");
        var n=new Affine(linear.A/scale,linear.B/scale,linear.C/scale,linear.D/scale,0,0);
        var norm=n.A*n.A+n.B*n.B+n.C*n.C+n.D*n.D;var transpose=new Affine(n.A/norm,n.C/norm,n.B/norm,n.D/norm,0,0);
        var inverse=new Affine(transpose.A/scale,transpose.B/scale,transpose.C/scale,transpose.D/scale,0,0);var projection=n*transpose;
        try{inverse.Validate();delta.Validate();RequireTarget(projection.Apply(delta),delta,delta);}
        catch(Exception error)when(error is InvalidDataException or InvalidOperationException){throw new InvalidOperationException("Singular edit transform",error);}
        return inverse;
    }
    static JsonArray Numbers(double[] numbers)=>new(numbers.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray());
    public void MoveDeformedVertex(string key, int index, Point target, string animation, double time,string skin="default",bool createKey=false)
        =>MoveDeformedVertices(key,new Dictionary<int,Point>{{index,target}},animation,time,skin,createKey);
    internal void MoveDeformedVertices(string key,IReadOnlyDictionary<int,Point> targets,string animation,double time,string skin="default",bool createKey=false)
    {
        foreach(var target in targets.Values)target.Validate();if (!double.IsFinite(time) || time < 0) throw new ArgumentOutOfRangeException(nameof(time));
        var mesh = Playback.DeformTarget(Document,Document.Meshes[key]);
        var source = Document.Data["skins"]!.AsArray().Single(s => Scene.Text(s?["name"]) == mesh.Skin)!["attachments"]![mesh.Slot]![mesh.Name]!;
        if (Scene.Text(source["type"]) is not ("mesh" or "linkedmesh")) throw new NotSupportedException("Region deform editing deferred");
        var pose = Document.Pose(animation,time,skin);
        if(targets.Count==0||targets.Keys.Any(index=>index<0||index>=mesh.UV.Length))throw new ArgumentOutOfRangeException(nameof(targets));
        var needed = mesh.DeformLength;
        var values = Playback.Deform(Document,mesh,animation,time) ?? new double[needed];
        var current=Document.Vertices(mesh,pose,values);
        if(targets.All(p=>p.Value==current[p.Key].Position)&&!createKey)return;
        var starts=new int[mesh.UV.Length];for(var i=1;i<starts.Length;i++)starts[i]=starts[i-1]+(mesh.Weights.Length==0?2:mesh.Weights[i-1].Length*2);
        var bone=Document.Slots.Single(s=>s.Name==mesh.Slot).Bone;
        foreach(var (index,target) in targets)
        {
            var position=current[index].Position;if(target==position)continue;var delta=target-position;var combined=EditLinear(mesh,index,pose,bone);
            var inverse=EditInverse(combined,delta);var offset=inverse.Apply(delta);
            var start=starts[index];
            var count=mesh.Weights.Length==0 ? 1 : mesh.Weights[index].Length;
            for(var i=0;i<count;i++){var at=start+i*2;var shifted=ShiftLocal(new(values[at],values[at+1]),offset,combined,inverse,position,target);values[at]=shifted.X;values[at+1]=shifted.Y;}
        }
        Edit(root=>
        {
            JsonObject Object(JsonObject parent,string field) {if(parent[field] is null) parent[field]=new JsonObject();return parent[field]!.AsObject();}
            var anim=root["animations"]?[animation] as JsonObject ?? throw new InvalidDataException("Animation required");
            var modern=Document.Version.StartsWith("4.1.");
            var parent=Object(Object(Object(anim,modern?"attachments":"deform"),mesh.Skin),mesh.Slot);
            if(modern) parent=Object(parent,mesh.Name);
            var field=modern?"deform":mesh.Name;
            if(parent[field] is null) parent[field]=new JsonArray();
            var frames=parent[field]!.AsArray();
            var existing=frames.FirstOrDefault(k=>Scene.Number(k?["time"])==time);
            var frame=existing?.DeepClone().AsObject() ?? new JsonObject();
            frame["time"]=time;frame.Remove("offset");frame["vertices"]=Numbers(values);
            var position=0;while(position<frames.Count && Scene.Number(frames[position]?["time"])<time)position++;
            if(existing is null&&position==0&&frames.Count>0)frame["curve"]="stepped";
            if(existing is null&&position>0&&position<frames.Count)
            {
                var left=frames[position-1]!.AsObject();var curve=Timeline.NormalizedCurve(left);
                if(curve is JsonArray)
                {
                    var from=Scene.Number(left["time"]);var to=Scene.Number(frames[position]?["time"]);var x=(time-from)/(to-from);var controls=Scene.Values(curve);
                    var amount=Curves.Ease(x,controls[0],controls[1],controls[2],controls[3]);(double[] Left,double[] Right)? split=null;
                    // De Casteljau and playback can round an endpoint crossing differently.
                    if(amount is not (0 or 1))
                    {try{split=Curves.SplitEase(x,controls);}catch(NotSupportedException error)when(error.Message=="Bezier subdivision has zero value span") {}}
                    if(split is {} simple){left["curve"]=Numbers(simple.Left);frame["curve"]=Numbers(simple.Right);}
                    else
                    {
                        var inserted=false;
                        foreach(var ratio in new[]{.5,.25,.75,.125})
                        {
                            foreach(var before in new[]{true,false})
                            {
                                if(amount==0&&!before||amount==1&&before)continue;
                                var at=before?double.Lerp(from,time,ratio):double.Lerp(time,to,ratio);
                                if(at<=from||at>=to||(before?at>=time:at<=time))continue;
                                var supportX=(at-from)/(to-from);
                                if(Curves.Ease(supportX,controls[0],controls[1],controls[2],controls[3]) is 0 or 1)continue;
                                (double[] Left,double[] Right) support,part;
                                try
                                {
                                    support=Curves.SplitEase(supportX,controls);var segment=before?support.Right:support.Left;
                                    var cut=before?(time-at)/(to-at):(time-from)/(at-from);
                                    if(Curves.Ease(cut,segment[0],segment[1],segment[2],segment[3]) is 0 or 1)continue;
                                    part=Curves.SplitEase(cut,segment);
                                }
                                catch(NotSupportedException error)when(error.Message=="Bezier subdivision has zero value span"){continue;}
                                var extra=new JsonObject{["time"]=at,["vertices"]=Numbers(Playback.Deform(Document,mesh,animation,at)??throw new InvalidDataException("Deform support values"))};
                                left["curve"]=Numbers(before?support.Left:part.Left);frame["curve"]=Numbers(part.Right);extra["curve"]=Numbers(before?part.Left:support.Right);
                                frames.Insert(position,extra);if(before)position++;inserted=true;break;
                            }
                            if(inserted)break;
                        }
                        if(!inserted)throw new NotSupportedException("Bezier subdivision has zero value span");
                    }
                    foreach(var extra in new[]{"c2","c3","c4"})left.Remove(extra);
                }
                else if(curve is not null)frame["curve"]=curve.DeepClone();
            }
            if(existing is null) frames.Insert(position,frame);else frames[position]=frame;
        },next=>
        {
            var actual=next.Vertices(next.Meshes[key],next.Pose(animation,time,skin),Playback.Deform(next,next.Meshes[key],animation,time));
            foreach(var (index,target) in targets)RequireTarget(actual[index].Position,target,current[index].Position);
        });
    }
    public void MoveVertex(string key, int index, Point target, Affine[] pose, double[]? deform = null)
        =>MoveVertices(key,new Dictionary<int,Point>{{index,target}},pose,deform);
    internal void MoveVertices(string key,IReadOnlyDictionary<int,Point> targets,Affine[] pose,double[]? deform=null)
    {
        foreach(var target in targets.Values)target.Validate();var mesh=Document.Meshes[key];
        if (Scene.Text(mesh.Data["type"]) != "mesh") throw new NotSupportedException("Only mesh vertex editing");
        if(targets.Count==0||targets.Keys.Any(index=>index<0||index>=mesh.UV.Length)||pose.Length!=Document.Bones.Length)throw new ArgumentOutOfRangeException(nameof(targets));
        var current=Document.Vertices(mesh,pose,deform);if(targets.All(p=>p.Value==current[p.Key].Position))return;
        var bone = Document.Slots.Single(s => s.Name == mesh.Slot).Bone;
        var values=Scene.Values(mesh.Data["vertices"]);var starts=new int[mesh.UV.Length];for(var i=1;i<starts.Length;i++)starts[i]=starts[i-1]+(mesh.Weights.Length==0?2:1+mesh.Weights[i-1].Length*4);
        foreach(var (index,target) in targets)
        {
            var position=current[index].Position;if(target==position)continue;var delta=target-position;var combined=EditLinear(mesh,index,pose,bone);var inverse=EditInverse(combined,delta);var localDelta=inverse.Apply(delta);
            if(mesh.Weights.Length==0){var at=starts[index];var shifted=ShiftLocal(new(values[at],values[at+1]),localDelta,combined,inverse,position,target);values[at]=shifted.X;values[at+1]=shifted.Y;}
            else
            {
                var offset=starts[index]+1;
                for(var j=0;j<mesh.Weights[index].Length;j++,offset+=4){var shifted=ShiftLocal(new(values[offset+1],values[offset+2]),localDelta,combined,inverse,position,target);values[offset+1]=shifted.X;values[offset+2]=shifted.Y;}
            }
        }
        void Validate(Scene next){var actual=next.Vertices(next.Meshes[key],pose,deform);foreach(var (index,target) in targets)RequireTarget(actual[index].Position,target,current[index].Position);}
        // Linked mesh geometry belongs to its resolved parent; edit that node once.
        var owner = Document.Meshes.Values.First(m => ReferenceEquals(m.Data, mesh.Data) && Scene.Text(m.Data["type"]) == "mesh");
        if(geometryPreview)
        {
            var next=Document.PreviewMeshVertices(Scene.Key(owner.Skin,owner.Slot,owner.Name),values);Validate(next);Document=next;return;
        }
        Edit(root =>
        {
            var skin = root["skins"]!.AsArray().Single(n => Scene.Text(n?["name"]) == owner.Skin)!;
            var attachments = skin["attachments"]![owner.Slot]!.AsObject();
            var source = attachments[owner.Name]!.AsObject();
            source["vertices"] = Numbers(values);
        },Validate);
    }
    public static string? Digest(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    internal static void ReplaceChecked(string temporary,string path,string expectedDigest,Action<string,string,string?>? replace=null,Action<string>? checkPath=null)
    {
        replace??=File.Replace;checkPath??=Experiment.CheckOutput;
        for(var attempt=0;;attempt++)
        {
            checkPath(path);checkPath(temporary);checkPath(path+".bak");
            if(Directory.Exists(path+".bak"))throw new IOException("Backup destination is a directory");
            if(Digest(path)!=expectedDigest)throw new IOException("Destination changed during save");
            try{replace(temporary,path,File.Exists(path+".bak")?null:path+".bak");return;}
            // Only these failures keep both names; never retry partial-move errors 1176/1177.
            catch(IOException error)when(attempt<3&&unchecked((uint)error.HResult) is 0x80070020 or 0x80070021 or 0x80070497&&File.Exists(temporary))
            {Thread.Sleep(20*(attempt+1));}
        }
    }
    public void Save(string path, string experimentRoot, string? expectedDigest)
    {
        if(geometryPreview)throw new InvalidOperationException("Commit the drag before saving a geometry preview");
        Experiment.CheckOutput(path);
        var root = Path.GetFullPath(experimentRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(path),IsBinary?".skel":".json",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(IsBinary?"Binary save extension":"JSON save extension");
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Save outside experiment root");
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(path)!); dir is not null && dir.FullName.Length >= root.Length - 1; dir = dir.Parent)
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked save directory");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked save target");
        if (Digest(path) != expectedDigest) throw new IOException("External modification or destination conflict");
        if(binary is not null){binary.SaveChecked(path,expectedDigest);return;}
        var json = Document.Data.ToJsonString(); Scene.Parse(json); var temporary = Path.Combine(Path.GetDirectoryName(path)!,Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { var bytes = Encoding.UTF8.GetBytes(json); stream.Write(bytes); stream.Flush(true); }
            Scene.Parse(File.ReadAllText(temporary));
            if (Digest(path) != expectedDigest) throw new IOException("Destination changed during save");
            if (expectedDigest is null) File.Move(temporary, path, false);
            else ReplaceChecked(temporary,path,expectedDigest);
            savedRevision = revision;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

