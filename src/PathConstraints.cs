using System.Text.Json.Nodes;
namespace IndependentDev;

internal sealed class BezierPath
{
    readonly Point[][] curves;
    readonly double[] lengths;
    readonly bool closed,constantSpeed;
    readonly Action<int> consume;
    public double Length=>lengths[^1];
    public static int CurveCount(int points,bool closed)
    {
        if(points%3!=0||points<(closed?3:6))throw new InvalidDataException("Path knot layout");
        return points/3-(closed?0:1);
    }
    public static void ValidateSetupLengths(double[] values,int count)
    {
        if(values.Length<count)throw new InvalidDataException("Path setup lengths");
        double total=0;
        for(var i=0;i<count;i++){var next=values[i];if(!double.IsFinite(next)||next<total)throw new InvalidDataException("Path setup lengths");total=next;}
        if(total<=0)throw new InvalidDataException("Zero length path");
    }
    public BezierPath(Point[] triplets,bool closed,bool constantSpeed,double[] setupLengths,Action<int> consume)
    {
        var count=CurveCount(triplets.Length,closed);
        foreach(var point in triplets)point.Validate();
        this.closed=closed;this.constantSpeed=constantSpeed;this.consume=consume;
        curves=new Point[count][];lengths=new double[count];
        if(!constantSpeed)ValidateSetupLengths(setupLengths,count);
        double total=0;
        for(var i=0;i<count;i++)
        {
            var start=1+i*3;curves[i]=Enumerable.Range(0,4).Select(k=>triplets[(start+k)%triplets.Length]).ToArray();
            if(constantSpeed)total+=Integral(i,1);
            else total=setupLengths[i];
            if(!double.IsFinite(total))throw new InvalidDataException("Path numeric range");lengths[i]=total;
        }
        if(total<=0)throw new InvalidDataException("Zero length path");
    }
    (Point Position,Point Tangent) At(int curve,double t)
    {var p=curves[curve];return Curves.Cubic(p[0],p[1],p[2],p[3],t);}
    double Integral(int curve,double end)
    {
        if(end==0)return 0;
        double Speed(double t){consume(1);var speed=At(curve,t).Tangent.Length;if(!double.IsFinite(speed))throw new InvalidDataException("Path numeric range");return speed;}
        var p=curves[curve];var polygon=(p[1]-p[0]).Length+(p[2]-p[1]).Length+(p[3]-p[2]).Length;
        if(!double.IsFinite(polygon))throw new InvalidDataException("Path numeric range");
        static double Simpson(double width,double a,double m,double b)
        {
            var weighted=a+4*m+b;
            return double.IsFinite(weighted)?width*weighted/6:width*(a/6+m*(2d/3)+b/6);
        }
        double Integrate(double lo,double hi,double a,double m,double b,double whole,double epsilon,int depth)
        {
            var mid=(lo+hi)/2;var l=Speed((lo+mid)/2);var r=Speed((mid+hi)/2);
            var left=Simpson(mid-lo,a,l,m);var right=Simpson(hi-mid,m,r,b);var difference=left+right-whole;
            if(!double.IsFinite(difference))throw new InvalidDataException("Path numeric range");
            if(Math.Abs(difference)<=15*epsilon)return left+right+difference/15;
            if(depth==0)throw new InvalidDataException("Path integration convergence");
            return Integrate(lo,mid,a,l,m,left,epsilon/2,depth-1)+Integrate(mid,hi,m,r,b,right,epsilon/2,depth-1);
        }
        var a=Speed(0);var m=Speed(end/2);var b=Speed(end);
        return Integrate(0,end,a,m,b,Simpson(end,a,m,b),1e-10+1e-9*polygon,20);
    }
    (Point Position,Point Tangent) Directed(int curve,double t)
    {
        var result=At(curve,t);var tangent=result.Tangent;
        if(tangent.Length==0)
        {
            if(t==0||t==1)
            {
                var p=curves[curve];for(var k=1;k<4&&tangent.Length==0;k++)tangent=t==0?p[k]-p[0]:p[3]-p[3-k];
            }
            else
            {
                var p=curves[curve];var ab=p[1]-p[0];var bc=p[2]-p[1];var cd=p[3]-p[2];var left=bc-ab;var right=cd-bc;
                tangent=new(Curves.Interpolate(left.X,right.X,t),Curves.Interpolate(left.Y,right.Y,t));
                if(tangent.Length==0)tangent=right-left;
                // shortcut: unrepresentable higher differences retain sampled fallback; use wider direction math for a reproduced need.
                if(!double.IsFinite(tangent.Length))
                {
                    tangent=At(curve,Math.Min(1,t+1e-5)).Position-result.Position;
                    if(tangent.Length==0)tangent=result.Position-At(curve,Math.Max(0,t-1e-5)).Position;
                }
            }
        }
        var length=tangent.Length;if(length==0)throw new InvalidDataException("Undefined path tangent");
        if(!double.IsFinite(length))throw new InvalidDataException("Path numeric range");
        return(result.Position,new(tangent.X/length,tangent.Y/length));
    }
    public (Point Position,Point Tangent) Sample(double distance)
    {
        if(!double.IsFinite(distance))throw new InvalidDataException("Path distance");
        if(closed){distance%=Length;if(distance<0)distance+=Length;}
        else if(distance<0){var first=Directed(0,0);var point=first.Position+first.Tangent*distance;point.Validate();return(point,first.Tangent);}
        else if(distance>Length){var last=Directed(curves.Length-1,1);var point=last.Position+last.Tangent*(distance-Length);point.Validate();return(point,last.Tangent);}
        var curve=0;while(curve+1<lengths.Length&&lengths[curve]<=distance)curve++;
        var start=curve==0?0:lengths[curve-1];var length=lengths[curve]-start;var wanted=distance-start;
        if(length<=0)throw new InvalidDataException("Zero path span");
        double t;
        if(wanted<=0)t=0;else if(wanted>=length)t=1;
        else if(!constantSpeed)t=wanted/length;
        else {double lo=0,hi=1;for(var i=0;i<44;i++){var mid=(lo+hi)/2;if(Integral(curve,mid)<wanted)lo=mid;else hi=mid;}t=(lo+hi)/2;}
        return Directed(curve,t);
    }
}

internal static partial class Constraints
{
    static void ApplyPath(Scene scene,JsonObject c,int[] bones,JsonObject? channels,LocalPose[] locals,Affine[] world,string? animation,double time,string skin,Action<int> consume,Action rebuild,bool enabled,bool[] active)
    {
        var absolute=scene.Version is "4.1.11" or "4.1.17";var modernMix=c["mixRotate"] is not null||c["mixX"] is not null||c["mixY"] is not null;
        if(channels is not null&&channels.Any(p=>p.Key is not ("position" or "spacing" or "mix")))throw new NotSupportedException("Path timeline channel");
        foreach(var channel in channels??[])
        {
            var keys=Scene.Array(channel.Value)??throw new InvalidDataException("Path timeline array");
            foreach(var node in keys)
            {
                var key=Scene.Object(node)??throw new InvalidDataException("Path frame object");
                var allowed=channel.Key=="mix"?new[]{"time","curve"}.Concat(modernMix?["mixRotate","mixX","mixY"]:["rotateMix","translateMix"]).ToArray():new[]{"time","curve",channel.Key};
                if(key.Any(p=>!allowed.Contains(p.Key,StringComparer.Ordinal)))throw new NotSupportedException("Path key schema");
                if(channel.Key=="mix"){if(key["rotateMix"] is not null)Ratio(Scene.Number(key["rotateMix"]));if(key["translateMix"] is not null)Ratio(Scene.Number(key["translateMix"]));}
                if(channel.Key=="mix"&&modernMix)foreach(var field in new[]{"mixRotate","mixX","mixY"})Ratio(Scene.Number(key[field],1));
                if(absolute&&key["curve"] is JsonArray curve){if(ReferenceEquals(node,keys[^1]))throw new InvalidDataException("Path curve requires a following key");var controls=Scene.Values(curve);var count=channel.Key=="mix"?(modernMix?3:2):1;if(controls.Length!=4*count)throw new InvalidDataException("Path curve components");if(channel.Key=="mix")for(var i=0;i<count;i++){Ratio(controls[i*4+1]);Ratio(controls[i*4+3]);}}
            }
        }
        var position=SampleConstraint(Scene.Array(channels?["position"]),time,"position",Scene.Number(c["position"]),0,absolute,0,1);
        var spacing=SampleConstraint(Scene.Array(channels?["spacing"]),time,"spacing",Scene.Number(c["spacing"]),0,absolute,0,1);
        double Mix(string field,int component)=>Ratio(SampleConstraint(Scene.Array(channels?["mix"]),time,field,Ratio(Scene.Number(c[field],1)),1,absolute,component,modernMix?3:2));
        var rotate=Mix(modernMix?"mixRotate":"rotateMix",0);var translate=Mix(modernMix?"mixX":"translateMix",1);var translateY=modernMix?Mix("mixY",2):translate;
        var positionMode=Scene.Text(c["positionMode"],"percent");var spacingMode=Scene.Text(c["spacingMode"],"length");var mode=Scene.Text(c["rotateMode"],"tangent");var rotation=Scene.Number(c["rotation"]);
        if(positionMode is not ("fixed" or "percent")||spacingMode is not ("length" or "fixed" or "percent")||mode is not ("tangent" or "chain" or "chainScale"))throw new NotSupportedException("Path mode");
        var slot=scene.Slots.FirstOrDefault(s=>s.Name==Scene.Text(c["target"]))??throw new InvalidDataException("Path target slot reference");
        if(!enabled||!active[slot.Bone]||bones.All(b=>!active[b]))return;
        bones=bones.Where(b=>active[b]).ToArray();
        var name=Playback.Attachment(scene,slot,animation,time);if(name is null)return;
        if(!scene.TryMesh(skin,slot.Name,name,out var mesh))throw new InvalidDataException("Path attachment reference");
        if(Scene.Text(mesh.Data["type"])!="path")return;
        if(mesh.Weights.Any(weights=>weights.Any(w=>!active[w.Bone])))throw new InvalidDataException("Path uses inactive skin bone");
        var points=scene.Vertices(mesh,world,Playback.Deform(scene,mesh,animation,time)).Select(v=>v.Position).ToArray();
        if(rotate==0&&translate==0&&translateY==0)return;
        var path=new BezierPath(points,Flag(mesh.Data["closed"]),Flag(mesh.Data["constantSpeed"],true),Scene.Values(mesh.Data["lengths"]),consume);
        var distance=positionMode=="percent"?position*path.Length:position;
        foreach(var bone in bones)
        {
            var at=path.Sample(distance);var original=world[bone];var length=scene.Bones[bone].Length*double.Hypot(original.A,original.C);
            var interval=spacingMode switch{"percent"=>spacing*path.Length,"length"=>length+spacing,_=>spacing};
            var next=path.Sample(distance+interval);var direction=mode=="tangent"?at.Tangent:next.Position-at.Position;
            if(direction.Length==0)direction=at.Tangent;
            var delta=Math.IEEERemainder(Affine.DegreeSum(Affine.DegreeSum(Aim(new(),direction),rotation),-Math.Atan2(original.C,original.A)*180/Math.PI),360)*rotate;
            var linear=original with{X=0,Y=0};
            if(mode=="chainScale"&&rotate!=0)
            {
                if(length==0)throw new InvalidDataException("Path chain zero length");var factor=1+((next.Position-at.Position).Length/length-1)*rotate;
                linear=linear with{A=linear.A*factor,C=linear.C*factor};
            }
            linear=Affine.Local(0,0,delta,1,1,0,0)*linear;
            var destination=new Point(Curves.Interpolate(original.X,at.Position.X,translate),Curves.Interpolate(original.Y,at.Position.Y,translateY));
            locals[bone]=PoseMath.FromWorld(scene.Bones[bone],linear with{X=destination.X,Y=destination.Y},world);rebuild();distance+=interval;
        }
    }
}




