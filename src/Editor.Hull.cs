namespace IndependentDev;

internal sealed partial class Editor
{
    public int InsertHullVertex(string key,int a,int b,Point world,Action<Scene>? validate=null,string skin="default")
    {
        if(binary is not null){var index=-1;Document=binary.EditMeshStructure(e=>index=e.InsertHullVertex(key,a,b,world,skin:skin),next=>{MeshTopology.ValidateHull(next,key,skin);validate?.Invoke(next);});return index;}
        RequireMeshStructure();var result=-1;Edit(root=>result=MeshTopology.InsertHull(Document,root,key,a,b,-1,world,skin),validate);return result;
    }
    public int IncludeHullVertex(string key,int a,int b,int vertex,Action<Scene>? validate=null,string skin="default")
    {
        if(vertex<0)throw new InvalidDataException("Vertex selection reference");
        if(binary is not null){var index=-1;Document=binary.EditMeshStructure(e=>index=e.IncludeHullVertex(key,a,b,vertex,skin:skin),next=>{MeshTopology.ValidateHull(next,key,skin);validate?.Invoke(next);});return index;}
        RequireMeshStructure();var result=-1;Edit(root=>result=MeshTopology.InsertHull(Document,root,key,a,b,vertex,default,skin),validate);return result;
    }
    public void ExcludeHullVertices(string key,IEnumerable<int> vertices,Action<Scene>? validate=null,string skin="default")
    {
        var selected=vertices.ToHashSet();
        if(binary is not null){Document=binary.EditMeshStructure(e=>e.ExcludeHullVertices(key,selected,skin:skin),next=>{MeshTopology.ValidateHull(next,key,skin);validate?.Invoke(next);});return;}
        RequireMeshStructure();Edit(root=>MeshTopology.ExcludeHull(Document,root,key,selected,skin),validate);
    }
}
