namespace IndependentDev;

internal static partial class MeshTopology
{
    internal static int[] Boundary(IEnumerable<int> triangles)
    {
        var counts=new Dictionary<(int,int),int>();
        foreach(var t in triangles.Chunk(3))for(var i=0;i<3;i++){var edge=Edge(t[i],t[(i+1)%3]);counts[edge]=counts.GetValueOrDefault(edge)+1;if(counts[edge]>2)throw new InvalidDataException("Nonmanifold mesh edge");}
        var neighbors=new Dictionary<int,List<int>>();
        foreach(var (edge,count) in counts)if(count==1)foreach(var (a,b) in new[]{edge,(edge.Item2,edge.Item1)}){if(!neighbors.TryGetValue(a,out var list))neighbors[a]=list=[];list.Add(b);}
        if(neighbors.Count<3||neighbors.Any(p=>p.Value.Count!=2))throw new NotSupportedException("닫힌 외곽선 하나를 가진 메시만 정점을 삭제할 수 있습니다.");
        var ring=new List<int>();var start=neighbors.Keys.Min();var current=start;var previous=-1;
        do{if(ring.Count>=neighbors.Count)throw new InvalidDataException("Mesh boundary cycle");ring.Add(current);var next=neighbors[current].First(v=>v!=previous);previous=current;current=next;}while(current!=start);
        if(ring.Count!=neighbors.Count)throw new NotSupportedException("구멍이나 분리된 영역이 있는 메시의 정점 삭제는 지원하지 않습니다.");
        return ring.ToArray();
    }
    static bool HasEdge(int[] ring,int a,int b)=>Enumerable.Range(0,ring.Length).Any(i=>Edge(ring[i],ring[(i+1)%ring.Length])==Edge(a,b));
    static bool OnSegment(Point a,Point b,Point p)=>Point.Orientation(a,b,p)==0&&p.X>=Math.Min(a.X,b.X)&&p.X<=Math.Max(a.X,b.X)&&p.Y>=Math.Min(a.Y,b.Y)&&p.Y<=Math.Max(a.Y,b.Y);
    static bool Inside(Point p,Point[] polygon)
    {
        var inside=false;
        for(var i=0;i<polygon.Length;i++)
        {
            var a=polygon[i];var b=polygon[(i+1)%polygon.Length];if(OnSegment(a,b,p))return false;
            if((a.Y>p.Y)!=(b.Y>p.Y)&&(Point.Orientation(a,b,p)>0)==(b.Y>a.Y))inside=!inside;
        }
        return inside;
    }
    internal static void ValidateTriangles(Point[] uv,IEnumerable<int> triangles,int[] hull)
    {
        var polygon=hull.Select(i=>uv[i]).ToArray();Polygons.Triangulate(polygon);
        var edges=new HashSet<(int,int)>();double area=0;int sign=0;
        foreach(var t in triangles.Chunk(3))
        {
            var cross=Point.Cross(uv[t[1]]-uv[t[0]],uv[t[2]]-uv[t[0]]);var orientation=Point.Orientation(uv[t[0]],uv[t[1]],uv[t[2]]);if(orientation==0||!double.IsFinite(cross)||sign!=0&&orientation!=sign)throw new InvalidOperationException("퇴화하거나 뒤집힌 삼각형 때문에 삭제할 수 없습니다.");
            sign=orientation;area+=Math.Abs(cross)/2;for(var i=0;i<3;i++)edges.Add(Edge(t[i],t[(i+1)%3]));
        }
        var work=20_000_000;ValidateEdges(uv,edges,ref work);
        var expected=Math.Abs(Polygons.SignedArea(polygon));
        if(!double.IsFinite(area)||Math.Abs(area-expected)>1e-12*expected)throw new InvalidOperationException("삼각형이 외곽선과 일치하지 않거나 겹쳐 삭제할 수 없습니다.");
    }
    static void ValidateEdges(Point[] uv,IEnumerable<(int,int)> edges,ref int work)
    {
        var lines=edges.Select(e=>(A:e.Item1,B:e.Item2,MinX:Math.Min(uv[e.Item1].X,uv[e.Item2].X),MaxX:Math.Max(uv[e.Item1].X,uv[e.Item2].X),MinY:Math.Min(uv[e.Item1].Y,uv[e.Item2].Y),MaxY:Math.Max(uv[e.Item1].Y,uv[e.Item2].Y))).OrderBy(e=>e.MinX).ThenBy(e=>e.MinY).ToArray();
        for(var i=0;i<lines.Length;i++)for(var j=i+1;j<lines.Length&&lines[j].MinX<=lines[i].MaxX;j++)
        {
            if(--work<0)throw new InvalidDataException("Mesh triangulation work limit");
            if(lines[j].MaxY<lines[i].MinY||lines[j].MinY>lines[i].MaxY)continue;
            var a=lines[i].A;var b=lines[i].B;var c=lines[j].A;var d=lines[j].B;var p=uv[a];var q=uv[b];var r=uv[c];var s=uv[d];
            if(Crosses(p,q,r,s)||a!=c&&a!=d&&OnSegment(r,s,p)||b!=c&&b!=d&&OnSegment(r,s,q)||c!=a&&c!=b&&OnSegment(p,q,r)||d!=a&&d!=b&&OnSegment(p,q,s))throw new InvalidOperationException("연결선이 교차하거나 다른 정점과 겹쳐 편집할 수 없습니다.");
        }
    }
}

