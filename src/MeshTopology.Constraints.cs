namespace IndependentDev;

internal static partial class MeshTopology
{
    static List<int> Constrain(Point[] uv,List<int> triangles,HashSet<(int,int)> constraints,int sign,ref int work)
    {
        var cells=triangles.Chunk(3).ToArray();var edges=new Dictionary<(int,int),HashSet<int>>();
        void Attach(int index){var t=cells[index];for(var i=0;i<3;i++){var edge=Edge(t[i],t[(i+1)%3]);if(!edges.TryGetValue(edge,out var users))edges[edge]=users=[];users.Add(index);if(users.Count>2)throw new InvalidDataException("Nonmanifold mesh edge");}}
        void Detach(int index){var t=cells[index];for(var i=0;i<3;i++){var edge=Edge(t[i],t[(i+1)%3]);var users=edges[edge];users.Remove(index);if(users.Count==0)edges.Remove(edge);}}
        for(var i=0;i<cells.Length;i++)Attach(i);
        var locked=constraints.Where(edges.ContainsKey).ToHashSet();
        foreach(var target in constraints.Order())
        {
            if(edges.ContainsKey(target)){locked.Add(target);continue;}
            var queue=new Queue<(int,int)>();
            foreach(var edge in edges.Keys){if(--work<0)throw new InvalidDataException("Mesh triangulation work limit");if(Crosses(uv[target.Item1],uv[target.Item2],uv[edge.Item1],uv[edge.Item2]))queue.Enqueue(edge);}
            while(!edges.ContainsKey(target)&&queue.TryDequeue(out var edge))
            {
                if(--work<0)throw new InvalidDataException("Mesh triangulation work limit");
                if(!edges.TryGetValue(edge,out var users))continue;
                if(locked.Contains(edge)||users.Count!=2)throw new InvalidOperationException("사용자 지정 연결선을 교차 없이 보존할 수 없습니다.");
                var pair=users.ToArray();var a=edge.Item1;var b=edge.Item2;var c=cells[pair[0]].Single(v=>v!=a&&v!=b);var d=cells[pair[1]].Single(v=>v!=a&&v!=b);
                if(!Crosses(uv[a],uv[b],uv[c],uv[d])){queue.Enqueue(edge);continue;}
                int[] Cell(int x,int y,int z)=>Point.Orientation(uv[x],uv[y],uv[z])==sign?[x,y,z]:[x,z,y];
                Detach(pair[0]);Detach(pair[1]);cells[pair[0]]=Cell(c,d,a);cells[pair[1]]=Cell(d,c,b);Attach(pair[0]);Attach(pair[1]);
                if(Crosses(uv[target.Item1],uv[target.Item2],uv[c],uv[d]))queue.Enqueue(Edge(c,d));
            }
            if(!edges.ContainsKey(target))throw new InvalidOperationException("사용자 지정 연결선을 복원할 수 없습니다.");locked.Add(target);
        }
        return cells.SelectMany(t=>t).ToList();
    }
    static List<int> MatchCoordinates(Point[] uv,Point[] positions,List<int> triangles,HashSet<(int,int)> locked,int uvSign,int worldSign,ref int work)
    {
        var cells=triangles.Chunk(3).ToArray();
        bool Good(int[] t)=>Point.Orientation(uv[t[0]],uv[t[1]],uv[t[2]])==uvSign;
        var pending=new Queue<int>(Enumerable.Range(0,cells.Length).Where(i=>!Good(cells[i])));if(pending.Count==0)return triangles;
        var edges=new Dictionary<(int,int),HashSet<int>>();
        void Attach(int index){var t=cells[index];for(var i=0;i<3;i++){var edge=Edge(t[i],t[(i+1)%3]);if(!edges.TryGetValue(edge,out var users))edges[edge]=users=[];users.Add(index);if(users.Count>2)throw new InvalidDataException("Nonmanifold mesh edge");}}
        void Detach(int index){var t=cells[index];for(var i=0;i<3;i++){var edge=Edge(t[i],t[(i+1)%3]);var users=edges[edge];users.Remove(index);if(users.Count==0)edges.Remove(edge);}}
        int[] Cell(int a,int b,int c)=>Point.Orientation(positions[a],positions[b],positions[c])==worldSign?[a,b,c]:[a,c,b];
        for(var i=0;i<cells.Length;i++)Attach(i);
        void Search(ref int budget)
        {
            ValidateEdges(uv,locked,ref budget);
            var changes=new SortedSet<(int,int)>();var visited=new HashSet<string>{""};var bad=cells.Count(t=>!Good(t));
            void Replace((int A,int B,int[] First,int[] Second) move)
            {
                var oldEdge=cells[move.A].Intersect(cells[move.B]).ToArray();var newEdge=move.First.Intersect(move.Second).ToArray();
                foreach(var edge in new[]{Edge(oldEdge[0],oldEdge[1]),Edge(newEdge[0],newEdge[1])})if(!changes.Add(edge))changes.Remove(edge);
                bad-=(Good(cells[move.A])?0:1)+(Good(cells[move.B])?0:1);Detach(move.A);Detach(move.B);cells[move.A]=move.First;cells[move.B]=move.Second;Attach(move.A);Attach(move.B);bad+=(Good(move.First)?0:1)+(Good(move.Second)?0:1);
            }
            IEnumerator<(int A,int B,int[] First,int[] Second)> Moves(ref int remaining)
            {
                var moves=new List<(int A,int B,int[] First,int[] Second,int Delta,int Nearby)>();
                foreach(var (edge,users) in edges)
                {
                    if(--remaining<0)throw new InvalidDataException("Mesh triangulation work limit");if(locked.Contains(edge)||users.Count!=2)continue;
                    var pair=users.ToArray();var a=edge.Item1;var b=edge.Item2;var c=cells[pair[0]].Single(v=>v!=a&&v!=b);var d=cells[pair[1]].Single(v=>v!=a&&v!=b);
                    if(!Crosses(positions[a],positions[b],positions[c],positions[d]))continue;var first=Cell(c,d,a);var second=Cell(d,c,b);var previous=(Good(cells[pair[0]])?0:1)+(Good(cells[pair[1]])?0:1);
                    // Charge retained candidates as well as scans to bound search memory with the existing work budget.
                    remaining-=16;if(remaining<0)throw new InvalidDataException("Mesh triangulation work limit");moves.Add((pair[0],pair[1],first,second,(Good(first)?0:1)+(Good(second)?0:1)-previous,previous==0?1:0));
                }
                return moves.OrderBy(m=>m.Delta).ThenBy(m=>m.Nearby).Select(m=>(m.A,m.B,m.First,m.Second)).GetEnumerator();
            }
            var stack=new Stack<IEnumerator<(int A,int B,int[] First,int[] Second)>>();var undo=new Stack<(int A,int B,int[] First,int[] Second)>();stack.Push(Moves(ref budget));
            while(stack.Count>0)
            {
                if(--budget<0)throw new InvalidDataException("Mesh triangulation work limit");
                if(!stack.Peek().MoveNext()){stack.Pop().Dispose();if(undo.Count>0)Replace(undo.Pop());continue;}
                var move=stack.Peek().Current;var before=(move.A,move.B,cells[move.A],cells[move.B]);Replace(move);if(bad==0)return;
                budget-=changes.Count*16+1;if(budget<0)throw new InvalidDataException("Mesh triangulation work limit");var state=string.Join(';',changes.Select(e=>$"{e.Item1},{e.Item2}"));
                if(!visited.Add(state)){Replace(before);continue;}undo.Push(before);stack.Push(Moves(ref budget));
            }
            throw new InvalidOperationException("UV와 실제 좌표에서 함께 유효한 자동 연결선을 만들 수 없습니다.");
        }
        while(pending.Count>0)
        {
            var count=pending.Count;var changed=false;
            for(var item=0;item<count;item++)
            {
                var index=pending.Dequeue();if(Good(cells[index]))continue;var fixedCell=false;
                for(var i=0;i<3;i++)
                {
                    if(--work<0)throw new InvalidDataException("Mesh triangulation work limit");var a=cells[index][i];var b=cells[index][(i+1)%3];var edge=Edge(a,b);
                    if(locked.Contains(edge)||edges[edge].Count!=2)continue;var other=edges[edge].Single(j=>j!=index);var c=cells[index].Single(v=>v!=a&&v!=b);var d=cells[other].Single(v=>v!=a&&v!=b);
                    if(!Crosses(positions[a],positions[b],positions[c],positions[d]))continue;var first=Cell(c,d,a);var second=Cell(d,c,b);
                    // Repair only automatic diagonals, and only when both replacements are valid in both coordinates.
                    if(!Good(first)||!Good(second))continue;
                    Detach(index);Detach(other);cells[index]=first;cells[other]=second;Attach(index);Attach(other);fixedCell=changed=true;break;
                }
                if(!fixedCell)pending.Enqueue(index);
            }
            // A valid result can need neutral or temporarily worse UV flips; keep those candidates private until the whole mesh is valid.
            if(!changed&&pending.Count>0){Search(ref work);break;}
        }
        return cells.SelectMany(t=>t).ToList();
    }
}
