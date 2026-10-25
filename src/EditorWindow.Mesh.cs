using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
namespace IndependentDev;

internal sealed partial class EditorWindow
{
    readonly StackPanel meshTools=new(){Margin=new Thickness(12)};
    readonly StackPanel meshVertexTools=new(){Margin=new Thickness(12)};
    readonly Expander meshEdit=new(){Header="메시 편집",Margin=new Thickness(0,8,0,8),Visibility=Visibility.Collapsed};
    readonly Expander meshParts=new(){Header="파츠 관리",Margin=new Thickness(0,8,0,8),Visibility=Visibility.Collapsed};
    (Editor Editor,string Skin,string? Key,string? Slot,string? Animation)? meshToolContext;
    readonly TextBox meshName=new(){Width=160,HorizontalAlignment=HorizontalAlignment.Left};
    readonly ComboBox meshRegion=new(){MinWidth=160,HorizontalAlignment=HorizontalAlignment.Left};
    readonly CheckBox insertVertex=new(){Content="클릭하여 내부 정점 추가",Margin=new Thickness(0,6,0,6)};
    sealed record HullEdgeChoice(int A,int B){public override string ToString()=>$"정점 {A} — {B}";}
    readonly ComboBox hullEdge=new(){MinWidth=160,HorizontalAlignment=HorizontalAlignment.Left};
    readonly CheckBox insertHullVertex=new(){Content="외곽 정점 추가 (변 선택 후 클릭)",Margin=new Thickness(0,6,0,6)};
    readonly Button includeHullVertex=new(){Content="선택 내부 정점을 외곽에 포함",HorizontalAlignment=HorizontalAlignment.Left};
    readonly Button excludeHullVertices=new(){Content="선택 외곽 정점을 내부로 전환",HorizontalAlignment=HorizontalAlignment.Left};
    (Scene Scene,string? Key)? hullContext;
    readonly Button deleteMesh=new(){Content="메시 파츠 삭제",HorizontalAlignment=HorizontalAlignment.Left};
    readonly Button deleteVertex=new(){Content="선택 정점 삭제",HorizontalAlignment=HorizontalAlignment.Left};
    readonly TextBlock meshHelp=new(){TextWrapping=TextWrapping.Wrap,MaxWidth=300};
    Scene? structureScene;
    TextureAtlas? meshChoicesAtlas;
    void ConfigureMeshTools()
    {
        meshTools.Children.Add(new TextBlock{Text="새 메시 이름"});meshTools.Children.Add(meshName);
        meshTools.Children.Add(new TextBlock{Text="이미지 (Atlas 영역)"});meshTools.Children.Add(meshRegion);
        var add=new Button{Content="선택 슬롯에 메시 추가",HorizontalAlignment=HorizontalAlignment.Left};meshTools.Children.Add(add);
        add.Click+=(_,_)=>Guard(()=>{PausePlayback();session.AddMesh(meshName.Text,(AtlasRegion?)meshRegion.SelectedItem??throw new InvalidOperationException("Atlas 영역을 선택하세요."),ValidateMeshView);});
        meshRegion.DisplayMemberPath=nameof(AtlasRegion.Name);
        meshTools.Children.Add(deleteMesh);meshVertexTools.Children.Add(insertVertex);meshVertexTools.Children.Add(deleteVertex);
        meshVertexTools.Children.Add(new TextBlock{Text="편집할 외곽 변",Margin=new Thickness(0,10,0,0)});meshVertexTools.Children.Add(hullEdge);meshVertexTools.Children.Add(insertHullVertex);meshVertexTools.Children.Add(includeHullVertex);meshVertexTools.Children.Add(excludeHullVertices);
        insertHullVertex.Checked+=(_,_)=>insertVertex.IsChecked=false;insertVertex.Checked+=(_,_)=>insertHullVertex.IsChecked=false;
        includeHullVertex.Click+=(_,_)=>Guard(()=>{PausePlayback();var edge=SelectedHullEdge();session.IncludeHullVertex(edge.A,edge.B,ValidateMeshView);});
        excludeHullVertices.Click+=(_,_)=>Guard(()=>{PausePlayback();session.ExcludeHullVertices(ValidateMeshView);});
        AutomationProperties.SetName(hullEdge,"편집할 외곽 변");AutomationProperties.SetName(insertHullVertex,"외곽 정점 추가");
        hullEdge.SelectionChanged+=(_,_)=>{if(hullEdge.SelectedItem is HullEdgeChoice edge&&session.SelectedKey is{} key)Guard(()=>session.SelectVertices(key,[edge.A,edge.B]));};
        insertHullVertex.ToolTip="선택한 외곽 변의 두 끝 사이에 새 정점을 넣습니다. 기존 정점은 자동으로 내부 정점이 되지 않습니다.";
        deleteMesh.Click+=(_,_)=>Guard(()=>
        {
            if(session.SelectedKey is not{} key)return;
            if(MessageBox.Show(this,$"메시 '{session.Document.Meshes[key].Name}'와 해당 Deform 키를 삭제하시겠습니까?\nUndo로 되돌릴 수 있습니다.","메시 삭제",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
            PausePlayback();session.DeleteSelectedMesh(ValidateMeshView);
        });
        deleteVertex.Click+=(_,_)=>Guard(()=>{PausePlayback();session.DeleteSelectedVertex(ValidateMeshView);});
        AutomationProperties.SetName(meshName,"새 메시 이름");AutomationProperties.SetName(meshRegion,"새 메시 Atlas 영역");AutomationProperties.SetName(insertVertex,"클릭하여 메시 정점 추가");
        insertVertex.ToolTip="설정 자세에서 메시 내부를 클릭합니다. 외곽을 바꾸려면 편집할 외곽 변을 선택하고 외곽 정점 추가를 사용하세요.";
        deleteVertex.ToolTip="선택한 모든 정점을 삭제합니다. Undo 한 번으로 되돌릴 수 있습니다.";
    }
    void ValidateMeshView(Scene next)=>EditorHandles.ValidateView(next,atlas,session.Camera,session.Skin,skia:nativeRenderer);
    HullEdgeChoice SelectedHullEdge()=>hullEdge.SelectedItem as HullEdgeChoice??throw new InvalidOperationException("편집할 외곽 변을 선택하세요.");
    void RefreshMeshTools()
    {
        if(!session.IsDragging&&!ReferenceEquals(structureScene,session.Document))
        {
            var old=structureScene?.Meshes;var next=session.Document.Meshes;
            if(old is null||old.Count!=next.Count||next.Any(p=>!old.TryGetValue(p.Key,out var mesh)||mesh.UV.Length!=p.Value.UV.Length||mesh.Triangles.Length!=p.Value.Triangles.Length))inspectedEditor=null;
            structureScene=session.Document;
        }
        if(!ReferenceEquals(meshChoicesAtlas,atlas))
        {
            meshRegion.ItemsSource=atlas.Regions.Values.Where(r=>r.OriginalWidth>0&&r.OriginalHeight>0).OrderBy(r=>r.Name,StringComparer.Ordinal).ToArray();meshRegion.SelectedIndex=0;meshChoicesAtlas=atlas;
        }
        var editable=session.Editor.CanEditMeshStructure&&session.Animation is null&&!session.DeformMode&&!session.IsDragging;
        var selected=session.SelectedKey is{} key&&Scene.Text(session.Document.Meshes[key].Data["type"])=="mesh";
        var context=(session.Editor,session.Skin,session.SelectedKey,session.SelectedSlot,session.Animation);
        if(meshToolContext!=context){meshToolContext=context;meshEdit.IsExpanded=false;meshParts.IsExpanded=session.SelectedSlot is not null;insertVertex.IsChecked=false;insertHullVertex.IsChecked=false;}
        if(!session.IsDragging&&hullContext!=(session.Document,session.SelectedKey))
        {
            hullContext=(session.Document,session.SelectedKey);hullEdge.ItemsSource=null;
            if(selected)
            {
                try{var ring=MeshTopology.Boundary(session.Document.Meshes[session.SelectedKey!].Triangles);hullEdge.ItemsSource=Enumerable.Range(0,ring.Length).Select(i=>new HullEdgeChoice(ring[i],ring[(i+1)%ring.Length])).ToArray();hullEdge.ToolTip=null;}
                catch(Exception error)when(EditorErrors.IsExpected(error)){hullEdge.ToolTip=error.Message;}
            }
            hullEdge.SelectedIndex=-1;insertHullVertex.IsChecked=false;
        }
        meshEdit.Visibility=selected?Visibility.Visible:Visibility.Collapsed;
        meshParts.Visibility=selected||session.SelectedSlot is not null?Visibility.Visible:Visibility.Collapsed;
        meshTools.IsEnabled=editable&&(selected||session.SelectedSlot is not null);deleteMesh.IsEnabled=selected;deleteVertex.IsEnabled=selected&&session.SelectedVertex>=0;insertVertex.IsEnabled=selected;
        meshVertexTools.IsEnabled=editable&&selected;
        if(!editable||!selected){insertVertex.IsChecked=false;insertHullVertex.IsChecked=false;}
        includeHullVertex.IsEnabled=selected&&session.SelectedVertices.Count==1;excludeHullVertices.IsEnabled=selected&&session.SelectedVertices.Count>0;
        meshHelp.Text=!session.Editor.CanEditMeshStructure?"이 SKEL의 애니메이션을 완전히 판독하지 못해 구조 편집을 비활성화했습니다.":session.Animation is not null?"설정 모드에서 메시 구조를 편집할 수 있습니다.":$"선택 정점 {session.SelectedVertices.Count}개 · 같은 메시에서 Ctrl+클릭으로 선택/해제, Ctrl+드래그로 범위 추가 선택. 선택 정점을 드래그하면 함께 이동하며, Delete 또는 ‘선택 정점 삭제’로 함께 삭제합니다.";
        wireframes=wireframes.Intersect(session.Document.Meshes.Keys);
    }
}
