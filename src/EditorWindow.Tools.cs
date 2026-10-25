using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
namespace IndependentDev;

internal sealed partial class EditorWindow
{
    void ConfigurePropertiesPanel(Grid inspector)
    {
        ConfigureMeshTools();meshHelp.Margin=new Thickness(12);
        meshTools.Margin=vertexTools.Margin=meshVertexTools.Margin=new Thickness(0,8,0,8);
        var editContent=new StackPanel();editContent.Children.Add(vertexTools);editContent.Children.Add(meshVertexTools);meshEdit.Content=editContent;
        meshParts.Content=meshTools;
        AutomationProperties.SetName(meshEdit,"선택 파츠 메시 편집");AutomationProperties.SetName(meshParts,"선택 슬롯 또는 메시 파츠 관리");
        meshEdit.Collapsed+=(_,_)=>{insertVertex.IsChecked=false;insertHullVertex.IsChecked=false;};
        meshEdit.Expanded+=(_,_)=>Guard(()=>{PausePlayback();session.FinishDrag(true);surface.ReleaseMouseCapture();});
        inspector.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star),MinHeight=80});
        inspector.RowDefinitions.Add(new(){Height=new GridLength(5)});
        inspector.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star),MinHeight=80});
        var content=new StackPanel();foreach(var control in new UIElement[]{properties,meshEdit,meshParts,meshHelp,slotAttachmentTools,slotColorTools,colorTools,meshWireframe})content.Children.Add(control);
        var details=new DockPanel();var title=new TextBlock{Text="속성",Margin=new Thickness(12,8,12,8),FontWeight=FontWeights.SemiBold};DockPanel.SetDock(title,Dock.Top);details.Children.Add(title);
        var scroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};AutomationProperties.SetName(scroll,"선택 항목 속성");details.Children.Add(scroll);Grid.SetRow(details,2);inspector.Children.Add(details);
        var split=new GridSplitter{Height=5,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,Background=Brushes.Transparent,ResizeDirection=GridResizeDirection.Rows,ResizeBehavior=GridResizeBehavior.PreviousAndNext,Focusable=true};Grid.SetRow(split,1);AutomationProperties.SetName(split,"계층과 속성 높이 조절");inspector.Children.Add(split);
    }
}
