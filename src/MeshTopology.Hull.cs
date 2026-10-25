using System.Text.Json.Nodes;
namespace IndependentDev;

internal static partial class MeshTopology
{
    internal static bool InsideHull(Point point,Point[] positions,int[] triangles)=>Inside(point,Boundary(triangles).Select(i=>positions[i]).ToArray());
    static List<int> InsertPoints(Point[] uv,List<int> triangles,IEnumerable<int> vertices,int sign,ref int work)
    {
        var used=triangles.ToHashSet();
        foreach(var vertex in vertices)
        {
            if(used.Contains(vertex))continue;var found=false;
            for(var at=0;at<triangles.Count;at+=3)
            {
                if(--work<0)throw new InvalidDataException("Mesh triangulation work limit");
                var a=triangles[at];var b=triangles[at+1];var c=triangles[at+2];
                var x=sign*Point.Orientation(uv[a],uv[b],uv[vertex]);var y=sign*Point.Orientation(uv[b],uv[c],uv[vertex]);var z=sign*Point.Orientation(uv[c],uv[a],uv[vertex]);if(x<0||y<0||z<0)continue;
                if((x==0?1:0)+(y==0?1:0)+(z==0?1:0)>1)throw new InvalidOperationException("다른 정점과 겹치는 정점이 있습니다.");
                if(x!=0&&y!=0&&z!=0){triangles.RemoveRange(at,3);triangles.AddRange([a,b,vertex,b,c,vertex,c,a,vertex]);}
                else
                {
                    var edge=x==0?Edge(a,b):y==0?Edge(b,c):Edge(c,a);var result=new List<int>();
                    foreach(var t in triangles.Chunk(3))
                    {
                        var side=-1;for(var i=0;i<3;i++)if(Edge(t[i],t[(i+1)%3])==edge)side=i;
                        if(side<0)result.AddRange(t);else result.AddRange([t[side],vertex,t[(side+2)%3],vertex,t[(side+1)%3],t[(side+2)%3]]);
                    }
                    triangles=result;
                }
                used.Add(vertex);found=true;break;
            }
            if(!found)throw new InvalidOperationException("남은 정점을 새 외곽선 안에 연결할 수 없습니다.");
        }
        return triangles;
    }
    static int[] CheckedHull(Mesh mesh)
    {
        var hull=Boundary(mesh.Triangles);
        if(mesh.Data["hull"] is{} declared)
        {
            var count=Scene.Integer(declared);
            if(count!=hull.Length||!hull.ToHashSet().SetEquals(Enumerable.Range(0,count))||!Enumerable.Range(0,count).All(i=>HasEdge(hull,i,(i+1)%count)))throw new InvalidOperationException("저장된 외곽선과 삼각형 경계가 일치하지 않습니다.");
        }
        return hull;
    }
    static List<int> RebuildHull(Mesh mesh,Point[] uv,Point[] positions,int[] hull,HashSet<(int,int)> edited)
    {
        if(hull.Length<3||hull.Distinct().Count()!=hull.Length)throw new InvalidOperationException("외곽선에는 서로 다른 정점이 최소 세 개 필요합니다.");
        var polygon=hull.Select(i=>uv[i]).ToArray();Polygons.Triangulate(polygon);var worldHull=hull.Select(i=>positions[i]).ToArray();Polygons.Triangulate(worldHull);var boundary=hull.ToHashSet();var work=20_000_000;
        if((long)uv.Length*hull.Length>work)throw new InvalidDataException("Mesh triangulation work limit");
        foreach(var i in Enumerable.Range(0,uv.Length).Except(boundary))if(!Inside(uv[i],polygon)||!Inside(positions[i],worldHull))throw new InvalidOperationException($"정점 {i}이 새 외곽선 밖에 있거나 테두리와 겹칩니다.");
        var oldHull=CheckedHull(mesh);var triangles=mesh.Triangles.ToList();var sign=Point.Orientation(positions[triangles[0]],positions[triangles[1]],positions[triangles[2]]);
        void Fill(int[] ring)
        {
            foreach(var t in Polygons.Triangulate(ring.Select(i=>positions[i]).ToArray()).Chunk(3))
            {var a=ring[t[0]];var b=ring[t[1]];var c=ring[t[2]];if(Point.Orientation(positions[a],positions[b],positions[c])!=sign)(b,c)=(c,b);triangles.AddRange([a,b,c]);}
        }
        if(uv.Length>mesh.UV.Length)
        {
            var vertex=mesh.UV.Length;var previous=oldHull.Select(i=>positions[i]).ToArray();
            if(Inside(positions[vertex],previous)||Enumerable.Range(0,previous.Length).Any(i=>OnSegment(previous[i],previous[(i+1)%previous.Length],positions[vertex])))
                triangles=InsertPoints(positions,triangles,[vertex],sign,ref work);
            else{var at=Array.IndexOf(hull,vertex);Fill([hull[(at+hull.Length-1)%hull.Length],vertex,hull[(at+1)%hull.Length]]);}
        }
        else if(hull.All(oldHull.Contains))
        {
            // Turning an inward corner into an interior vertex adds only the gap beside the old outline.
            for(var i=0;i<oldHull.Length;i++)if(boundary.Contains(oldHull[i]))
            {
                var ring=new List<int>{oldHull[i]};var j=(i+1)%oldHull.Length;
                while(!boundary.Contains(oldHull[j])){ring.Add(oldHull[j]);j=(j+1)%oldHull.Length;}
                ring.Add(oldHull[j]);if(ring.Count>2)Fill(ring.ToArray());
            }
        }
        var constraints=new HashSet<(int,int)>();
        var manual=Scene.Values(mesh.Data["edges"]);if(manual.Length%2!=0)throw new InvalidDataException("Mesh edges");
        for(var i=0;i<manual.Length;i+=2)
        {
            if(manual[i]%2!=0||manual[i+1]%2!=0||manual[i]<0||manual[i+1]<0||manual[i]>=mesh.UV.Length*2||manual[i+1]>=mesh.UV.Length*2)throw new InvalidDataException("Mesh edge vertex offsets");
            var edge=Edge((int)manual[i]/2,(int)manual[i+1]/2);if(edited.Contains(edge))continue;
            constraints.Add(edge);
        }
        // Automatic diagonals may flip; only manual connections and the explicit outline are mandatory.
        for(var i=0;i<hull.Length;i++)constraints.Add(Edge(hull[i],hull[(i+1)%hull.Length]));
        triangles=Constrain(positions,triangles,constraints,sign,ref work);
        var cells=triangles.Chunk(3).ToArray();var users=new Dictionary<(int,int),List<int>>();
        for(var i=0;i<cells.Length;i++)for(var j=0;j<3;j++){var edge=Edge(cells[i][j],cells[i][(j+1)%3]);if(!users.TryGetValue(edge,out var list))users[edge]=list=[];list.Add(i);}
        var border=Enumerable.Range(0,hull.Length).Select(i=>Edge(hull[i],hull[(i+1)%hull.Length])).ToHashSet();var outside=new HashSet<int>();var queue=new Queue<int>();var winding=Polygons.Winding(worldHull);
        for(var i=0;i<hull.Length;i++)
        {
            var a=hull[i];var b=hull[(i+1)%hull.Length];foreach(var cell in users[Edge(a,b)])
                if(Point.Orientation(positions[a],positions[b],positions[cells[cell].Single(v=>v!=a&&v!=b)])!=winding&&outside.Add(cell))queue.Enqueue(cell);
        }
        while(queue.TryDequeue(out var cell))for(var i=0;i<3;i++)
        {
            if(--work<0)throw new InvalidDataException("Mesh triangulation work limit");var edge=Edge(cells[cell][i],cells[cell][(i+1)%3]);
            if(!border.Contains(edge))foreach(var neighbor in users[edge])if(outside.Add(neighbor))queue.Enqueue(neighbor);
        }
        triangles=cells.Where((_,i)=>!outside.Contains(i)).SelectMany(t=>t).ToList();
        if(!constraints.IsSubsetOf(triangles.Chunk(3).SelectMany(t=>Enumerable.Range(0,3).Select(i=>Edge(t[i],t[(i+1)%3]))).ToHashSet()))throw new InvalidOperationException("새 외곽선과 사용자 지정 연결선을 함께 보존할 수 없습니다.");
        var actual=Boundary(triangles);if(actual.Length!=hull.Length||!Enumerable.Range(0,actual.Length).All(i=>border.Contains(Edge(actual[i],actual[(i+1)%actual.Length]))))throw new InvalidDataException("Mesh outline mismatch");
        triangles=MatchCoordinates(uv,positions,triangles,constraints,Point.Orientation(mesh.UV[mesh.Triangles[0]],mesh.UV[mesh.Triangles[1]],mesh.UV[mesh.Triangles[2]]),sign,ref work);
        ValidateTriangles(uv,triangles,hull);
        if(triangles.Distinct().Count()!=uv.Length)throw new InvalidDataException("Mesh vertex omitted");return triangles;
    }
    internal static int InsertHull(Scene scene,JsonObject root,string key,int a,int b,int vertex,Point world,string skin)
    {
        var mesh=Source(scene,key);var hull=CheckedHull(mesh);if(!HasEdge(hull,a,b))throw new InvalidOperationException("외곽 변을 선택하세요.");
        var added=vertex<0;var count=mesh.UV.Length;if(added&&count>=100_000)throw new InvalidDataException("Vertex count limit");
        if(!added&&(vertex>=count||hull.Contains(vertex)))throw new InvalidOperationException("외곽에 포함할 내부 정점을 하나 선택하세요.");
        var positions=scene.Vertices(mesh,scene.Pose(skin:skin)).Select(v=>v.Position).ToArray();var samples=Enumerable.Range(0,count).Select(i=>new[]{new Sample(i,1)}).ToList();var uv=mesh.UV.ToArray();
        if(added)
        {
            world.Validate();if(Math.Abs(world.X)>100_000||Math.Abs(world.Y)>100_000)throw new InvalidDataException("Edit target range");
            var d=positions[b]-positions[a];var size=d.X*d.X+d.Y*d.Y;if(!(size>0)||!double.IsFinite(size))throw new InvalidDataException("Hull edge numeric range");
            var offset=world-positions[a];var amount=Math.Clamp((offset.X*d.X+offset.Y*d.Y)/size,0,1);
            var t=mesh.Triangles.Chunk(3).Single(t=>t.Contains(a)&&t.Contains(b));var c=t.Single(i=>i!=a&&i!=b);var ac=positions[c]-positions[a];var cross=Point.Cross(d,ac);if(cross==0)throw new InvalidOperationException("납작한 외곽 변에는 정점을 추가할 수 없습니다.");
            var wb=Point.Cross(offset,ac)/cross;var wc=Point.Cross(d,offset)/cross;var image=mesh.UV[a]*(1-wb-wc)+mesh.UV[b]*wb+mesh.UV[c]*wc;image.Validate();
            if(image.X<0||image.X>1||image.Y<0||image.Y>1)throw new InvalidOperationException("파츠 이미지 범위 안에 정점을 추가하세요.");
            vertex=count;uv=[..uv,image];samples.Add(amount==0?[new(a,1)]:amount==1?[new(b,1)]:[new(a,1-amount),new(b,amount)]);
        }
        var next=new List<int>();for(var i=0;i<hull.Length;i++){next.Add(hull[i]);if(Edge(hull[i],hull[(i+1)%hull.Length])==Edge(a,b))next.Add(vertex);}
        var triangles=RebuildHull(mesh,uv,added?[..positions,world]:positions,next.ToArray(),[Edge(a,b)]);
        var result=Apply(scene,root,mesh,samples,triangles,added?Edge(a,b):null,added?vertex:-1,out var mapping,added?uv[vertex]:null);
        if(!added)
        {
            // A replaced boundary edge follows the explicit new boundary, even when reusing a vertex.
            var raw=Definition(root,mesh);var edges=Scene.Values(raw["edges"]);if(edges.Length>0){var output=new List<double>();for(var i=0;i<edges.Length;i+=2){var x=(int)edges[i]/2;var y=(int)edges[i+1]/2;if(Edge(x,y)==Edge(mapping[a],mapping[b]))output.AddRange([mapping[a]*2,mapping[vertex]*2,mapping[vertex]*2,mapping[b]*2]);else output.AddRange([edges[i],edges[i+1]]);}raw["edges"]=Values(output);}
            result=mapping[vertex];
        }
        else
        {
            var candidate=new Editor(root.ToJsonString(),recordHistory:false);candidate.MoveVertex(key,result,world,candidate.Document.Pose(skin:skin));Definition(root,mesh)["vertices"]=candidate.Document.Meshes[key].Data["vertices"]!.DeepClone();
        }
        ValidateHullWorld(root,key,skin);return result;
    }
    internal static void ExcludeHull(Scene scene,JsonObject root,string key,IReadOnlySet<int> selected,string skin)
    {
        var mesh=Source(scene,key);var hull=CheckedHull(mesh);if(selected.Count==0||!selected.IsSubsetOf(hull.ToHashSet()))throw new InvalidOperationException("외곽에서 제외할 외곽 정점만 선택하세요.");
        var next=hull.Where(i=>!selected.Contains(i)).ToArray();var edited=Enumerable.Range(0,hull.Length).Where(i=>selected.Contains(hull[i])||selected.Contains(hull[(i+1)%hull.Length])).Select(i=>Edge(hull[i],hull[(i+1)%hull.Length])).ToHashSet();
        var triangles=RebuildHull(mesh,mesh.UV,scene.Vertices(mesh,scene.Pose(skin:skin)).Select(v=>v.Position).ToArray(),next,edited);Apply(scene,root,mesh,Enumerable.Range(0,mesh.UV.Length).Select(i=>new[]{new Sample(i,1)}).ToList(),triangles,null,-1,out var mapping);
        var raw=Definition(root,mesh);if(raw["edges"] is not null){var previous=Scene.Values(mesh.Data["edges"]);var edges=new List<double>();for(var i=0;i<previous.Length;i+=2){var a=(int)previous[i]/2;var b=(int)previous[i+1]/2;if(!edited.Contains(Edge(a,b)))edges.AddRange([mapping[a]*2,mapping[b]*2]);}raw["edges"]=Values(edges);}
        ValidateHullWorld(root,key,skin);
    }
    static void ValidateHullWorld(JsonObject root,string key,string skin)
        =>ValidateHull(Scene.Parse(root.ToJsonString()),key,skin);
    internal static void ValidateHull(Scene scene,string key,string skin)
    {
        var mesh=scene.Meshes[key];var hull=Boundary(mesh.Triangles);ValidateTriangles(mesh.UV,mesh.Triangles,hull);ValidateTriangles(scene.Vertices(mesh,scene.Pose(skin:skin)).Select(v=>v.Position).ToArray(),mesh.Triangles,hull);
    }
}
