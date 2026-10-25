using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
namespace IndependentDev;

internal sealed partial class EditorWindow
{
    readonly Button play=new(){Content="재생",Margin=new Thickness(7,0,0,0)};
    readonly Button rewind=new(){Content="처음",Margin=new Thickness(7,0,0,0)};
    readonly CheckBox repeat=new(){Content="반복",IsChecked=true,Margin=new Thickness(7,0,0,0),VerticalAlignment=VerticalAlignment.Center};
    readonly Slider timeSlider=new(){Minimum=0,Maximum=1,Width=280,Margin=new Thickness(7,5,7,0),VerticalAlignment=VerticalAlignment.Center};
    readonly TextBlock durationLabel=new(){VerticalAlignment=VerticalAlignment.Center};
    readonly DispatcherTimer playbackTimer=new(DispatcherPriority.Background){Interval=TimeSpan.FromSeconds(1d/30)};
    readonly Stopwatch playbackClock=new();
    readonly Dictionary<string,double> durations=new(StringComparer.Ordinal);
    JsonNode? durationAnimations;
    double playbackOrigin;
    double AnimationDuration=>session.Animation is{} name&&durations.TryGetValue(name,out var value)?Math.Min(value,3600):0;
    void ConfigurePlayback(Panel tools)
    {
        foreach(var control in new FrameworkElement[]{play,rewind,repeat,timeSlider,durationLabel})tools.Children.Add(control);
        AutomationProperties.SetName(play,"애니메이션 재생 또는 일시정지");AutomationProperties.SetName(rewind,"애니메이션 처음으로");AutomationProperties.SetName(repeat,"애니메이션 반복");AutomationProperties.SetName(timeSlider,"애니메이션 시각 이동");
        play.Click+=(_,_)=>Guard(()=>{if(playbackTimer.IsEnabled)PausePlayback();else StartPlayback();});
        time.GotKeyboardFocus+=(_,_)=>PausePlayback();
        rewind.Click+=(_,_)=>Guard(()=>{PausePlayback();session.SetPose(session.Animation,0,false);UpdatePlayTime();});
        timeSlider.ValueChanged+=(_,_)=>{if(!refreshing&&session.Animation is not null)Guard(()=>{PausePlayback();session.SetPose(session.Animation,timeSlider.Value,session.DeformMode);UpdatePlayTime();});};
        playbackTimer.Tick+=(_,_)=>
        {
            try{AdvancePlayback(playbackClock.Elapsed.TotalSeconds);}
            catch(Exception error)when(EditorErrors.IsExpected(error)){PausePlayback();status.Text="재생 중단: "+error.Message;}
        };
        Closed+=(_,_)=>PausePlayback();
    }
    void RefreshPlaybackControls()
    {
        var animations=session.Document.Data["animations"];
        if(!ReferenceEquals(durationAnimations,animations))
        {
            durations.Clear();foreach(var entry in Scene.Object(session.Document.Data["animations"])??[])
            {
                double Duration(JsonNode? node)=>node switch{JsonObject obj=>Math.Max(Scene.Number(obj["time"]),obj.Where(p=>p.Key!="time").Select(p=>Duration(p.Value)).DefaultIfEmpty().Max()),JsonArray array=>array.Select(Duration).DefaultIfEmpty().Max(),_=>0};
                durations.Add(entry.Key,Duration(entry.Value));
            }
            durationAnimations=animations;
        }
        var duration=AnimationDuration;play.IsEnabled=duration>0;rewind.IsEnabled=session.Animation is not null;timeSlider.IsEnabled=duration>0;
        var previous=refreshing;refreshing=true;try{timeSlider.Maximum=Math.Max(duration,double.Epsilon);timeSlider.Value=Math.Min(session.Time,timeSlider.Maximum);}finally{refreshing=previous;}
        durationLabel.Text=$" / {duration:0.###} 초";
    }
    void StartPlayback()
    {
        var duration=AnimationDuration;if(session.Animation is null||duration<=0)return;session.FinishDrag(true);
        session.SetPose(session.Animation,session.Time>=duration?0:session.Time,false);
        playbackOrigin=session.Time;playbackClock.Restart();playbackTimer.Start();play.Content="일시정지";
    }
    void PausePlayback(){playbackTimer.Stop();playbackClock.Stop();play.Content="재생";}
    void AdvancePlayback(double elapsed)
    {
        if(!double.IsFinite(elapsed)||elapsed<0)throw new InvalidDataException("Playback elapsed time");
        var duration=AnimationDuration;if(session.Animation is null||duration<=0){PausePlayback();return;}
        var value=playbackOrigin+elapsed;
        if(value>=duration){if(repeat.IsChecked==true)value%=duration;else{value=duration;PausePlayback();}}
        session.SetPose(session.Animation,value,false);UpdatePlayTime();surface.InvalidateVisual();
    }
    void UpdatePlayTime()
    {
        var previous=refreshing;refreshing=true;try{time.Text=session.Time.ToString("G9",System.Globalization.CultureInfo.InvariantCulture);timeSlider.Value=Math.Min(session.Time,timeSlider.Maximum);}finally{refreshing=previous;}
    }
}
