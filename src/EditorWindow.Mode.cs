using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
namespace IndependentDev;

internal sealed partial class EditorWindow
{
    readonly ComboBox editorMode=new(){MinWidth=100,ItemsSource=new[]{"설정","애니메이션"},SelectedIndex=0};
    readonly Grid timeline=new(){Visibility=Visibility.Collapsed};
    readonly GridSplitter timeSplit=new(){Height=5,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,Background=Brushes.Transparent,ResizeDirection=GridResizeDirection.Rows,ResizeBehavior=GridResizeBehavior.PreviousAndNext,Focusable=true,Visibility=Visibility.Collapsed};
    readonly RowDefinition timelineRow=new(){Height=new GridLength(0)};
    readonly RowDefinition timelineSplitRow=new(){Height=new GridLength(0)};
    GridLength timelineHeight=new(150);
    bool animationMode;
    string? lastAnimation;
    double lastAnimationTime;

    void ConfigureEditorMode()
    {
        AutomationProperties.SetName(editorMode,"편집 모드");
        editorMode.SelectionChanged+=(_,_)=>{if(!refreshing)Guard(()=>SetEditorMode(editorMode.SelectedIndex==1));};
    }
    void SetEditorMode(bool animate)
    {
        if(animate==animationMode)return;
        PausePlayback();session.FinishDrag(true);surface.ReleaseMouseCapture();
        var name=animate?animations.Items.Cast<AnimationChoice>().FirstOrDefault(choice=>choice.Name is not null&&choice.Name==lastAnimation)?.Name
            ??animations.Items.Cast<AnimationChoice>().FirstOrDefault(choice=>choice.Name is not null)?.Name:null;
        var at=animate&&name is not null&&name==lastAnimation?lastAnimationTime:0;
        EditorHandles.ValidateView(session.Document,atlas,session.Camera,session.Skin,name,at,nativeRenderer);
        if(!animate){lastAnimation=session.Animation;lastAnimationTime=session.Time;}
        session.SetPose(name,at,false);animationMode=animate;
    }
    void RefreshEditorMode()
    {
        var previous=refreshing;refreshing=true;
        try{editorMode.SelectedIndex=animationMode?1:0;}finally{refreshing=previous;}
        if(animationMode==(timeline.Visibility==Visibility.Visible))return;
        if(!animationMode)timelineHeight=timelineRow.Height;
        timelineRow.MinHeight=animationMode?80:0;
        timelineRow.Height=animationMode?timelineHeight:new GridLength(0);
        timelineSplitRow.Height=new GridLength(animationMode?5:0);
        timeline.Visibility=timeSplit.Visibility=animationMode?Visibility.Visible:Visibility.Collapsed;
    }
}
