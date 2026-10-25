using System.Collections.Immutable;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
namespace IndependentDev;

internal sealed record ItemViewState(ImmutableHashSet<string> Hidden,ImmutableHashSet<string> Locked)
{
    public static readonly ItemViewState Empty=new(ImmutableHashSet<string>.Empty,ImmutableHashSet<string>.Empty);
    public bool Visible(Scene scene,string skin,string slot,string attachment)
        =>!Hidden.Contains("slot:"+slot)&&scene.TryMesh(skin,slot,attachment,out var mesh)&&!Hidden.Contains("attachment:"+Scene.Key(mesh.Skin,mesh.Slot,mesh.Name));
    public bool CanSelect(Scene scene,string key)
        =>scene.Meshes.TryGetValue(key,out var mesh)&&Visible(scene,mesh.Skin,mesh.Slot,mesh.Name)&&!Locked.Contains("slot:"+mesh.Slot)&&!Locked.Contains("attachment:"+key);
}

internal sealed partial class EditorWindow
{
    ItemViewState itemView=ItemViewState.Empty;
    bool updatingItemView;
    void AddTreeToggles(FrameworkElementFactory header)
    {
        foreach(var (property,title,help) in new[]{(nameof(InspectorItem.Selectable),"선택","모든 스킨의 하위 항목에 적용합니다. 켤 때 필요한 부모만 함께 켭니다. 트리에서는 계속 선택할 수 있습니다."),(nameof(InspectorItem.Shown),"표시","모든 스킨의 하위 항목을 함께 표시하거나 숨깁니다. 켤 때 필요한 부모만 함께 켭니다.")})
        {
            var toggle=new FrameworkElementFactory(typeof(CheckBox));toggle.SetValue(ContentControl.ContentProperty,title);toggle.SetValue(FrameworkElement.MinWidthProperty,0d);toggle.SetValue(FrameworkElement.MinHeightProperty,0d);toggle.SetValue(FrameworkElement.WidthProperty,52d);toggle.SetValue(FrameworkElement.HeightProperty,24d);toggle.SetValue(Control.PaddingProperty,new Thickness(0));toggle.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,6,0));toggle.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);toggle.SetValue(FrameworkElement.ToolTipProperty,help);
            toggle.SetBinding(CheckBox.IsCheckedProperty,new Binding(property){Mode=BindingMode.TwoWay});toggle.SetBinding(AutomationProperties.NameProperty,new Binding(nameof(InspectorItem.Label)){StringFormat="{0} · "+title});header.AppendChild(toggle);
        }
    }
    void ConfigureTreeItem(InspectorItem item)
    {
        var key=item.Node.ViewKey;item.Shown=!itemView.Hidden.Contains(key);item.Selectable=!itemView.Locked.Contains(key);
        item.PropertyChanged+=(_,e)=>
        {
            if(updatingItemView||e.PropertyName is not(nameof(InspectorItem.Shown) or nameof(InspectorItem.Selectable)))return;
            Guard(()=>
            {
                session.FinishDrag(false);surface.CancelVertexSelection();surface.ReleaseMouseCapture();insertVertex.IsChecked=insertHullVertex.IsChecked=false;
                var selection=e.PropertyName==nameof(InspectorItem.Selectable);var enabled=selection?item.Selectable:item.Shown;
                var disabled=(selection?itemView.Locked:itemView.Hidden).ToBuilder();var pending=new Stack<InspectorItem>();pending.Push(item);var slots=new HashSet<string>(StringComparer.Ordinal);
                updatingItemView=true;
                try
                {
                    if(enabled)for(var parent=item.Parent;parent is not null;parent=parent.Parent)
                    {
                        if(selection)parent.Selectable=true;else parent.Shown=true;
                        disabled.Remove(parent.Node.ViewKey);
                    }
                    while(pending.TryPop(out var current))
                    {
                        if(selection)current.Selectable=enabled;else current.Shown=enabled;
                        if(enabled)disabled.Remove(current.Node.ViewKey);else disabled.Add(current.Node.ViewKey);
                        if(current.Node.SlotName is{} slot)slots.Add(slot);
                        foreach(var child in current.Children)pending.Push(child);
                    }
                    foreach(var mesh in session.Document.Meshes.Values)
                    {
                        if(!slots.Contains(mesh.Slot))continue;var attachmentKey="attachment:"+Scene.Key(mesh.Skin,mesh.Slot,mesh.Name);
                        if(enabled)disabled.Remove(attachmentKey);else disabled.Add(attachmentKey);
                    }
                    itemView=selection?itemView with{Locked=disabled.ToImmutable()}:itemView with{Hidden=disabled.ToImmutable()};
                }
                finally{updatingItemView=false;}
            });
        };
    }
    VertexHandle[] SelectableHandles()=>EditorHandles.Visible(session,atlas).Where(h=>itemView.CanSelect(session.Document,h.Key)).ToArray();
    string? SelectablePartAt(Point point)=>EditorHandles.PartAt(session,atlas,point,key=>itemView.CanSelect(session.Document,key));
    bool SelectBoneAt(Point point)
    {
        if(showBones.IsChecked!=true)return false;
        var scene=session.Document;var pose=scene.Pose(session.Animation,session.Time,session.Skin);var active=scene.ActiveBones(session.Skin);
        for(var i=scene.Bones.Length-1;i>=0;i--)
        {
            if(!active[i]||itemView.Hidden.Contains("bone:"+i)||itemView.Locked.Contains("bone:"+i))continue;
            var transform=session.Camera.Matrix*pose[i];var a=transform.Apply(new());var b=transform.Apply(new(scene.Bones[i].Length,0));var delta=b-a;var length=delta.X*delta.X+delta.Y*delta.Y;
            var t=length==0?0:Math.Clamp(((point.X-a.X)*delta.X+(point.Y-a.Y)*delta.Y)/length,0,1);if((point-(a+delta*t)).Length>6)continue;
            var items=new Stack<InspectorItem>(hierarchy.Items.Cast<InspectorItem>().Where(item=>item.Parent is null));while(items.TryPop(out var item)){if(item.Node.BoneIndex==i){session.Select(null);RevealInspector(item);return true;}foreach(var child in item.Children)items.Push(child);}return false;
        }
        return false;
    }
}
