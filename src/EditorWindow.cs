using System.Globalization;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
namespace IndependentDev;

internal static class EditorErrors
{
    public static bool IsExpected(Exception error)=>error is InvalidDataException or IOException or UnauthorizedAccessException or FormatException or OverflowException or InvalidOperationException or ArgumentException or NotSupportedException or System.Text.Json.JsonException;
}

internal sealed record VertexHandle(string Key,int Index,Point Screen);
internal readonly record struct VertexGeometry(string Key,int Index,Point Position);
internal sealed record AnimationChoice(string? Name)
{public string Label=>Name is null?"설정 자세":"애니메이션 · "+Name;}
internal static class EditorHandles
{
    public static IEnumerable<(Point Start,Point End,bool Selected)> BoneLines(EditingSession session,Affine[] pose,int? selected)
        =>BoneLines(session.Document,session.Skin,pose,selected,session.Camera.Matrix);
    public static IEnumerable<(Point Start,Point End,bool Selected)> BoneLines(Scene scene,string skin,Affine[] pose,int? selected,Affine camera,IReadOnlySet<string>? hidden=null)
    {
        if(pose.Length!=scene.Bones.Length)throw new InvalidDataException("Bone pose size");var active=scene.ActiveBones(skin);
        for(var i=0;i<scene.Bones.Length;i++)
        {
            if(!active[i]||hidden?.Contains("bone:"+i)==true)continue;var transform=camera*pose[i];var start=transform.Apply(new());var end=transform.Apply(new(scene.Bones[i].Length,0));start.Validate();end.Validate();yield return(start,end,i==selected);
        }
    }
    public static IEnumerable<(Point Start,Point End,bool Selected,bool Hull)> MeshLines(EditingSession session,IEnumerable<RenderPart> parts)
        =>MeshLines(session.Document,session.Skin,session.SelectedKey,parts,session.Camera.Matrix);
    public static IEnumerable<(Point Start,Point End,bool Selected,bool Hull)> MeshLines(Scene scene,string skin,string? selectedKey,IEnumerable<RenderPart> parts,Affine camera,IReadOnlySet<string>? wireframes=null)
    {
        var visible=parts.ToArray();var uses=new Dictionary<(string Slot,string Attachment,Vertex A,Vertex B),int>();
        foreach(var part in visible)if(part.Vertices.Length==3)for(var i=0;i<3;i++)
        {
            var a=part.Vertices[i];var b=part.Vertices[(i+1)%3];var edge=(part.Slot,part.Attachment,a,b);var reverse=(part.Slot,part.Attachment,b,a);uses[edge]=uses.GetValueOrDefault(edge)+1;uses[reverse]=uses.GetValueOrDefault(reverse)+1;
        }
        (string Slot,string Attachment)? previous=null;bool isMesh=false,selected=false;
        foreach(var part in visible)
        {
            var identity=(part.Slot,part.Attachment);
            if(previous!=identity){if(!scene.TryMesh(skin,part.Slot,part.Attachment,out var mesh))throw new InvalidDataException("Wire attachment reference");var key=Scene.Key(mesh.Skin,mesh.Slot,mesh.Name);selected=key==selectedKey;isMesh=Scene.Text(mesh.Data["type"])=="mesh"&&(wireframes is null||selected||wireframes.Contains(key));previous=identity;}
            if(!isMesh)continue;
            if(part.Vertices.Length!=3)throw new InvalidDataException("Wire triangle size");
            for(var i=0;i<3;i++)yield return(camera.Apply(part.Vertices[i].Position),camera.Apply(part.Vertices[(i+1)%3].Position),selected,uses[(part.Slot,part.Attachment,part.Vertices[i],part.Vertices[(i+1)%3])]==1);
        }
    }
    public static void ValidateView(Scene scene,TextureAtlas atlas,CanvasCamera camera,string skin="default",string? animation=null,double time=0,bool skia=false)
    {
        var viewport=camera.Viewport;var resolution=Math.Min(1,Math.Sqrt(1_000_000/(viewport.X*viewport.Y)));var width=Math.Max(1,(int)(viewport.X*resolution));var height=Math.Max(1,(int)(viewport.Y*resolution));
        var parts=Playback.Parts(scene,skin,animation,time,atlas);var matrix=new Affine(width/viewport.X,0,0,height/viewport.Y,0,0)*camera.Matrix;
        if(skia)SkiaRaster.Draw(width,height,parts,atlas.Pages,matrix);else TexturedRaster.Draw(width,height,parts,atlas.Pages,matrix);
    }
    public static VertexHandle[] Visible(EditingSession session,TextureAtlas atlas)
    {
        var parts=Playback.Parts(session.Document,session.Skin,out var pose,session.Animation,session.Time,atlas);
        return Visible(session,parts,pose);
    }
    public static VertexHandle[] Visible(EditingSession session,IEnumerable<RenderPart> parts,Affine[] pose)
        =>Visible(session.Document,session.Skin,parts,pose,session.Camera.Matrix,session.Animation,session.Time);
    public static VertexHandle[] Visible(Scene scene,string skin,IEnumerable<RenderPart> parts,Affine[] pose,Affine camera,string? animation,double time)
        =>ProjectVertices(VisibleVertices(scene,skin,parts,pose,animation,time),camera);
    internal static VertexHandle[] ProjectVertices(IEnumerable<VertexGeometry> vertices,Affine camera)
        =>vertices.Select(vertex=>new VertexHandle(vertex.Key,vertex.Index,camera.Apply(vertex.Position))).ToArray();
    internal static IEnumerable<VertexGeometry> VisibleVertices(Scene scene,string skin,IEnumerable<RenderPart> parts,Affine[] pose,string? animation,double time)
    {
        foreach(var identity in parts.Select(p=>(p.Slot,p.Attachment)).Distinct())
        {
            if(!scene.TryMesh(skin,identity.Slot,identity.Attachment,out var mesh))mesh=scene.Meshes[Scene.Key("default",identity.Slot,identity.Attachment)];
            if(Scene.Text(mesh.Data["type"])!="mesh")continue;
            var vertices=scene.Vertices(mesh,pose,Playback.Deform(scene,mesh,animation,time));
            var key=Scene.Key(mesh.Skin,mesh.Slot,mesh.Name);
            for(var i=0;i<vertices.Length;i++)yield return new(key,i,vertices[i].Position);
        }
    }
    public static VertexHandle? Hit(IEnumerable<VertexHandle> handles,Point screen,string? selectedKey,int selectedIndex)
        =>handles.Select(h=>(Handle:h,Distance:(h.Screen-screen).Length)).Where(p=>p.Distance<=8).OrderBy(p=>p.Distance).ThenBy(p=>p.Handle.Key==selectedKey&&p.Handle.Index==selectedIndex?0:1).Select(p=>p.Handle).FirstOrDefault();
    internal static string? PartAt(EditingSession session,TextureAtlas atlas,Point screen,Func<string,bool>? canSelect=null)
    {
        var point=session.Camera.ToWorld(screen);var parts=Playback.Parts(session.Document,session.Skin,session.Animation,session.Time,atlas);
        foreach(var part in parts.AsEnumerable().Reverse())
        {
            if(part.Tint.A<=0||!session.Document.TryMesh(session.Skin,part.Slot,part.Attachment,out var mesh)||Scene.Text(mesh.Data["type"],"region") is not("mesh" or "region"))continue;
            if(canSelect is not null&&!canSelect(Scene.Key(mesh.Skin,mesh.Slot,mesh.Name)))continue;var p=part.Vertices.Select(vertex=>vertex.Position-point).ToArray();var scale=p.SelectMany(v=>new[]{Math.Abs(v.X),Math.Abs(v.Y)}).Max();if(scale==0||!double.IsFinite(scale))continue;for(var i=0;i<p.Length;i++)p[i]=new(p[i].X/scale,p[i].Y/scale);
            var area=Point.Cross(p[1]-p[0],p[2]-p[0]);if(area==0)continue;var a=Point.Cross(p[0],p[1]);var b=Point.Cross(p[1],p[2]);var c=Point.Cross(p[2],p[0]);if(a>=0&&b>=0&&c>=0||a<=0&&b<=0&&c<=0)return Scene.Key(mesh.Skin,mesh.Slot,mesh.Name);
        }
        return null;
    }
    public static VertexHandle? Cycle(VertexHandle[] handles,string? selectedKey,int selectedIndex,bool reverse)
    {
        if(handles.Length==0)return null;
        var index=Array.FindIndex(handles,h=>h.Key==selectedKey&&h.Index==selectedIndex);
        return index<0?(reverse?handles[^1]:handles[0]):handles[(index+(reverse?-1:1)+handles.Length)%handles.Length];
    }
}

internal sealed class InspectorItem(InspectorNode node):INotifyPropertyChanged
{
    public InspectorNode Node {get;}=node;
    public InspectorItem? Parent {get;set;}
    public int DisplayDepth {get;set;}=-1;
    public string Label=>DisplayDepth<0?Node.Label:$"[{DisplayDepth}] {Node.Label}";
    public bool FlatBranch=>DisplayDepth>=0&&Children.Count>0;
    public IEnumerable<InspectorItem> TreeChildren=>DisplayDepth<0?Children:[];
    public ObservableCollection<InspectorItem> Children {get;}=[];
    bool expanded=node.Parent<0,selected,shown=true,selectable=true;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool Shown {get=>shown;set{if(shown==value)return;shown=value;PropertyChanged?.Invoke(this,new(nameof(Shown)));}}
    public bool Selectable {get=>selectable;set{if(selectable==value)return;selectable=value;PropertyChanged?.Invoke(this,new(nameof(Selectable)));}}
    public bool Expanded {get=>expanded;set{if(expanded==value)return;expanded=value;PropertyChanged?.Invoke(this,new(nameof(Expanded)));}}
    public bool Selected {get=>selected;set{if(selected==value)return;selected=value;PropertyChanged?.Invoke(this,new(nameof(Selected)));}}
}

internal sealed partial class EditorWindow:Window
{
    EditingSession session;
    TextureAtlas atlas;
    string? savedPath,savedDigest;
    string? originalSkelPath;
    string outputRoot;
    readonly bool nativeRenderer;
    readonly bool binaryRotation;
    readonly bool binarySetup4117;
    readonly bool preserveAnimations;
    readonly EditorSurface surface;
    readonly TexturePreview textures;
    TextureAtlas? previewAtlas;
    readonly TabControl workspaceTabs=new(){Margin=new Thickness(8,4,8,0)};
    readonly ComboBox skins=new(){MinWidth=100};
    readonly ListBox animations=new(){MinWidth=80,DisplayMemberPath=nameof(AnimationChoice.Label)};
    readonly TextBox time=new(){Text="0",Width=150};
    readonly CheckBox deform=new(){Content="Deform 편집",VerticalAlignment=VerticalAlignment.Center};
    readonly CheckBox showMeshes=new(){Content="전체 메시 표시",IsChecked=false,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(7,0,0,0)};
    readonly CheckBox meshWireframe=new(){Content="선택 메시 표시 유지",ToolTip="선택을 해제한 뒤에도 이 메시의 정점과 선을 표시합니다.",Margin=new Thickness(12),Visibility=Visibility.Collapsed};
    ImmutableHashSet<string> wireframes=ImmutableHashSet<string>.Empty;
    readonly CheckBox showBones=new(){Content="본 표시",IsChecked=true,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(7,0,0,0)};
    readonly CheckBox crispPixels=new(){Content="픽셀 선명하게",IsChecked=true,Margin=new Thickness(7,0,0,0)};
    readonly CheckBox pixelGrid=new(){Content="픽셀 격자",IsChecked=true,Margin=new Thickness(7,0,0,0)};
    readonly CheckBox transparency=new(){Content="투명 배경",IsChecked=true,Margin=new Thickness(7,0,0,0)};
    readonly TextBlock status=new(){Margin=new Thickness(8)};
    readonly TreeView hierarchy=new();
    readonly TextBlock properties=new(){Margin=new Thickness(8),TextWrapping=TextWrapping.Wrap};
    readonly TextBox vertexIndex=new(){Width=65},vertexX=new(){Width=160},vertexY=new(){Width=160};
    readonly StackPanel vertexTools=new(){Margin=new Thickness(8)};
    readonly StackPanel colorTools=new(){Margin=new Thickness(12)};
    readonly StackPanel slotColorTools=new(){Margin=new Thickness(12)};
    readonly StackPanel slotAttachmentTools=new(){Margin=new Thickness(12)};
    readonly TextBox slotAttachment=new(){Width=160,HorizontalAlignment=HorizontalAlignment.Left};
    readonly TextBox slotColor=new(){Width=160,MaxLength=8,HorizontalAlignment=HorizontalAlignment.Left};
    readonly TextBox attachmentColor=new(){Width=160,MaxLength=8,HorizontalAlignment=HorizontalAlignment.Left};
    Editor? inspectedEditor;
    string? inspectedSkin;
    readonly Dictionary<string,InspectorItem> meshItems=[];
    InspectorItem[]? flatRoots;
    bool refreshing;
    public EditorWindow(string jsonPath,string atlasFile,bool skia=false,bool binary=false,bool rotation=false,bool setup4117=false,bool preserveAnimations=false)
    {
        InitializeComponent();nativeRenderer=skia;binaryRotation=rotation;binarySetup4117=setup4117;this.preserveAnimations=preserveAnimations;
        outputRoot=Path.Combine(Experiment.Root(),"output",binary?"manual-binary-"+Guid.NewGuid().ToString("N"):"manual-editor");
        atlas=TextureAtlas.Load(atlasFile);session=binary?new(BinaryDocument.Load(jsonPath).UnchangedBytes(),new(960,640),rotation,setup4117,preserveAnimations):new(Input.ReadText(jsonPath),new(960,640));
        Playback.Parts(session.Document,session.Skin,atlas:atlas);
        if(nativeRenderer)EditorHandles.ValidateView(session.Document,atlas,session.Camera,skia:true);
        if(binary){originalSkelPath=savedPath=Path.GetFullPath(jsonPath);savedDigest=session.Editor.BinaryDigest;}
        Width=1280;Height=800;MinWidth=900;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterScreen;Title="독립 2D 편집기 · 실험";
        var panel=(Brush)FindResource("EditorPanelBrush");var border=(Brush)FindResource("EditorBorderBrush");var dock=new DockPanel();Content=dock;var tools=new WrapPanel();
        var toolbar=new Border{Child=tools,Padding=new Thickness(12,8,12,8),Background=panel,BorderBrush=border,BorderThickness=new Thickness(0,0,0,1)};DockPanel.SetDock(toolbar,Dock.Top);dock.Children.Add(toolbar);
        void Button(string text,Action action){var button=new Button{Content=text,Padding=new Thickness(8,4,8,4),Margin=new Thickness(2)};button.Click+=(_,_)=>Guard(action);tools.Children.Add(button);}
        Button("열기",Open);Button("PNG 보기",OpenTexture);Button("Atlas 열기",OpenAtlasPreview);Button("Atlas 보기",ShowAtlas);Button("저장",()=>Save(false));Button("다른 이름",()=>Save(true));Button("Undo",()=>{PausePlayback();session.Undo();});Button("Redo",()=>{PausePlayback();session.Redo();});Button("맞춤",Fit);Button("100%",()=>session.Camera.ActualSize());
        void Field(string label,FrameworkElement field){tools.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(7,0,3,0)});AutomationProperties.SetName(field,label);tools.Children.Add(field);}
        ConfigureEditorMode();Field("모드",editorMode);Field("스킨",skins);
        tools.Children.Add(showMeshes);AutomationProperties.SetName(showMeshes,"전체 메시 정점과 선 표시");
        tools.Children.Add(showBones);AutomationProperties.SetName(showBones,"본 표시");
        foreach(var option in new[]{crispPixels,pixelGrid,transparency}){tools.Children.Add(option);option.Click+=(_,_)=>surface?.InvalidateVisual();}
        skins.SelectionChanged+=(_,_)=>{if(!refreshing)Guard(()=>{session.FinishDrag(true);var skin=(string)skins.SelectedItem;EditorHandles.ValidateView(session.Document,atlas,session.Camera,skin,session.Animation,session.Time,nativeRenderer);session.SetSkin(skin);});};
        animations.SelectionChanged+=(_,_)=>{if(!refreshing)Guard(ApplyPose);};deform.Click+=(_,_)=>Guard(ApplyPose);
        status.Margin=new Thickness(0);status.TextWrapping=TextWrapping.Wrap;status.Foreground=(Brush)FindResource("EditorMutedBrush");var statusBar=new Border{Child=status,Padding=new Thickness(12,7,12,7),Background=panel,BorderBrush=border,BorderThickness=new Thickness(0,1,0,0)};DockPanel.SetDock(statusBar,Dock.Bottom);dock.Children.Add(statusBar);
        var workspace=new Grid();workspace.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star),MinWidth=200});workspace.ColumnDefinitions.Add(new(){Width=new GridLength(5)});workspace.ColumnDefinitions.Add(new(){Width=new GridLength(270),MinWidth=200});dock.Children.Add(workspaceTabs);workspaceTabs.Items.Add(new TabItem{Header="스켈레톤",Content=workspace});
        workspace.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star),MinHeight=200});workspace.RowDefinitions.Add(timelineSplitRow);workspace.RowDefinitions.Add(timelineRow);
        timeline.Background=panel;timeline.ColumnDefinitions.Add(new(){Width=new GridLength(200),MinWidth=80});timeline.ColumnDefinitions.Add(new(){Width=new GridLength(5)});timeline.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star),MinWidth=200});Grid.SetRow(timeline,2);Grid.SetColumnSpan(timeline,3);workspace.Children.Add(timeline);
        animations.Background=panel;animations.BorderBrush=border;AutomationProperties.SetName(animations,"애니메이션 목록");VirtualizingStackPanel.SetIsVirtualizing(animations,true);VirtualizingStackPanel.SetVirtualizationMode(animations,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(animations,true);timeline.Children.Add(animations);
        var poseTools=new WrapPanel{Margin=new Thickness(12,6,12,6)};Grid.SetColumn(poseTools,2);timeline.Children.Add(poseTools);poseTools.Children.Add(new TextBlock{Text="시각(초)",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,7,0)});poseTools.Children.Add(time);poseTools.Children.Add(deform);AutomationProperties.SetName(time,"시각(초)");AutomationProperties.SetName(deform,"Deform 편집");
        var applyPose=new Button{Content="시각 적용",Padding=new Thickness(8,4,8,4),Margin=new Thickness(7,0,0,0)};applyPose.Click+=(_,_)=>Guard(ApplyPose);poseTools.Children.Add(applyPose);
        ConfigurePlayback(poseTools);
        var listSplit=new GridSplitter{Width=5,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,Background=Brushes.Transparent,ResizeDirection=GridResizeDirection.Columns,ResizeBehavior=GridResizeBehavior.PreviousAndNext,Focusable=true};Grid.SetColumn(listSplit,1);AutomationProperties.SetName(listSplit,"애니메이션 목록 너비 조절");timeline.Children.Add(listSplit);
        Grid.SetRow(timeSplit,1);Grid.SetColumnSpan(timeSplit,3);AutomationProperties.SetName(timeSplit,"타임라인 높이 조절");workspace.Children.Add(timeSplit);
        var inspector=new Grid();Grid.SetColumn(inspector,2);workspace.Children.Add(inspector);
        inspector.Background=panel;hierarchy.Background=panel;hierarchy.BorderBrush=border;hierarchy.Padding=new Thickness(12);properties.Margin=vertexTools.Margin=new Thickness(12);
        var sideSplit=new GridSplitter{Width=5,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,Background=Brushes.Transparent,ResizeDirection=GridResizeDirection.Columns,ResizeBehavior=GridResizeBehavior.PreviousAndNext,Focusable=true};Grid.SetColumn(sideSplit,1);AutomationProperties.SetName(sideSplit,"계층 패널 너비 조절");workspace.Children.Add(sideSplit);
        AutomationProperties.SetName(meshWireframe,"선택 메시 Wireframe 표시");meshWireframe.Click+=(_,_)=>Guard(()=>{if(session.SelectedKey is{} key&&Scene.Text(session.Document.Meshes[key].Data["type"])=="mesh")wireframes=meshWireframe.IsChecked==true?wireframes.Add(key):wireframes.Remove(key);});
        inspector.Children.Add(hierarchy);ConfigurePropertiesPanel(inspector);
        void VertexField(string label,TextBox field){vertexTools.Children.Add(new TextBlock{Text=label});AutomationProperties.SetName(field,label);field.HorizontalAlignment=HorizontalAlignment.Left;vertexTools.Children.Add(field);}
        slotAttachmentTools.Children.Add(new TextBlock{Text="슬롯 기본 어태치먼트"});AutomationProperties.SetName(slotAttachment,"슬롯 기본 어태치먼트");slotAttachmentTools.Children.Add(slotAttachment);var referenceButtons=new WrapPanel();slotAttachmentTools.Children.Add(referenceButtons);var applyAttachment=new Button{Content="어태치먼트 적용"};applyAttachment.Click+=(_,_)=>Guard(ApplySlotAttachment);referenceButtons.Children.Add(applyAttachment);var clearAttachment=new Button{Content="어태치먼트 해제"};clearAttachment.Click+=(_,_)=>Guard(ClearSlotAttachment);referenceButtons.Children.Add(clearAttachment);
        slotColorTools.Children.Add(new TextBlock{Text="슬롯 설정 색상 (RRGGBBAA)"});AutomationProperties.SetName(slotColor,"슬롯 설정 색상 RRGGBBAA");slotColorTools.Children.Add(slotColor);var applySlotColor=new Button{Content="슬롯 색상 적용",HorizontalAlignment=HorizontalAlignment.Left};applySlotColor.Click+=(_,_)=>Guard(ApplySlotColor);slotColorTools.Children.Add(applySlotColor);
        colorTools.Children.Add(new TextBlock{Text="어태치먼트 색상 (RRGGBBAA)"});AutomationProperties.SetName(attachmentColor,"어태치먼트 색상 RRGGBBAA");colorTools.Children.Add(attachmentColor);var applyColor=new Button{Content="색상 적용",HorizontalAlignment=HorizontalAlignment.Left};applyColor.Click+=(_,_)=>Guard(ApplyAttachmentColor);colorTools.Children.Add(applyColor);
        VertexField("정점 번호 (0부터)",vertexIndex);var selectVertex=new Button{Content="정점 선택",HorizontalAlignment=HorizontalAlignment.Left};vertexTools.Children.Add(selectVertex);
        selectVertex.Click+=(_,_)=>Guard(()=>session.Select(session.SelectedKey,int.Parse(vertexIndex.Text,CultureInfo.InvariantCulture)));
        VertexField("월드 X",vertexX);VertexField("월드 Y",vertexY);var applyVertex=new Button{Content="좌표 적용",HorizontalAlignment=HorizontalAlignment.Left};vertexTools.Children.Add(applyVertex);
        applyVertex.ToolTip="여러 정점을 선택하면 대표 정점을 이 좌표로 옮기고, 나머지도 같은 거리만큼 이동합니다.";
        applyVertex.Click+=(_,_)=>Guard(ApplyVertexPosition);
        AutomationProperties.SetName(hierarchy,"본 슬롯 어태치먼트 계층");AutomationProperties.SetName(properties,"선택 항목 설정 속성");
        var itemStyle=new Style(typeof(TreeViewItem),(Style)FindResource(typeof(TreeViewItem)));itemStyle.Setters.Add(new Setter(TreeViewItem.IsExpandedProperty,new Binding(nameof(InspectorItem.Expanded)){Mode=BindingMode.TwoWay}));itemStyle.Setters.Add(new Setter(TreeViewItem.IsSelectedProperty,new Binding(nameof(InspectorItem.Selected)){Mode=BindingMode.TwoWay}));itemStyle.Setters.Add(new Setter(AutomationProperties.NameProperty,new Binding(nameof(InspectorItem.Label)){Mode=BindingMode.OneTime}));itemStyle.Setters.Add(new Setter(AutomationProperties.HelpTextProperty,new Binding("Node.Details"){Mode=BindingMode.OneTime}));
        var selectedItem=new Trigger{Property=TreeViewItem.IsSelectedProperty,Value=true};selectedItem.Setters.Add(new Setter(VirtualizingPanel.IsContainerVirtualizableProperty,false));itemStyle.Triggers.Add(selectedItem);
        var header=new FrameworkElementFactory(typeof(StackPanel));header.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);
        var fold=new FrameworkElementFactory(typeof(CheckBox));fold.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,4,0));fold.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);fold.SetValue(UIElement.FocusableProperty,false);fold.SetValue(FrameworkElement.ToolTipProperty,"하위 항목 표시");fold.SetBinding(CheckBox.IsCheckedProperty,new Binding(nameof(InspectorItem.Expanded)){Mode=BindingMode.TwoWay});fold.SetBinding(UIElement.VisibilityProperty,new Binding(nameof(InspectorItem.FlatBranch)){Mode=BindingMode.OneTime,Converter=new BooleanToVisibilityConverter()});fold.SetBinding(AutomationProperties.NameProperty,new Binding(nameof(InspectorItem.Label)){Mode=BindingMode.OneTime});fold.SetValue(AutomationProperties.HelpTextProperty,"하위 항목 표시");header.AppendChild(fold);
        AddTreeToggles(header);var label=new FrameworkElementFactory(typeof(TextBlock));label.SetBinding(TextBlock.TextProperty,new Binding(nameof(InspectorItem.Label)){Mode=BindingMode.OneTime});header.AppendChild(label);var template=new HierarchicalDataTemplate(typeof(InspectorItem)){ItemsSource=new Binding(nameof(InspectorItem.TreeChildren)){Mode=BindingMode.OneTime},ItemContainerStyle=itemStyle,VisualTree=header};hierarchy.ItemTemplate=template;hierarchy.ItemContainerStyle=itemStyle;
        VirtualizingStackPanel.SetIsVirtualizing(hierarchy,true);VirtualizingStackPanel.SetVirtualizationMode(hierarchy,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(hierarchy,true);
        hierarchy.SelectedItemChanged+=(_,e)=>{if(refreshing||e.NewValue is not InspectorItem item)return;var node=item.Node;Guard(()=>{properties.Text=node.Details;if(node.SlotName is{} slotName){session.SelectSlot(slotName);return;}session.Select(node.MeshKey,node.MeshKey is null?-1:node.MeshKey==session.SelectedKey?session.SelectedVertex:0);});};
        hierarchy.PreviewKeyDown+=(_,e)=>
        {
            if(e.OriginalSource is CheckBox||e.KeyboardDevice.Modifiers!=ModifierKeys.None||e.Key is not(Key.Left or Key.Right or Key.Space)||hierarchy.SelectedItem is not InspectorItem{DisplayDepth:>=0} item||e.Key==Key.Space&&!item.FlatBranch)return;
            e.Handled=true;Guard(()=>{if(e.Key==Key.Space){item.Expanded=!item.Expanded;return;}var target=e.Key==Key.Left?item.Parent:item.Children.FirstOrDefault();if(target is null)return;if(e.Key==Key.Right)item.Expanded=true;RevealInspector(target);(hierarchy.ItemContainerGenerator.ContainerFromItem(target) as TreeViewItem)?.Focus();});
        };
        surface=new(this);workspace.Children.Add(surface);
        textures=new(atlas,session.Document,session.Skin);previewAtlas=atlas;workspaceTabs.Items.Add(new TabItem{Header="텍스처",Content=textures});workspaceTabs.SelectedIndex=0;
        workspaceTabs.SelectionChanged+=(_,e)=>{if(ReferenceEquals(e.Source,workspaceTabs))Guard(()=>{session.FinishDrag(true);surface.ReleaseMouseCapture();});};
        showMeshes.Click+=(_,_)=>surface.InvalidateVisual();
        showBones.Click+=(_,_)=>surface.InvalidateVisual();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo,(_,_)=>Guard(()=>{PausePlayback();session.Undo();}),(_,e)=>e.CanExecute=session.IsDragging||session.Editor.UndoCount>0));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo,(_,_)=>Guard(()=>{PausePlayback();session.Redo();}),(_,e)=>e.CanExecute=session.CanRedo));
        PreviewKeyDown+=HandleKeyDown;PreviewKeyUp+=(_,e)=>surface.NavigationKey(e.Key,false);InputBindings.Add(new KeyBinding(ApplicationCommands.Redo,new KeyGesture(Key.Z,ModifierKeys.Control|ModifierKeys.Shift)));Closing+=(_,e)=>{PausePlayback();e.Cancel=true;Guard(()=>{session.FinishDrag(true);e.Cancel=!ConfirmDiscard();});};
        Loaded+=(_,_)=>Guard(Fit);RefreshLists();Refresh();
    }
    void Guard(Action action)
    {try{action();}catch(Exception e)when(EditorErrors.IsExpected(e)){MessageBox.Show(this,e.Message,"처리하지 못했습니다",MessageBoxButton.OK,MessageBoxImage.Error);}finally{Refresh();}}
    void RefreshLists()
    {
        PausePlayback();animationMode=false;lastAnimation=null;lastAnimationTime=0;
        refreshing=true;
        try
        {
            skins.ItemsSource=new ObservableCollection<string>(new[]{"default"}.Concat((Scene.Array(session.Document.Data["skins"])??[]).Select(s=>Scene.Text(s?["name"]))).Distinct());skins.SelectedItem=session.Skin;
            animations.ItemsSource=new ObservableCollection<AnimationChoice>(new[]{new AnimationChoice(null)}.Concat((Scene.Object(session.Document.Data["animations"])??[]).Select(a=>new AnimationChoice(a.Key))));animations.SelectedIndex=0;time.Text="0";deform.IsChecked=false;
        }
        finally{refreshing=false;}
    }
    void Refresh()
    {
        RefreshEditorMode();
        RefreshMeshTools();
        if(!ReferenceEquals(previewAtlas,atlas)){textures.SetAtlas(atlas,session.Document,session.Skin);previewAtlas=atlas;}
        textures.UpdateScene(session.Document,session.Skin);
        if(!session.IsDragging&&(!ReferenceEquals(inspectedEditor,session.Editor)||inspectedSkin!=session.Skin))
        {
            refreshing=true;
            try
            {
                properties.Text="항목을 선택하면 설정 속성을 표시합니다.";
                var nodes=Inspector.Build(session.Document,session.Skin);var items=nodes.Select(node=>new InspectorItem(node)).ToArray();var roots=new ObservableCollection<InspectorItem>();var depths=new int[items.Length];
                meshItems.Clear();flatRoots=null;foreach(var item in items)ConfigureTreeItem(item);
                for(var i=0;i<items.Length;i++)
                {
                    if(nodes[i].Parent<0)roots.Add(items[i]);else{items[i].Parent=items[nodes[i].Parent];items[i].Parent!.Children.Add(items[i]);depths[i]=depths[nodes[i].Parent]+1;}
                    if(nodes[i].MeshKey is{} key)meshItems.Add(key,items[i]);
                }
                if(depths.Any(depth=>depth>32))
                {
                    // WPF measures nested controls recursively; deep graphs use bounded visual rows with intact parent references.
                    for(var i=0;i<items.Length;i++)items[i].DisplayDepth=depths[i];
                    flatRoots=roots.ToArray();foreach(var item in items)item.PropertyChanged+=(_,e)=>{if(!refreshing&&e.PropertyName==nameof(InspectorItem.Expanded))Guard(RefreshFlatRows);};RefreshFlatRows();
                }
                else hierarchy.ItemsSource=roots;
                inspectedEditor=session.Editor;inspectedSkin=session.Skin;
            }
            finally{refreshing=false;}
        }
        refreshing=true;
        try
        {
            skins.SelectedItem=session.Skin;animations.SelectedItem=animations.Items.Cast<AnimationChoice>().FirstOrDefault(choice=>choice.Name==session.Animation);deform.IsChecked=session.DeformMode;deform.IsEnabled=session.Animation is not null&&(!session.Editor.PreservesAnimations||session.Editor.CanEditDeform41);
            if(session.SelectedKey is{} key&&meshItems.TryGetValue(key,out var item))
            {
                if(!item.Selected)ClearInspectorSelection();for(var parent=item.Parent;parent is not null;parent=parent.Parent)parent.Expanded=true;item.Selected=true;
                RefreshFlatRows();if(!ReferenceEquals(hierarchy.SelectedItem,item)&&hierarchy.ActualHeight>0)RevealInspector(item);
                properties.Text=item.Node.Details;
            }
            else if(hierarchy.SelectedItem is InspectorItem selected&&selected.Node.MeshKey is not null)ClearInspectorSelection();
        }
        finally{refreshing=false;}
        var slotName=session.SelectedSlot;slotColorTools.Visibility=slotName is null?Visibility.Collapsed:Visibility.Visible;slotColorTools.IsEnabled=!session.IsDragging&&slotName is not null&&(!session.Editor.IsBinary||session.Editor.BinarySetup4117);slotColor.Text=slotName is null?"":session.Editor.SlotColor(slotName);
        slotAttachmentTools.Visibility=slotName is null?Visibility.Collapsed:Visibility.Visible;slotAttachmentTools.IsEnabled=!session.IsDragging&&slotName is not null&&(!session.Editor.IsBinary||session.Editor.BinarySetup4117);slotAttachment.Text=slotName is null?"":session.Editor.SlotAttachment(slotName)??"";if(slotName is not null)properties.Text=Inspector.SlotDetails(session.Document,session.Document.Slots.Single(slot=>slot.Name==slotName));
        var colorKey=session.SelectedKey;var hasColor=colorKey is not null&&Scene.Text(session.Document.Meshes[colorKey].Data["type"],"region") is "region" or "mesh";
        meshWireframe.Visibility=colorKey is not null&&Scene.Text(session.Document.Meshes[colorKey].Data["type"])=="mesh"?Visibility.Visible:Visibility.Collapsed;meshWireframe.IsChecked=colorKey is not null&&wireframes.Contains(colorKey);
        colorTools.Visibility=hasColor?Visibility.Visible:Visibility.Collapsed;colorTools.IsEnabled=!session.IsDragging&&hasColor&&(!session.Editor.IsBinary||session.Editor.BinarySetup4117);attachmentColor.Text=hasColor?session.Editor.AttachmentColor(colorKey!):"";
        var position=session.IsDragging?null:session.SelectedPosition();vertexTools.IsEnabled=position is not null;
        vertexIndex.Text=position is null?"":session.SelectedVertex.ToString(CultureInfo.InvariantCulture);
        vertexX.Text=position?.X.ToString("G17",CultureInfo.InvariantCulture)??"";vertexY.Text=position?.Y.ToString("G17",CultureInfo.InvariantCulture)??"";
        var setupEditable=!session.Editor.PreservesAnimations||session.Animation is null;slotColorTools.IsEnabled&=setupEditable;slotAttachmentTools.IsEnabled&=setupEditable;colorTools.IsEnabled&=setupEditable;vertexTools.IsEnabled&=!session.PoseReadOnly;
        var preserved=session.Editor.PreservedAnimationCount>0?$" · 애니메이션 {session.Editor.PreservedAnimationCount}개 · "+(session.Editor.PlaybackError is{} error?"재생 미지원: "+error:"재생 / Deform 키 편집 가능"):"";
        Title=(session.Modified?"* ":"")+"독립 2D 편집기 · 실험"+(session.Editor.IsBinary?" · SKEL "+session.Document.Version+(session.Editor.BinaryRotation?" · 본 애니메이션":""):"")+(nativeRenderer?" · Skia":"")+preserved;
        status.Text=$"{session.Document.Version} · 캔버스 {session.Canvas.Width:0} × {session.Canvas.Height:0} px · 좌상단 (0,0) X→ Y↓ · {session.Camera.Scale*100:0.##}% · Undo {session.Editor.UndoCount} / Redo {session.Editor.RedoCount}{(session.Editor.HistoryTruncated?" (이력 용량 제한 적용)":"")} · 선택 정점 {session.SelectedVertices.Count}개 · Ctrl+클릭/드래그 다중 선택 · 정점 드래그 / 우클릭 또는 Space+드래그 이동 / 휠 확대 · {savedPath??"저장 전"}";
        status.Text+=" · 격자 1 px / 배경 체크 8 px";
        status.Text+=preserved+(session.PoseReadOnly?" · 정점 편집: Deform 편집을 켜거나 설정 자세 선택 · 인게임 보간 동일성 미검증":"");RefreshPlaybackControls();surface.InvalidateVisual();
    }
    void RefreshFlatRows()
    {
        if(flatRoots is null)return;
        var rows=new List<InspectorItem>();var pending=new Stack<InspectorItem>(flatRoots.Reverse());
        while(pending.TryPop(out var item)){rows.Add(item);if(item.Expanded)for(var i=item.Children.Count-1;i>=0;i--)pending.Push(item.Children[i]);}
        if(hierarchy.Items.Cast<InspectorItem>().SequenceEqual(rows))return;
        var visible=rows.ToHashSet();var selected=hierarchy.SelectedItem as InspectorItem;var previousSelected=selected;while(selected is not null&&!visible.Contains(selected))selected=selected.Parent;
        var previous=refreshing;refreshing=true;try{if(previousSelected is not null)previousSelected.Selected=false;hierarchy.ItemsSource=rows;}finally{refreshing=previous;}
        if(selected is not null&&hierarchy.ActualHeight>0)RevealInspector(selected);
    }
    void RevealInspector(InspectorItem selected)
    {
        static VirtualizingPanel? Host(DependencyObject view,ItemsControl owner)
        {
            if(view is ItemsPresenter presenter&&ReferenceEquals(presenter.TemplatedParent,owner))return VisualTreeHelper.GetChildrenCount(presenter)>0?VisualTreeHelper.GetChild(presenter,0) as VirtualizingPanel:null;
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(view);i++)
            {var child=VisualTreeHelper.GetChild(view,i);if(child is not ItemsControl&&Host(child,owner) is{} panel)return panel;}
            return null;
        }
        var path=new Stack<InspectorItem>();if(selected.DisplayDepth>=0)path.Push(selected);else for(InspectorItem? item=selected;item is not null;item=item.Parent)path.Push(item);
        ItemsControl control=hierarchy;
        while(path.TryPop(out var item))
        {
            control.ApplyTemplate();control.UpdateLayout();
            var container=control.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
            if(container is null)
            {
                var index=control.Items.IndexOf(item);if(index<0||Host(control,control) is not{} panel)return;
                panel.BringIndexIntoViewPublic(index);control.UpdateLayout();container=control.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
            }
            if(container is null)return;
            if(path.Count==0){container.SetCurrentValue(TreeViewItem.IsSelectedProperty,true);container.BringIntoView();return;}
            container.SetCurrentValue(TreeViewItem.IsExpandedProperty,true);control=container;
        }
    }
    void ApplyPose()
    {
        PausePlayback();
        try
        {
            var value=double.Parse(time.Text,CultureInfo.InvariantCulture);var name=(animations.SelectedItem as AnimationChoice)?.Name;
            session.FinishDrag(true);EditorHandles.ValidateView(session.Document,atlas,session.Camera,session.Skin,name,value,nativeRenderer);
            session.SetPose(name,value,name is not null&&deform.IsChecked==true);
            animationMode=name is not null;
        }
        finally{time.Text=session.Time.ToString("G17",CultureInfo.InvariantCulture);}
    }
    void ValidateSlotAttachmentView(Scene scene)=>EditorHandles.ValidateView(scene,atlas,session.Camera,session.Skin,session.Animation,session.Time,nativeRenderer);
    void ApplySlotAttachment()
    {
        if(slotAttachment.Text.Length==0)throw new InvalidDataException("기존 어태치먼트 이름을 입력하세요. 비우려면 해제를 사용하세요.");session.SetSelectedSlotAttachment(slotAttachment.Text,ValidateSlotAttachmentView);
    }
    void ClearSlotAttachment()=>session.SetSelectedSlotAttachment(null,ValidateSlotAttachmentView);
    static uint ReadSetupColor(TextBox field)
    {
        if(field.Text.Length!=8||!uint.TryParse(field.Text,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out var color))throw new InvalidDataException("색상은 RRGGBBAA 8자리로 입력하세요.");return color;
    }
    void ApplyAttachmentColor()=>session.SetSelectedAttachmentColor(ReadSetupColor(attachmentColor));
    void ApplySlotColor()=>session.SetSelectedSlotColor(ReadSetupColor(slotColor));
    void ApplyVertexPosition()=>session.MoveSelectedVertex(new(double.Parse(vertexX.Text,CultureInfo.InvariantCulture),double.Parse(vertexY.Text,CultureInfo.InvariantCulture)));
    void ClearInspectorSelection()
    {if(hierarchy.SelectedItem is InspectorItem item)item.Selected=false;properties.Text="항목을 선택하면 설정 속성을 표시합니다.";}
    void EscapeSelection()
    {var dragging=session.IsDragging;surface.CancelVertexSelection();session.Escape();if(!dragging)ClearInspectorSelection();surface.ReleaseMouseCapture();}
    void Fit()
    {
        session.FinishDrag(true);session.Camera.Fit([session.Canvas.Origin,session.Canvas.Opposite],new(Math.Max(1,surface.ActualWidth),Math.Max(1,surface.ActualHeight)));
    }
    bool ConfirmDiscard()
    {
        if(!session.Modified)return true;
        var answer=MessageBox.Show(this,"변경 내용을 저장하시겠습니까?","미저장 편집",MessageBoxButton.YesNoCancel,MessageBoxImage.Question,MessageBoxResult.Cancel);
        if(answer==MessageBoxResult.No)return true;if(answer!=MessageBoxResult.Yes)return false;
        try{return Save(false);}catch(Exception e)when(EditorErrors.IsExpected(e)){MessageBox.Show(this,e.Message,"저장 실패");return false;}
    }
    bool Save(bool choose)
    {
        PausePlayback();
        session.FinishDrag(true);var target=savedPath;var digest=savedDigest;
        if(choose||target is null)
        {
            Directory.CreateDirectory(outputRoot);var binary=session.Editor.IsBinary;var dialog=new SaveFileDialog{Filter=binary?"Skeleton SKEL|*.skel":"Skeleton JSON|*.json",InitialDirectory=outputRoot,FileName=binary?$"edited-{DateTime.Now:yyyyMMdd-HHmmssfff}.skel":"edited.json",OverwritePrompt=true};
            if(dialog.ShowDialog(this)!=true)return false;target=dialog.FileName;Experiment.CheckOutput(target);digest=Editor.Digest(target);
        }
        if(session.Editor.IsBinary&&originalSkelPath is not null&&string.Equals(Path.GetFullPath(target),originalSkelPath,StringComparison.OrdinalIgnoreCase))
        {
            foreach(var source in atlas.Sources)if(Editor.Digest(source.Key)!=source.Value)throw new IOException("Source companion changed since open");
            session.Editor.SaveOriginalSkel(target,digest??throw new InvalidOperationException("Original SKEL digest required"));
        }
        else atlas=BundleSave.Save(session.Editor,target,atlas.SourcePath,outputRoot,digest,atlas.Sources);
        savedPath=target;savedDigest=session.Editor.IsBinary?session.Editor.BinaryDigest:Editor.Digest(target);Refresh();return true;
    }
    void ShowPng(string path)
    {
        var image=Texture.LoadPng(path);textures.SetAtlas(new(new(),new(){{Path.GetFileName(path),image}}));workspaceTabs.SelectedIndex=1;
    }
    void ShowAtlas(){textures.SetAtlas(atlas,session.Document,session.Skin);workspaceTabs.SelectedIndex=1;}
    void ShowAtlasFile(string path)
    {var next=TextureAtlas.Load(path);textures.SetAtlas(next,Path.GetFullPath(path).Equals(Path.GetFullPath(atlas.SourcePath),StringComparison.OrdinalIgnoreCase)?session.Document:null,session.Skin);workspaceTabs.SelectedIndex=1;}
    void OpenAtlasPreview()
    {var pick=new OpenFileDialog{Filter="Atlas|*.atlas",Title="Atlas 미리보기"};if(pick.ShowDialog(this)==true)ShowAtlasFile(pick.FileName);}
    void OpenTexture()
    {var pick=new OpenFileDialog{Filter="PNG|*.png",Title="PNG 미리보기"};if(pick.ShowDialog(this)==true)ShowPng(pick.FileName);}
    void Open()
    {
        session.FinishDrag(true);if(!ConfirmDiscard())return;
        var file=new OpenFileDialog{Filter="Skeleton JSON / SKEL|*.json;*.skel|JSON|*.json|SKEL (제한 3.8.55 / 4.1.11 / 4.1.17)|*.skel"};if(file.ShowDialog(this)!=true)return;
        var atlasFile=Path.ChangeExtension(file.FileName,".atlas");
        if(!File.Exists(atlasFile)){var pick=new OpenFileDialog{Filter="Atlas|*.atlas",Title="연결할 Atlas 선택"};if(pick.ShowDialog(this)!=true)return;atlasFile=pick.FileName;}
        LoadDocument(file.FileName,atlasFile);
    }
    void LoadDocument(string path,string atlasFile)
    {
        PausePlayback();
        var nextAtlas=TextureAtlas.Load(atlasFile);Action<Scene,CanvasCamera> validate=(scene,camera)=>{EditorHandles.ValidateView(scene,nextAtlas,camera,skia:nativeRenderer);};
        bool opened;
        if(string.Equals(Path.GetExtension(path),".skel",StringComparison.OrdinalIgnoreCase))
        {
            var document=BinaryDocument.Load(path);var setup= binarySetup4117||document.Header.Version is "4.1.11" or "4.1.17";
            opened=session.TryOpen(document.UnchangedBytes(),true,validate,rotation:binaryRotation&&!setup,setup4117:setup,preserveAnimations:preserveAnimations||setup&&!binarySetup4117);
        }
        else opened=session.TryOpen(Input.ReadText(path),true,validate);
        if(opened){itemView=ItemViewState.Empty;wireframes=ImmutableHashSet<string>.Empty;if(session.Editor.IsBinary)outputRoot=Path.Combine(Experiment.Root(),"output","manual-binary-"+Guid.NewGuid().ToString("N"));atlas=nextAtlas;originalSkelPath=savedPath=session.Editor.IsBinary?Path.GetFullPath(path):null;savedDigest=session.Editor.BinaryDigest;RefreshLists();Fit();}
    }
    void HandleKeyDown(object sender,KeyEventArgs e)
    {
        if(surface.IsKeyboardFocused&&e.KeyboardDevice.Modifiers==ModifierKeys.None&&e.Key is Key.J or Key.U){surface.NavigationKey(e.Key,true);e.Handled=true;return;}
        if(e.KeyboardDevice.Modifiers==(ModifierKeys.Control|ModifierKeys.Shift)&&e.Key is Key.F or Key.S){e.Handled=true;Guard(()=>{if(e.Key==Key.F)Fit();else Save(true);});return;}
        if(e.KeyboardDevice.Modifiers==ModifierKeys.Control)
        {
            Action? action=e.Key switch{Key.S=>()=>Save(false),Key.O=>Open,Key.F=>()=>session.Camera.ActualSize(),_=>null};
            if(action is not null){e.Handled=true;Guard(action);}
        }
        else if(e.Key==Key.Escape){e.Handled=true;Guard(EscapeSelection);}
        else if(surface.IsKeyboardFocused&&e.KeyboardDevice.Modifiers==ModifierKeys.None&&e.Key==Key.Delete)
        {e.Handled=true;Guard(()=>{if(session.SelectedKey is{} selected&&itemView.CanSelect(session.Document,selected)&&session.SelectedVertices.Count>0){PausePlayback();session.DeleteSelectedVertex(ValidateMeshView);}});}
        else if(surface.IsKeyboardFocused&&(e.KeyboardDevice.Modifiers is ModifierKeys.None or ModifierKeys.Shift)&&e.Key==Key.Tab)
        {
            Guard(()=>{var handles=SelectableHandles();var next=EditorHandles.Cycle(handles,session.SelectedKey,session.SelectedVertex,e.KeyboardDevice.Modifiers==ModifierKeys.Shift);if(next is null)return;e.Handled=true;ClearInspectorSelection();session.Select(next.Key,next.Index);});
        }
        else if(surface.IsKeyboardFocused&&(e.KeyboardDevice.Modifiers is ModifierKeys.None or ModifierKeys.Shift)&&e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            if(session.PoseReadOnly||playbackTimer.IsEnabled){e.Handled=true;return;}
            e.Handled=true;Guard(()=>
            {
                var handle=SelectableHandles().FirstOrDefault(h=>h.Key==session.SelectedKey&&h.Index==session.SelectedVertex);if(handle is null)return;
                var amount=e.KeyboardDevice.Modifiers==ModifierKeys.Shift?10:1;var delta=e.Key switch{Key.Left=>new Point(-amount,0),Key.Right=>new Point(amount,0),Key.Up=>new Point(0,-amount),_=>new Point(0,amount)};
                session.Nudge(handle.Screen,delta);
            });
        }
    }
    sealed partial class EditorSurface:FrameworkElement
    {
        static Pen FrozenPen(Brush brush,double width){var pen=new Pen(brush,width);pen.Freeze();return pen;}
        static readonly Pen AxisPen=FrozenPen(new SolidColorBrush(Color.FromRgb(0x53,0x56,0x5b)),1),BonePen=FrozenPen(Brushes.LightSkyBlue,1),SelectedPen=FrozenPen(Brushes.Orange,2),WirePen=FrozenPen(new SolidColorBrush(Color.FromArgb(0x99,0,0xdc,0xca)),1),HandlePen=FrozenPen(Brushes.Black,1);
        static readonly Pen HullPen=FrozenPen(Brushes.Turquoise,2),InteriorPen=FrozenPen(Brushes.Gray,1),SelectedInteriorPen=FrozenPen(Brushes.Gray,2);
        readonly EditorWindow owner;Point? pan,zoom,selectionStart,selectionEnd;string? selectionKey;Point previousZoom;bool panKey,zoomKey;
        internal void CancelVertexSelection(){selectionStart=selectionEnd=null;selectionKey=null;InvalidateVisual();}
        internal void BeginVertexSelection(Point point,bool additive)
        {
            owner.ClearInspectorSelection();
            var hit=owner.session.PoseReadOnly?null:EditorHandles.Hit(owner.SelectableHandles().Where(handle=>owner.showMeshes.IsChecked==true||handle.Key==owner.session.SelectedKey||owner.wireframes.Contains(handle.Key)),point,owner.session.SelectedKey,owner.session.SelectedVertex);
            if(additive&&!owner.session.PoseReadOnly)
            {
                selectionStart=selectionEnd=point;selectionKey=hit?.Key??(owner.session.SelectedKey is{} current&&owner.itemView.CanSelect(owner.session.Document,current)?current:null)??owner.SelectablePartAt(point);
                if(selectionKey is{} key&&Scene.Text(owner.session.Document.Meshes[key].Data["type"])!="mesh")selectionKey=null;
                CaptureMouse();return;
            }
            if(hit is null){var part=owner.SelectablePartAt(point);if(part is null&&owner.SelectBoneAt(point))return;owner.session.Select(part);return;}
            owner.session.SelectVertex(hit.Key,hit.Index);owner.session.BeginDrag(point);CaptureMouse();
        }
        internal void UpdateVertexSelection(Point point){selectionEnd=point;InvalidateVisual();}
        internal void CompleteVertexSelection(Point point)
        {
            if(selectionStart is not{} start)return;
            var key=selectionKey;CancelVertexSelection();if(key is null)return;
            var handles=owner.SelectableHandles().Where(h=>h.Key==key).ToArray();
            if((point-start).Length<4)
            {var hit=EditorHandles.Hit(handles,point,owner.session.SelectedKey,owner.session.SelectedVertex);if(hit is not null)owner.session.SelectVertex(key,hit.Index,toggle:true);}
            else
            {
                var box=new Rect(new System.Windows.Point(start.X,start.Y),new System.Windows.Point(point.X,point.Y));
                owner.session.SelectVertices(key,handles.Where(h=>box.Contains(new System.Windows.Point(h.Screen.X,h.Screen.Y))).Select(h=>h.Index),additive:true);
            }
        }
        internal void NavigationKey(Key key,bool pressed){if(key==Key.J)panKey=pressed;if(key==Key.U)zoomKey=pressed;}
        public EditorSurface(EditorWindow owner)
        {
            this.owner=owner;Focusable=true;ClipToBounds=true;AutomationProperties.SetName(this,"메시 편집 화면");
            LostKeyboardFocus+=(_,_)=>panKey=zoomKey=false;
            Unloaded+=(_,_)=>AbortZoom();
            owner.Closed+=(_,_)=>AbortZoom();
            SizeChanged+=(_,_)=>{if(ActualWidth>0&&ActualHeight>0)owner.Guard(()=>owner.session.Camera.Resize(new(ActualWidth,ActualHeight)));};
            MouseDown+=(_,e)=>owner.Guard(()=>
            {
                FinishZoom();Focus();var p=e.GetPosition(this);var point=new Point(p.X,p.Y);
                if(e.ChangedButton==MouseButton.Left&&(zoomKey||panKey&&Keyboard.Modifiers.HasFlag(ModifierKeys.Control))||e.ChangedButton==MouseButton.Right&&Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){owner.session.FinishDrag(true);zoom=previousZoom=point;CaptureMouse();return;}
                if(e.ChangedButton==MouseButton.Right||e.ChangedButton==MouseButton.Left&&(Keyboard.IsKeyDown(Key.Space)||panKey)){owner.session.FinishDrag(true);pan=point;CaptureMouse();return;}
                if(e.ChangedButton!=MouseButton.Left||owner.playbackTimer.IsEnabled)return;
                var additive=Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                if(owner.insertHullVertex.IsChecked==true&&!additive&&owner.session.SelectedKey is{} hullKey&&owner.itemView.CanSelect(owner.session.Document,hullKey)){var edge=owner.SelectedHullEdge();owner.session.AddHullVertex(edge.A,edge.B,owner.session.Camera.ToWorld(point),owner.ValidateMeshView);return;}
                if(owner.insertVertex.IsChecked==true&&!additive&&owner.session.SelectedKey is{} insertKey&&owner.itemView.CanSelect(owner.session.Document,insertKey)){owner.session.AddMeshVertex(owner.session.Camera.ToWorld(point),owner.ValidateMeshView);return;}
                BeginVertexSelection(point,additive);
            });
            MouseMove+=(_,e)=>
            {
                if(!IsMouseCaptured)return;owner.Guard(()=>{var p=e.GetPosition(this);var point=new Point(p.X,p.Y);if(zoom is Point anchor){NavigateZoom(anchor,(previousZoom.Y-point.Y)*.01/Math.Log(1.15));previousZoom=point;}else if(pan is Point previous){owner.session.Camera.Pan(point-previous);pan=point;}else if(selectionStart is not null)UpdateVertexSelection(point);else if(owner.session.IsDragging)owner.session.DragTo(point);});
            };
            MouseUp+=(_,e)=>owner.Guard(()=>{if(selectionStart is not null){var p=e.GetPosition(this);CompleteVertexSelection(new(p.X,p.Y));}pan=zoom=null;owner.session.FinishDrag(true);ReleaseMouseCapture();});
            LostMouseCapture+=(_,_)=>owner.Guard(()=>{CancelVertexSelection();pan=zoom=null;owner.session.FinishDrag(true);});
            MouseWheel+=(_,e)=>owner.Guard(()=>{var p=e.GetPosition(this);NavigateZoom(new(p.X,p.Y),e.Delta/120d);});
        }
        protected override void OnRender(DrawingContext drawing)
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x25,0x27,0x2b)),null,new Rect(RenderSize));
            if(ActualWidth<1||ActualHeight<1)return;
            try
            {
                var frame=owner.session.Canvas;var origin=owner.session.Camera.Matrix.Apply(frame.Origin);var axis=AxisPen;
                if(origin.X>=0&&origin.X<=ActualWidth)drawing.DrawLine(axis,new System.Windows.Point(origin.X,0),new System.Windows.Point(origin.X,ActualHeight));
                if(origin.Y>=0&&origin.Y<=ActualHeight)drawing.DrawLine(axis,new System.Windows.Point(0,origin.Y),new System.Windows.Point(ActualWidth,origin.Y));
                var resolution=Math.Min(1,Math.Sqrt(1_000_000/(ActualWidth*ActualHeight)));var width=Math.Max(1,(int)(ActualWidth*resolution));var height=Math.Max(1,(int)(ActualHeight*resolution));
                var parts=FrameParts(out var pose);
                var matrix=new Affine(width/ActualWidth,0,0,height/ActualHeight,0,0)*owner.session.Camera.Matrix;
                var destination=new Rect(0,0,ActualWidth,ActualHeight);var camera=owner.session.Camera;
                if(owner.crispPixels.IsChecked==true&&camera.Scale>1&&(Math.Ceiling(ActualWidth/camera.Scale)+2)*(Math.Ceiling(ActualHeight/camera.Scale)+2)<=1_000_000)
                {
                    var corner=camera.ToWorld(new());var left=frame.Origin.X+Math.Floor(corner.X-frame.Origin.X);var top=frame.Origin.Y-Math.Floor(frame.Origin.Y-corner.Y);width=(int)Math.Ceiling(ActualWidth/camera.Scale)+2;height=(int)Math.Ceiling(ActualHeight/camera.Scale)+2;matrix=new(1,0,0,-1,-left,top);var screen=camera.Matrix.Apply(new(left,top));destination=new(screen.X,screen.Y,width*camera.Scale,height*camera.Scale);
                }
                var opposite=camera.Matrix.Apply(frame.Opposite);var canvas=new Rect(new System.Windows.Point(origin.X,origin.Y),new System.Windows.Point(opposite.X,opposite.Y));var bounds=Rect.Intersect(canvas,new Rect(RenderSize));var pixelOrigin=origin;
                if(owner.transparency.IsChecked==true)PixelDisplay.Background(drawing,bounds,pixelOrigin,camera.Scale);
                var previewZoom=PrepareAsyncZoom(parts,pose,width,height,matrix,destination)||CanPreviewZoom(parts);if(previewZoom)DrawZoomFrame(drawing);else DrawFrame(drawing,parts,width,height,matrix,destination);
                if(owner.pixelGrid.IsChecked==true)PixelDisplay.Grid(drawing,bounds,pixelOrigin,camera.Scale);
                drawing.DrawRectangle(null,axis,canvas);
                var caption=new FormattedText($"(0,0) · {frame.Width:0} × {frame.Height:0} px",System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);
                drawing.DrawText(caption,new System.Windows.Point(origin.X,origin.Y-caption.Height-4));
                if(previewZoom)DrawZoomOverlay(drawing);else DrawOverlay(drawing,parts,pose);
                if(selectionStart is{} start&&selectionEnd is{} end)drawing.DrawRectangle(new SolidColorBrush(Color.FromArgb(35,0,190,255)),WirePen,new Rect(new System.Windows.Point(start.X,start.Y),new System.Windows.Point(end.X,end.Y)));
            }
            catch(Exception e)when(EditorErrors.IsExpected(e)){owner.status.Text=e.Message;}
        }
        void BuildOverlay(DrawingContext drawing,List<RenderPart> parts,Affine[] pose,VertexHandle[] handles,Rect? region=null)
            =>PaintOverlay(parts,pose,handles,(a,b,pen)=>drawing.DrawLine(pen,new(a.X,a.Y),new(b.X,b.Y)),(p,r,brush,pen)=>drawing.DrawEllipse(brush,pen,new(p.X,p.Y),r,r),region);
        void PaintOverlay(List<RenderPart> parts,Affine[] pose,VertexHandle[] handles,Action<Point,Point,Pen> segment,Action<Point,double,Brush,Pen?> circle,Rect? region=null)
            =>PaintOverlay(new OverlayContext(owner.session.Document,owner.session.Skin,owner.session.Animation,owner.session.Time,parts,pose,owner.session.Camera.Matrix,OverlayState(),handles),segment,circle,region);
        static void PaintOverlay(OverlayContext context,Action<Point,Point,Pen> segment,Action<Point,double,Brush,Pen?> circle,Rect? region=null)
        {
                bool Touches(Point a,Point b,double padding){if(region is null)return true;var bounds=new Rect(new System.Windows.Point(a.X,a.Y),new System.Windows.Point(b.X,b.Y));bounds.Inflate(padding,padding);return bounds.IntersectsWith(region.Value);}
                if(context.State.Bones)
                {
                    var bone=BonePen;var selectedBone=SelectedPen;var selected=context.State.Bone;
                    foreach(var line in EditorHandles.BoneLines(context.Scene,context.Skin,context.Pose,selected,context.Camera,context.State.View.Hidden))
                    {
                        if(!Touches(line.Start,line.End,6))continue;var pen=line.Selected?selectedBone:bone;
                        var delta=line.End-line.Start;var length=delta.Length;
                        if(length>0)
                        {
                            var shoulder=line.Start+delta*.2;var width=Math.Min(5,length*.15);
                            var normal=new Point(-delta.Y/length,delta.X/length)*width;
                            var left=shoulder+normal;var right=shoulder-normal;
                            segment(line.Start,left,pen);segment(left,line.End,pen);
                            segment(line.End,right,pen);segment(right,line.Start,pen);
                        }
                        else segment(line.Start,line.End,pen);
                        circle(line.Start,2.5,pen.Brush,null);
                    }
                }
                if(context.State.Meshes||context.State.Key is not null||context.State.Wireframes.Count>0)
                {
                    foreach(var line in EditorHandles.MeshLines(context.Scene,context.Skin,context.State.Key,context.Parts,context.Camera,context.State.Meshes?null:context.State.Wireframes))
                    {if(!Touches(line.Start,line.End,2))continue;segment(line.Start,line.End,line.Hull?(line.Selected?HullPen:WirePen):(line.Selected?SelectedInteriorPen:InteriorPen));}
                }
                if(context.State.Handles)
                {
                    var selected=new List<VertexHandle>();
                    foreach(var handle in context.Handles??EditorHandles.Visible(context.Scene,context.Skin,context.Parts,context.Pose,context.Camera,context.Animation,context.Time))
                    {
                        if(!context.State.Meshes&&handle.Key!=context.State.Key&&!context.State.Wireframes.Contains(handle.Key))continue;
                        if(!Touches(handle.Screen,handle.Screen,7))continue;
                        if(handle.Key==context.State.Key&&context.State.Vertices.Contains(handle.Index)){selected.Add(handle);continue;}
                        circle(handle.Screen,3,Brushes.White,HandlePen);
                    }
                    foreach(var handle in selected)circle(handle.Screen,5,Brushes.Yellow,HandlePen);
                }
        }
    }
}


