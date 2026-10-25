using System.Globalization;
using System.Text.Json.Nodes;
namespace IndependentDev;

internal readonly record struct Rgba(double R,double G,double B,double A)
{
    public static readonly Rgba White=new(1,1,1,1);
    public static Rgba Parse(string text)
    {
        if(text.Length is not (6 or 8)||!uint.TryParse(text,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out var value))throw new InvalidDataException("Color RGBA");
        if(text.Length==6)value=(value<<8)|255;
        return new((value>>24)/255d,((value>>16)&255)/255d,((value>>8)&255)/255d,(value&255)/255d);
    }
    public void Validate() { static bool Unit(double v)=>double.IsFinite(v)&&v>=0&&v<=1;if(!Unit(R)||!Unit(G)||!Unit(B)||!Unit(A))throw new InvalidDataException("Color range"); }
    public static Rgba operator *(Rgba a,Rgba b)=>new(a.R*b.R,a.G*b.G,a.B*b.B,a.A*b.A);
    public static Rgba Lerp(Rgba a,Rgba b,double t)=>new(Math.Clamp(a.R+(b.R-a.R)*t,0,1),Math.Clamp(a.G+(b.G-a.G)*t,0,1),Math.Clamp(a.B+(b.B-a.B)*t,0,1),Math.Clamp(a.A+(b.A-a.A)*t,0,1));
}

internal static class ColorTimeline
{
    public static Rgba Sample(JsonArray? keys,double time,Rgba setup,string field="color",bool absolute=false)
    {
        if(!double.IsFinite(time)||time<0)throw new ArgumentOutOfRangeException(nameof(time));
        if(keys is null||keys.Count==0)return setup;
        double previous=-1;var index=-1;
        for(var i=0;i<keys.Count;i++)
        {
            var frame=Scene.Object(keys[i])??throw new InvalidDataException("Color frame object");
            if(frame.Any(p=>p.Key is not ("time" or "curve" or "c2" or "c3" or "c4")&&(field=="color"?p.Key!="color":p.Key is not ("light" or "dark"))))throw new NotSupportedException("Color key schema");
            var at=Scene.Number(frame["time"]);if(at<0||at<=previous)throw new InvalidDataException("Color timeline order");previous=at;if(at<=time)index++;
            if(frame[field] is null)throw new InvalidDataException("Color key value");Read(frame);
            if(absolute&&frame["curve"] is JsonArray controls)
            {
                if(controls.Count!=16||controls.Any(c=>c is null)||i+1==keys.Count)throw new InvalidDataException("Absolute color curve requires16 controls and following key");var values=Scene.Values(controls);var end=Scene.Number(keys[i+1]?["time"]);if(end<=at)throw new InvalidDataException("Color timeline order");
                for(var c=0;c<4;c++)if(values[c*4]<at||values[c*4]>end||values[c*4+2]<at||values[c*4+2]>end||values[c*4+1] is <0 or >1||values[c*4+3] is <0 or >1)throw new InvalidDataException("Absolute color curve controls");
            }
            else Timeline.Fraction(Timeline.NormalizedCurve(frame),0);
        }
        if(index<0)return setup;
        var left=keys[index]!.AsObject();var a=Read(left);if(index==keys.Count-1)return a;
        var right=keys[index+1]!.AsObject();var b=Read(right);
        if(absolute&&left["curve"] is JsonArray curve)
        {
            var controls=Scene.Values(curve);double Channel(double from,double to,int offset)=>Math.Clamp(Curves.Absolute(time,Scene.Number(left["time"]),Scene.Number(right["time"]),from,to,controls,offset),0,1);
            return new(Channel(a.R,b.R,0),Channel(a.G,b.G,4),Channel(a.B,b.B,8),Channel(a.A,b.A,12));
        }
        var ratio=Timeline.Fraction(Timeline.NormalizedCurve(left),(time-Scene.Number(left["time"]))/ (Scene.Number(right["time"])-Scene.Number(left["time"])));
        return Rgba.Lerp(a,b,ratio);
        Rgba Read(JsonObject frame){var text=Scene.Text(frame[field]);if(field=="dark"&&text.Length!=6)throw new InvalidDataException("Slot dark RGB");return Rgba.Parse(text);}
    }
    public static (Rgba Light,Rgba? Dark) Slot(Scene scene,Slot slot,string? animation,double time)
    {
        var source=slot.Data;
        var setup=Rgba.Parse(Scene.Text(source["color"],"ffffffff"));
        Rgba? dark=source["dark"] is null?null:Rgba.Parse(Scene.Text(source["dark"]));
        var channel=animation is null?null:Scene.Object(scene.Data["animations"]?[animation]?["slots"]?[slot.Name]);
        if(channel is not null&&channel.Any(p=>p.Key switch{"attachment" or "color"=>false,"twoColor"=>!scene.Version.StartsWith("3.8."),"rgba"=>scene.Version is not("4.1.11" or "4.1.17"),_=>true}))throw new NotSupportedException("Slot color channel");
        var colors=Scene.Array(channel?["color"]);var two=Scene.Array(channel?["twoColor"]);var rgba=Scene.Array(channel?["rgba"]);
        if(rgba is not null)
        {
            if(colors is not null||two is not null)throw new InvalidDataException("Conflicting slot color timelines");
            foreach(var node in rgba)
            {
                var frame=Scene.Object(node)??throw new InvalidDataException("Color frame object");
                if(frame.Any(p=>p.Key is not("time" or "color" or "curve")))throw new NotSupportedException("4.1.17 rgba key schema");
                if(frame["color"] is not JsonValue color||!color.TryGetValue<string>(out var text)||text is not{Length:8})throw new InvalidDataException("4.1.17 rgba requires RGBA color");
                if(frame["curve"] is{} curve&&!(curve is JsonArray{Count:16}||curve is JsonValue value&&value.TryGetValue<string>(out var interpolation)&&interpolation=="stepped"))throw new NotSupportedException("4.1.17 rgba component curves pending");
            }
            return(Sample(rgba,time,setup,absolute:true),dark);
        }
        if(two is not null)
        {
            if(dark is null||colors is not null)throw new InvalidDataException("twoColor requires setup dark and no color timeline");
            return(Sample(two,time,setup,"light"),Sample(two,time,dark.Value,"dark"));
        }
        return(Sample(colors,time,setup),dark);
    }
}
