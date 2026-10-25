namespace IndependentDev;

internal sealed record InspectorNode(int Parent,string Label,string Details,string? MeshKey=null,int? BoneIndex=null,string? SlotName=null,string? AttachmentKey=null)
{
    public string ViewKey=>BoneIndex is{} bone?"bone:"+bone:SlotName is{} slot?"slot:"+slot:"attachment:"+(AttachmentKey??MeshKey);
}
internal static class Inspector
{
    public static string SlotDetails(Scene scene,Slot slot)=>$"{slot.Name}\n본 {scene.Bones[slot.Bone].Name}\n설정 어태치먼트 {slot.Attachment??"없음"}\n블렌드 {slot.Blend}";
    public static InspectorNode[] Build(Scene scene,string skin)
    {
        var nodes=new List<InspectorNode>();
        foreach(var bone in scene.Bones)
            nodes.Add(new(bone.Parent,"본 · "+bone.Name,$"{bone.Name}\n부모 {(bone.Parent<0?"없음":scene.Bones[bone.Parent].Name)}\n위치 {bone.X}, {bone.Y}\n회전 {bone.Rotation}\n배율 {bone.ScaleX}, {bone.ScaleY}\n길이 {bone.Length}\n상속 {bone.Transform}",BoneIndex:nodes.Count));
        var attachments=scene.Meshes.Values.Where(m=>m.Skin==skin||m.Skin=="default").ToLookup(m=>m.Slot);
        foreach(var slot in scene.Slots)
        {
            var parent=nodes.Count;nodes.Add(new(slot.Bone,"슬롯 · "+slot.Name,SlotDetails(scene,slot),SlotName:slot.Name));
            foreach(var group in attachments[slot.Name].GroupBy(m=>m.Name))
            {
                var mesh=group.FirstOrDefault(m=>m.Skin==skin)??group.First();
                var selectable=Scene.Text(mesh.Data["type"],"region")=="region"||Scene.Text(mesh.Data["type"])=="mesh"&&mesh.UV.Length>0;
                nodes.Add(new(parent,"어태치먼트 · "+mesh.Name,$"{mesh.Name}\n슬롯 {mesh.Slot}\n스킨 {mesh.Skin}\n기하 종류 {Scene.Text(mesh.Data["type"],"region")}\n정점 {mesh.UV.Length}\n삼각형 {mesh.Triangles.Length/3}\n가중치 {(mesh.Weights.Length>0?"있음":"없음")}",selectable?Scene.Key(mesh.Skin,mesh.Slot,mesh.Name):null,AttachmentKey:Scene.Key(mesh.Skin,mesh.Slot,mesh.Name)));
            }
        }
        return nodes.ToArray();
    }
}
