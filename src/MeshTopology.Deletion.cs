namespace IndependentDev;

internal static partial class MeshTopology
{
    static List<int> RetriangulateDeletion(Mesh mesh,IReadOnlySet<int> selected,int[] hull)
    {
        var uv=mesh.UV;var original=mesh.Triangles.Chunk(3).ToArray();
        var sign=Point.Orientation(uv[mesh.Triangles[0]],uv[mesh.Triangles[1]],uv[mesh.Triangles[2]]);
        var work=20_000_000;var outer=Boundary(mesh.Triangles).ToHashSet();
        // Removing interior stars locally preserves surviving edges, including touching cavities and islands.
        foreach(var vertex in selected.Except(outer).Order())
        {
            var star=new List<int>();var rest=new List<int[]>();
            foreach(var triangle in original){if(--work<0)throw new InvalidDataException("Mesh deletion work limit");if(triangle.Contains(vertex))star.AddRange(triangle);else rest.Add(triangle);}
            var ring=Boundary(star);foreach(var t in Polygons.Triangulate(ring.Select(i=>uv[i]).ToArray()).Chunk(3))
            {var a=ring[t[0]];var b=ring[t[1]];var c=ring[t[2]];if(Point.Orientation(uv[a],uv[b],uv[c])!=sign)(b,c)=(c,b);rest.Add([a,b,c]);}
            original=rest.ToArray();
        }
        var kept=original.Where(t=>!t.Any(selected.Contains)).SelectMany(t=>t).ToList();
        var rim=new HashSet<(int A,int B)>();
        void Toggle(int a,int b){if(!rim.Remove((b,a)))rim.Add((a,b));}
        foreach(var t in kept.Chunk(3))for(var i=0;i<3;i++){var a=t[i];var b=t[(i+1)%3];if(sign<0)(a,b)=(b,a);Toggle(b,a);}
        var boundarySign=Polygons.Winding(hull.Select(i=>uv[i]).ToArray());
        for(var i=0;i<hull.Length;i++){var a=hull[i];var b=hull[(i+1)%hull.Length];if(boundarySign<0)(a,b)=(b,a);Toggle(a,b);}
        var outgoing=rim.GroupBy(e=>e.A).ToDictionary(g=>g.Key,g=>g.Select(e=>e.B).ToArray());var loops=new List<int[]>();
        while(rim.Count>0)
        {
            var edge=rim.Min();var start=edge.A;var ring=new List<int>();
            do
            {
                if(!rim.Remove(edge))throw new InvalidDataException("Deletion boundary cycle");ring.Add(edge.A);if(edge.B==start)break;
                if(!outgoing.TryGetValue(edge.B,out var neighbors))throw new InvalidDataException("Deletion boundary open");
                var reverse=uv[edge.A]-uv[edge.B];var angle=Math.Atan2(reverse.Y,reverse.X);
                var next=neighbors.Where(i=>rim.Contains((edge.B,i))).OrderBy(i=>(angle-Math.Atan2(uv[i].Y-uv[edge.B].Y,uv[i].X-uv[edge.B].X)+Math.PI*2)%(Math.PI*2)).DefaultIfEmpty(-1).First();
                if(next<0)throw new InvalidDataException("Deletion boundary branch");edge=(edge.B,next);
            }while(true);
            loops.Add(ring.ToArray());
        }
        void Fill(List<int> target,int[] ring)
        {
            foreach(var t in Polygons.Triangulate(ring.Select(i=>uv[i]).ToArray()).Chunk(3))
            {var a=ring[t[0]];var b=ring[t[1]];var c=ring[t[2]];if(Point.Orientation(uv[a],uv[b],uv[c])!=sign)(b,c)=(c,b);target.AddRange([a,b,c]);}
        }
        var preserved=kept.Chunk(3).SelectMany(t=>Enumerable.Range(0,3).Select(i=>Edge(t[i],t[(i+1)%3]))).ToHashSet();
        var nested=loops.Any(r=>r.Distinct().Count()!=r.Length||Polygons.Winding(r.Select(i=>uv[i]).ToArray())<0);
        // Enclosed or touching cavities use the full polygon, then restore all surviving triangle edges.
        if(nested){kept.Clear();Fill(kept,hull);}else foreach(var ring in loops)Fill(kept,ring);
        kept=InsertPoints(uv,kept,Enumerable.Range(0,uv.Length).Where(i=>!selected.Contains(i)),sign,ref work);
        var constraints=nested?preserved:new HashSet<(int,int)>();var manual=Scene.Values(mesh.Data["edges"]);
        if(manual.Length%2!=0)throw new InvalidDataException("Mesh edges");
        for(var i=0;i<manual.Length;i+=2)
        {
            if(manual[i]%2!=0||manual[i+1]%2!=0||manual[i]<0||manual[i+1]<0||manual[i]>=uv.Length*2||manual[i+1]>=uv.Length*2)throw new InvalidDataException("Mesh edge vertex offsets");
            var a=(int)manual[i]/2;var b=(int)manual[i+1]/2;if(!selected.Contains(a)&&!selected.Contains(b))constraints.Add(Edge(a,b));
        }
        kept=Constrain(uv,kept,constraints,sign,ref work);
        if(!kept.ToHashSet().SetEquals(Enumerable.Range(0,uv.Length).Where(i=>!selected.Contains(i))))throw new InvalidOperationException("남은 정점이 삼각형에서 누락됐습니다.");
        return kept;
    }
    static bool Crosses(Point a,Point b,Point c,Point d)=>Point.Orientation(a,b,c)*Point.Orientation(a,b,d)<0&&Point.Orientation(c,d,a)*Point.Orientation(c,d,b)<0;
}




