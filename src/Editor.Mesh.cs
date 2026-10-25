using System.Text.Json.Nodes;
namespace IndependentDev;

internal sealed partial class Editor
{
    internal JsonObject RawAttachment(string key)=>AttachmentData(Document.Data,key);
    void RequireMeshStructure()
    {
        if(!CanEditMeshStructure)throw new NotSupportedException("이 SKEL의 애니메이션을 완전히 판독하지 못해 메시 구조를 안전하게 변경할 수 없습니다.");
    }
    public string AddMesh(string skin,string slot,string name,AtlasRegion region,Action<Scene>? validate=null)
    {
        if(binary is not null){var key="";Document=binary.EditMeshStructure(editor=>key=editor.AddMesh(skin,slot,name,region),validate);return key;}
        RequireMeshStructure();
        if(string.IsNullOrWhiteSpace(name)||name.Contains('\0'))throw new ArgumentException("메시 이름을 입력하세요.");
        if(!Document.Slots.Any(s=>s.Name==slot))throw new ArgumentException("슬롯을 선택하세요.");
        var skinData=Scene.Array(Document.Data["skins"])?.SingleOrDefault(s=>Scene.Text(s?["name"])==skin);
        if(skinData is null&&skin!="default")throw new ArgumentException("스킨을 선택하세요.");
        if(skinData?["attachments"]?[slot]?[name] is not null)throw new InvalidOperationException("같은 이름의 어태치먼트가 있습니다.");
        var current=Document.Slots.Single(s=>s.Name==slot).Attachment;
        string? Resolve(string other,string? attachment)=>attachment is null?null:Document.HasSkinAttachment(other,slot,attachment)?Scene.Key(other,slot,attachment):Document.HasSkinAttachment("default",slot,attachment)?Scene.Key("default",slot,attachment):null;
        foreach(var other in new[]{"default"}.Concat((Scene.Array(Document.Data["skins"])??[]).Select(s=>Scene.Text(s?["name"]))).Distinct().Where(s=>s!=skin))
        {
            var next=Document.HasSkinAttachment(other,slot,name)?Scene.Key(other,slot,name):skin=="default"?Scene.Key("default",slot,name):Resolve(other,name);
            if(Resolve(other,current)!=next)throw new InvalidOperationException("다른 스킨의 표시를 바꾸는 메시 추가입니다. 다른 스킨의 어태치먼트 구성을 먼저 확인하세요.");
        }
        if(region.OriginalWidth<=0||region.OriginalHeight<=0)throw new InvalidDataException("Atlas image dimensions");
        var w=region.OriginalWidth/2d;var h=region.OriginalHeight/2d;
        Edit(root=>
        {
            if(root["skins"] is null)root["skins"]=new JsonArray();var skins=root["skins"]!.AsArray();
            var target=skins.SingleOrDefault(s=>Scene.Text(s?["name"])==skin)?.AsObject();if(target is null){target=new JsonObject{["name"]=skin};skins.Add(target);}
            if(target["attachments"] is null)target["attachments"]=new JsonObject();
            var attachments=target["attachments"]!.AsObject();if(attachments[slot] is null)attachments[slot]=new JsonObject();
            attachments[slot]![name]=new JsonObject{["type"]="mesh",["path"]=region.Name,["uvs"]=Numbers([0,1,1,1,1,0,0,0]),["vertices"]=Numbers([-w,-h,w,-h,w,h,-w,h]),["triangles"]=Numbers([0,1,2,0,2,3]),["hull"]=4,["width"]=region.OriginalWidth,["height"]=region.OriginalHeight};
            SlotData(root,slot)["attachment"]=name;
        },validate);
        return Scene.Key(skin,slot,name);
    }
    public void DeleteMesh(string key,Action<Scene>? validate=null)
    {
        if(binary is not null){Document=binary.EditMeshStructure(editor=>editor.DeleteMesh(key),validate);return;}
        RequireMeshStructure();var mesh=Document.Meshes[key];var type=Scene.Text(RawAttachment(key)["type"]);
        if(type is not("mesh" or "linkedmesh"))throw new InvalidOperationException("삭제할 메시를 선택하세요.");
        foreach(var skin in Document.Data["skins"]!.AsArray())foreach(var slot in Scene.Object(skin?["attachments"])??[])foreach(var attachment in Scene.Object(slot.Value)??[])
        {
            var data=attachment.Value!;
            if(Scene.Text(data["type"])=="linkedmesh"&&slot.Key==mesh.Slot&&Scene.Text(data["skin"],"default")==mesh.Skin&&Scene.Text(data["parent"])==mesh.Name)
                throw new InvalidOperationException("이 메시를 참조하는 Linked Mesh를 먼저 삭제하세요: "+attachment.Key);
        }
        Edit(root=>
        {
            var target=root["skins"]!.AsArray().Single(s=>Scene.Text(s?["name"])==mesh.Skin)!;
            target["attachments"]![mesh.Slot]!.AsObject().Remove(mesh.Name);
            var stillExists=root["skins"]!.AsArray().Any(s=>s?["attachments"]?[mesh.Slot]?[mesh.Name] is not null);
            if(!stillExists&&Scene.Text(SlotData(root,mesh.Slot)["attachment"])==mesh.Name)SlotData(root,mesh.Slot).Remove("attachment");
            foreach(var animation in Scene.Object(root["animations"])??[])
            {
                var anim=animation.Value!;var field=Document.Version.StartsWith("4.1.")?"attachments":"deform";
                Scene.Object(anim[field]?[mesh.Skin]?[mesh.Slot])?.Remove(mesh.Name);
                if(!stillExists)foreach(var frame in Scene.Array(anim["slots"]?[mesh.Slot]?["attachment"])??[])
                    if(Scene.Text(frame?["name"])==mesh.Name)frame!["name"]=null;
            }
        },validate);
    }
    public int AddMeshVertex(string key,Point world,Action<Scene>? validate=null,string skin="default")
    {
        if(binary is not null){var index=-1;Document=binary.EditMeshStructure(editor=>index=editor.AddMeshVertex(key,world,skin:skin),validate);return index;}
        RequireMeshStructure();world.Validate();var mesh=Document.Meshes[key];
        if(Scene.Text(mesh.Data["type"])!="mesh")throw new InvalidOperationException("메시를 선택하세요.");
        var positions=Document.Vertices(mesh,Document.Pose(skin:skin)).Select(v=>v.Position).ToArray();
        var added=-1;
        Edit(root=>added=MeshTopology.Insert(Document,root,key,positions,world,skin),validate);
        return added;
    }
    public void DeleteMeshVertex(string key,int vertex,Action<Scene>? validate=null)
        =>DeleteMeshVertices(key,[vertex],validate);
    public void DeleteMeshVertices(string key,IEnumerable<int> vertices,Action<Scene>? validate=null)
    {
        var selected=vertices.Distinct().OrderDescending().ToArray();
        if(binary is not null){Document=binary.EditMeshStructure(editor=>editor.DeleteMeshVertices(key,selected),validate);return;}
        RequireMeshStructure();var mesh=Document.Meshes[key];
        if(selected.Length==0||selected.Any(i=>i<0||i>=mesh.UV.Length))throw new InvalidDataException("Vertex selection reference");
        if(mesh.UV.Length-selected.Length<3)throw new InvalidOperationException("메시에는 최소 세 정점이 필요합니다. 파츠 전체 삭제를 사용하세요.");
        Edit(root=>MeshTopology.Delete(Document,root,key,selected.ToHashSet()),validate);
    }
}

internal static partial class MeshTopology
{
    readonly record struct Sample(int Vertex,double Amount);
    static (int,int) Edge(int a,int b)=>a<b?(a,b):(b,a);
    static JsonArray Values(IEnumerable<double> values)=>new(values.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray());
    static JsonObject Definition(JsonObject root,Mesh mesh)=>root["skins"]!.AsArray().Single(s=>Scene.Text(s?["name"])==mesh.Skin)!["attachments"]![mesh.Slot]![mesh.Name]!.AsObject();
    static Mesh Source(Scene scene,string key)
    {
        var selected=scene.Meshes[key];
        if(Scene.Text(selected.Data["type"])!="mesh")throw new InvalidOperationException("메시를 선택하세요.");
        return scene.Meshes.Values.First(m=>ReferenceEquals(m.Data,selected.Data)&&Definition(scene.Data,m)==m.Data);
    }
    public static int Insert(Scene scene,JsonObject root,string key,Point[] positions,Point target,string skin)
    {
        var mesh=Source(scene,key);var count=mesh.UV.Length;if(count>=100_000)throw new InvalidDataException("Vertex count limit");
        var triangles=mesh.Triangles.Chunk(3).Select(t=>t.ToArray()).ToList();Sample[]? blend=null;(int,int)? split=null;var chosen=-1;
        for(var i=0;i<triangles.Count;i++)
        {
            var t=triangles[i];var a=positions[t[0]];var b=positions[t[1]];var c=positions[t[2]];
            var size=new[]{(b-a).Length,(c-a).Length,(target-a).Length}.Max();if(size==0||!double.IsFinite(size))continue;
            b=(b-a)*(1/size);c=(c-a)*(1/size);var p=(target-a)*(1/size);var area=Point.Cross(b,c);if(area==0)continue;
            double[] weights=[1-(Point.Cross(p,c)+Point.Cross(b,p))/area,Point.Cross(p,c)/area,Point.Cross(b,p)/area];
            if(weights.Any(w=>!double.IsFinite(w)||w< -1e-9))continue;
            for(var j=0;j<3;j++)if(weights[j]<1e-9)weights[j]=0;
            var sum=weights.Sum();blend=t.Select((v,j)=>new Sample(v,weights[j]/sum)).Where(s=>s.Amount>0).ToArray();
            if(blend.Length==1)throw new InvalidOperationException("이미 정점이 있는 위치입니다.");
            if(blend.Length==2)split=Edge(blend[0].Vertex,blend[1].Vertex);chosen=i;break;
        }
        Point? outsideUV=null;List<int>? extension=null;
        if(blend is null)(blend,outsideUV,extension)=Extend(mesh,positions,target);
        var output=new List<int>();
        for(var i=0;i<triangles.Count;i++)
        {
            var t=triangles[i];var edge=-1;if(split is{} pair)for(var j=0;j<3;j++)if(Edge(t[j],t[(j+1)%3])==pair)edge=j;
            if(edge>=0){var a=t[edge];var b=t[(edge+1)%3];var c=t[(edge+2)%3];output.AddRange([a,count,c,count,b,c]);}
            else if(i==chosen&&split is null)output.AddRange([t[0],t[1],count,t[1],t[2],count,t[2],t[0],count]);
            else output.AddRange(t);
        }
        if(extension is not null)output.AddRange(extension);
        var samples=Enumerable.Range(0,count).Select(i=>new[]{new Sample(i,1)}).Append(blend).ToList();
        var added=Apply(scene,root,mesh,samples,output,split,count,out var _,outsideUV);
        if(outsideUV is not null)
        {
            // Reuse the checked world-to-local solver, including weighted and singular transforms.
            var candidate=new Editor(root.ToJsonString(),recordHistory:false);candidate.MoveVertex(key,added,target,candidate.Document.Pose(skin:skin));
            Definition(root,mesh)["vertices"]=candidate.Document.Meshes[key].Data["vertices"]!.DeepClone();
        }
        return added;
    }
    static (Sample[] Blend,Point UV,List<int> Triangles) Extend(Mesh mesh,Point[] positions,Point target)
    {
        var uses=new Dictionary<(int,int),List<(int A,int B,int C)>>();
        foreach(var t in mesh.Triangles.Chunk(3))for(var j=0;j<3;j++)
        {
            var a=t[j];var b=t[(j+1)%3];var c=t[(j+2)%3];var edge=Edge(a,b);
            if(!uses.TryGetValue(edge,out var list))uses[edge]=list=[];list.Add((a,b,c));
        }
        var boundary=uses.Values.Where(v=>v.Count==1).Select(v=>v[0]).ToArray();
        var scale=positions.Max(p=>(p-target).Length);if(!(scale>0)||!double.IsFinite(scale))throw new InvalidDataException("Mesh extension numeric range");
        var points=positions.Select(p=>new Point((p.X-target.X)/scale,(p.Y-target.Y)/scale)).ToArray();var nearest=-1;var distance=double.PositiveInfinity;double amount=0;
        for(var i=0;i<boundary.Length;i++)
        {
            var (a,b,c)=boundary[i];var d=points[b]-points[a];var size=d.X*d.X+d.Y*d.Y;if(size==0)continue;
            if(Math.Sign(Point.Cross(d,points[a]*-1))*Math.Sign(Point.Cross(d,points[c]-points[a]))>=0)continue;
            var t=Math.Clamp(-(points[a].X*d.X+points[a].Y*d.Y)/size,0,1);var length=(points[a]+d*t).Length;
            if(length<distance){nearest=i;distance=length;amount=t;}
        }
        if(nearest<0)throw new InvalidOperationException("외곽선을 확장할 수 없는 메시입니다.");
        var edge0=boundary[nearest];var origin=points[edge0.A];var ab=points[edge0.B]-origin;var ac=points[edge0.C]-origin;var area=Point.Cross(ab,ac);
        var wb=Point.Cross(origin*-1,ac)/area;var wc=Point.Cross(ab,origin*-1)/area;
        var uv=mesh.UV[edge0.A]*(1-wb-wc)+mesh.UV[edge0.B]*wb+mesh.UV[edge0.C]*wc;uv.Validate();
        if(uv.X< -1e-9||uv.X>1+1e-9||uv.Y< -1e-9||uv.Y>1+1e-9)throw new InvalidOperationException("메시 외부에도 추가할 수 있지만, 해당 파츠의 이미지 범위 안을 클릭하세요.");
        uv=new(Math.Clamp(uv.X,0,1),Math.Clamp(uv.Y,0,1));
        var work=20_000_000;
        bool Visible((int A,int B,int C) e,Point[] p,Point apex)
        {
            var a=p[e.A];var b=p[e.B];var sign=Math.Sign(Point.Cross(b-a,p[e.C]-a));
            if(sign==0||sign*Point.Cross(b-a,apex-a)>=0)return false;
            bool Between(Point x,Point y,Point z)=>Point.Cross(y-x,z-x)==0&&(z-x).X*(z-y).X+(z-x).Y*(z-y).Y<=0;
            bool Crosses(Point x,Point y,Point z,Point w)
            {
                var s=Point.Cross(y-x,z-x);var t=Point.Cross(y-x,w-x);var u=Point.Cross(w-z,x-z);var v=Point.Cross(w-z,y-z);
                return Math.Sign(s)*Math.Sign(t)<0&&Math.Sign(u)*Math.Sign(v)<0;
            }
            foreach(var other in boundary)
            {
                if(--work<0)throw new InvalidDataException("Mesh extension work limit");
                var q=p[other.A];
                if(other.A!=e.A&&other.A!=e.B&&(-sign*Point.Cross(b-a,q-a)>=0&&-sign*Point.Cross(apex-b,q-b)>=0&&-sign*Point.Cross(a-apex,q-apex)>=0))return false;
                if(Crosses(a,apex,q,p[other.B])||Crosses(b,apex,q,p[other.B]))return false;
                if(other.A!=e.A&&Between(a,apex,q)||other.A!=e.B&&Between(b,apex,q))return false;
            }
            return true;
        }
        var visible=new HashSet<int>();
        for(var i=0;i<boundary.Length;i++)if(Visible(boundary[i],mesh.UV,uv)&&Visible(boundary[i],points,new()))visible.Add(i);
        if(!visible.Contains(nearest))throw new InvalidOperationException("기존 메시와 겹치지 않는 외곽 위치를 클릭하세요.");
        var incident=new Dictionary<int,List<int>>();foreach(var i in visible)foreach(var v in new[]{boundary[i].A,boundary[i].B}){if(!incident.TryGetValue(v,out var list))incident[v]=list=[];list.Add(i);}
        var connected=new HashSet<int>();var pending=new Stack<int>();pending.Push(nearest);var triangles=new List<int>();
        while(pending.TryPop(out var i))if(connected.Add(i))
        {
            var e=boundary[i];triangles.AddRange([e.B,e.A,mesh.UV.Length]);
            foreach(var v in new[]{e.A,e.B})foreach(var next in incident[v])if(!connected.Contains(next))pending.Push(next);
        }
        // Positive edge interpolation preserves valid bone weights; only the new vertex is moved outward.
        Sample[] blend=amount==0?[new(edge0.A,1)]:amount==1?[new(edge0.B,1)]:[new(edge0.A,1-amount),new(edge0.B,amount)];
        return(blend,uv,triangles);
    }
    public static void Delete(Scene scene,JsonObject root,string key,IReadOnlySet<int> selected)
    {
        var mesh=Source(scene,key);var count=mesh.UV.Length;var hull=Boundary(mesh.Triangles);
        if(mesh.Data["hull"] is{} declared)
        {
            var size=Scene.Integer(declared);
            if(size!=hull.Length||!hull.ToHashSet().SetEquals(Enumerable.Range(0,size))||!Enumerable.Range(0,size).All(i=>HasEdge(hull,i,(i+1)%size)))throw new InvalidOperationException("저장된 외곽선과 삼각형 경계가 일치하지 않아 삭제할 수 없습니다.");
        }
        var desiredHull=hull.Where(i=>!selected.Contains(i)).ToArray();
        if(desiredHull.Length<3)throw new InvalidOperationException("외곽선에는 최소 세 정점이 필요합니다.");
        var polygon=desiredHull.Select(i=>mesh.UV[i]).ToArray();Polygons.Triangulate(polygon);
        if((long)count*desiredHull.Length>20_000_000)throw new InvalidDataException("Mesh deletion work limit");
        foreach(var i in Enumerable.Range(0,count).Except(hull).Where(i=>!selected.Contains(i)))
            if(!Inside(mesh.UV[i],polygon))throw new InvalidOperationException($"내부 정점 {i}이 새 외곽선 밖에 있거나 테두리와 겹칩니다. 해당 정점을 먼저 이동하거나 함께 삭제하세요.");
        var kept=RetriangulateDeletion(mesh,selected,desiredHull);
        var actualHull=Boundary(kept);
        if(!actualHull.ToHashSet().SetEquals(desiredHull)||!Enumerable.Range(0,desiredHull.Length).All(i=>HasEdge(actualHull,desiredHull[i],desiredHull[(i+1)%desiredHull.Length])))throw new InvalidOperationException("삭제가 다른 정점의 외곽·내부 구분을 변경합니다.");
        ValidateTriangles(mesh.UV,kept,desiredHull);
        var remap=Enumerable.Range(0,count).Where(i=>!selected.Contains(i)).ToArray();var indices=remap.Select((old,index)=>(old,index)).ToDictionary(p=>p.old,p=>p.index);
        Apply(scene,root,mesh,remap.Select(i=>new[]{new Sample(i,1)}).ToList(),kept.Select(i=>indices[i]).ToList(),null,-1,out _);
    }
    static int Apply(Scene scene,JsonObject root,Mesh mesh,List<Sample[]> samples,List<int> triangles,(int,int)? split,int added,out Dictionary<int,int> oldToNew,Point? addedUV=null)
    {
        // shortcut: topology edits require one simple boundary; add constrained triangulation when supporting holes or separate islands.
        var uv=samples.Select(s=>s.Aggregate(new Point(),(p,v)=>p+mesh.UV[v.Vertex]*v.Amount)).ToArray();
        if(addedUV is{} outside)uv[added]=outside;
        var edges=new Dictionary<(int,int),int>();foreach(var t in triangles.Chunk(3))
        {
            if(t.Distinct().Count()!=3||Point.Orientation(uv[t[0]],uv[t[1]],uv[t[2]])==0)throw new InvalidDataException("Degenerate mesh triangle");
            for(var j=0;j<3;j++){var e=Edge(t[j],t[(j+1)%3]);edges[e]=edges.GetValueOrDefault(e)+1;if(edges[e]>2)throw new InvalidDataException("Nonmanifold mesh edge");}
        }
        var boundary=new Dictionary<int,List<int>>();foreach(var (edge,uses) in edges)if(uses==1)foreach(var (a,b) in new[]{(edge.Item1,edge.Item2),(edge.Item2,edge.Item1)}){if(!boundary.TryGetValue(a,out var list))boundary[a]=list=[];list.Add(b);}
        if(boundary.Count<3||boundary.Any(p=>p.Value.Count!=2))throw new NotSupportedException("닫힌 외곽선 하나를 가진 메시만 구조 편집할 수 있습니다.");
        var order=new List<int>();var visited=new HashSet<int>();var start=boundary.Keys.Min();var current=start;var previous=-1;
        do{if(!visited.Add(current))throw new InvalidDataException("Mesh boundary cycle");order.Add(current);var next=boundary[current].First(v=>v!=previous);previous=current;current=next;}while(current!=start);
        if(order.Count!=boundary.Count)throw new NotSupportedException("구멍이나 분리된 영역이 있는 메시의 구조 편집은 지원하지 않습니다.");
        var hull=order.Count;order.AddRange(Enumerable.Range(0,samples.Count).Except(order));var map=order.Select((old,index)=>(old,index)).ToDictionary(p=>p.old,p=>p.index);
        var raw=Definition(root,mesh);var weighted=mesh.Weights.Length>0;var influences=new List<Influence[]>();var vertices=new List<double>();
        foreach(var index in order)
        {
            var sample=samples[index];
            if(!weighted){var local=sample.Aggregate(new Point(),(p,s)=>p+mesh.Local[s.Vertex]*s.Amount);vertices.AddRange([local.X,local.Y]);influences.Add([]);continue;}
            var group=sample.SelectMany(s=>mesh.Weights[s.Vertex].Select(w=>(w,s.Amount))).GroupBy(p=>p.w.Bone);
            var result=new List<Influence>();
            if(sample.Length==1&&sample[0].Amount==1)result.AddRange(mesh.Weights[sample[0].Vertex]);
            else foreach(var bone in group){var weight=bone.Sum(p=>p.w.Weight*p.Amount);if(weight==0)continue;var local=bone.Aggregate(new Point(),(p,v)=>p+v.w.Local*(v.w.Weight*v.Amount/weight));local.Validate();result.Add(new(bone.Key,local,weight));}
            influences.Add(result.ToArray());vertices.Add(result.Count);foreach(var w in result)vertices.AddRange([w.Bone,w.Local.X,w.Local.Y,w.Weight]);
        }
        long expanded=vertices.Count+order.Count*2L+triangles.Count;
        if(expanded>1_000_000)throw new InvalidDataException("Mesh structure numeric budget");
        raw["uvs"]=Values(order.SelectMany(i=>new[]{uv[i].X,uv[i].Y}));raw["vertices"]=Values(vertices);raw["triangles"]=Values(triangles.Select(i=>(double)map[i]));raw["hull"]=hull;
        oldToNew=new Dictionary<int,int>();for(var i=0;i<order.Count;i++){var s=samples[order[i]];if(order[i]!=added&&s.Length==1&&s[0].Amount==1)oldToNew[s[0].Vertex]=i;}
        if(raw["edges"] is not null)
        {
            var manual=Scene.Values(raw["edges"]);if(manual.Length%2!=0)throw new InvalidDataException("Mesh edges");var remapped=new List<double>();
            for(var i=0;i<manual.Length;i+=2)
            {
                if(manual[i]%2!=0||manual[i+1]%2!=0||manual[i]<0||manual[i+1]<0||manual[i]>=mesh.UV.Length*2||manual[i+1]>=mesh.UV.Length*2)throw new InvalidDataException("Mesh edge vertex offsets");
                var a=(int)manual[i]/2;var b=(int)manual[i+1]/2;if(!oldToNew.TryGetValue(a,out var x)||!oldToNew.TryGetValue(b,out var y))continue;
                if(split==Edge(a,b)){var n=map[added];remapped.AddRange([x*2,n*2,n*2,y*2]);}else remapped.AddRange([x*2,y*2]);
            }
            raw["edges"]=Values(remapped);
        }
        var starts=new int[mesh.UV.Length];for(var i=1;i<starts.Length;i++)starts[i]=starts[i-1]+(weighted?mesh.Weights[i-1].Length*2:2);
        var affected=scene.Meshes.Values.Where(m=>ReferenceEquals(m.Data,mesh.Data)).ToArray();
        foreach(var animation in Scene.Object(root["animations"])??[])foreach(var attachment in affected)
        {
            var modern=scene.Version.StartsWith("4.1.");var node=animation.Value?[modern?"attachments":"deform"]?[attachment.Skin]?[attachment.Slot]?[attachment.Name];
            var frames=Scene.Array(modern?node?["deform"]:node);if(frames is null)continue;
            Playback.ValidateDeformKeys(mesh,frames);
            foreach(var frame in frames)
            {
                expanded+=weighted?influences.Sum(w=>(long)w.Length)*2:order.Count*2L;
                if(expanded>1_000_000)throw new InvalidDataException("Mesh Deform expansion budget");
                var old=new double[mesh.DeformLength];var values=Scene.Values(frame!["vertices"]);var offset=frame["offset"] is null?0:Scene.Integer(frame["offset"]);values.CopyTo(old,offset);var output=new List<double>();
                for(var index=0;index<order.Count;index++)
                {
                    var sample=samples[order[index]];
                    if(!weighted){var value=sample.Aggregate(new Point(),(p,s)=>p+new Point(old[starts[s.Vertex]],old[starts[s.Vertex]+1])*s.Amount);output.AddRange([value.X,value.Y]);}
                    else if(sample.Length==1&&sample[0].Amount==1)
                    {
                        var at=starts[sample[0].Vertex];output.AddRange(old.AsSpan(at,mesh.Weights[sample[0].Vertex].Length*2).ToArray());
                    }
                    else foreach(var influence in influences[index])
                    {
                        var value=new Point();foreach(var s in sample)for(var j=0;j<mesh.Weights[s.Vertex].Length;j++){var w=mesh.Weights[s.Vertex][j];if(w.Bone==influence.Bone)value+=new Point(old[starts[s.Vertex]+j*2],old[starts[s.Vertex]+j*2+1])*(w.Weight*s.Amount/influence.Weight);}
                        output.AddRange([value.X,value.Y]);
                    }
                }
                frame.AsObject().Remove("offset");frame["vertices"]=Values(output);
            }
        }
        return added<0?-1:map[added];
    }
}

