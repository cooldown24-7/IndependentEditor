using System.Text.Json.Nodes;
namespace IndependentDev;

internal static partial class Constraints
{
    const double RoundoffFactor=8*2.220446049250313e-16;
    static bool Flag(JsonNode? node,bool fallback=false)=>node?.GetValue<bool>()??fallback;
    static double AngleMix(double a,double b,double mix)=>mix==0?a:mix==1?b:a+Math.IEEERemainder(b-a,360)*mix;
    static bool IkEndpointMatches(Point actual,Point expected)
        =>Math.Abs(expected.X-actual.X)<=1e-9+1e-7*Math.Abs(expected.X)&&Math.Abs(expected.Y-actual.Y)<=1e-9+1e-7*Math.Abs(expected.Y);
    static double Ratio(double value)
    {if(!double.IsFinite(value)||value<0||value>1)throw new InvalidDataException("Constraint mix range");return value;}
    // shortcut: signed Transform mixes are limited to the observed versions.
    static double TransformTranslationMix(double value,string version)=>version is "3.8.55" or "4.1.11"&&value>=-1&&value<0?value:Ratio(value);
    static double TransformMix(double value,string version)=>version is "4.1.11" or "4.1.17"&&value>=-1&&value<0?value:Ratio(value);
    static bool ConformalRootFrame(Joint bone,Affine[] world)
    {
        if(bone.Parent<0||bone.Transform is "onlyTranslation" or "noScale" or "noScaleOrReflection")return true;
        var parent=world[bone.Parent];var x=new Point(parent.A,parent.C).Length;var y=new Point(parent.B,parent.D).Length;
        // A normal parent's QR frame is conformal exactly when the parent itself is conformal.
        return x>0&&y>0&&double.IsFinite(x)&&double.IsFinite(y)&&Math.Abs(x-y)<=RoundoffFactor*Math.Max(x,y)
            &&Math.Abs(parent.A/x*(parent.B/y)+parent.C/x*(parent.D/y))<=RoundoffFactor;
    }
    static bool ConformalLocal(LocalPose pose)=>pose.Scale.X!=0&&Math.Abs(pose.Scale.X)==Math.Abs(pose.Scale.Y)&&Math.IEEERemainder(pose.Shear.Y-pose.Shear.X,180)==0;
    static bool CircleFrame(Affine frame)=>frame.A==frame.D&&frame.B==-frame.C||frame.A==-frame.D&&frame.B==frame.C;
    static double BendTie(double current,double angle,bool positive)=>Math.Abs(Math.IEEERemainder(angle-current,360))==180?current+(positive?180:-180):angle;
    static Affine RotationFrame(Joint bone,Affine[] world)=>bone.Parent<0?Affine.Identity:bone.Transform switch
    {
        "normal"=>world[bone.Parent],
        "onlyTranslation" or "noScale" or "noScaleOrReflection"=>Affine.Identity,
        "noRotationOrReflection"=>PoseMath.Inherited(world[bone.Parent],bone.Transform,0),
        _=>throw new NotSupportedException("IK with advanced inheritance")
    };
    static (Point Point,double Angle) PickIkTurn(Point[] points,Func<Point,double> control,double current,bool positive)
    {
        double Angle(Point point)=>BendTie(current,control(point),positive);
        var best=points[0];var bestAngle=Angle(best);var turn=Math.IEEERemainder(bestAngle-current,360);var roundoff=RoundoffFactor*180;
        foreach(var point in points.Skip(1))
        {
            var angle=Angle(point);var candidateTurn=Math.IEEERemainder(angle-current,360);
            if(Math.Abs(candidateTurn)<Math.Abs(turn)-roundoff||Math.Abs(Math.Abs(candidateTurn)-Math.Abs(turn))<=roundoff&&(positive?candidateTurn>turn:candidateTurn<turn)){best=point;bestAngle=angle;turn=candidateTurn;}
        }
        return(best,bestAngle);
    }

    static double SampleConstraint(JsonArray? keys,double time,string field,double setup,double frameDefault,bool absolute=false,int component=0,int components=1)
    {var value=Timeline.Sample(keys,time,field,frameDefault,absolute,component,components);return keys is null||keys.Count==0||time<Scene.Number(keys[0]!["time"])?setup:value;}
    public static void Apply(Scene scene,LocalPose[] locals,Affine[] world,string? animation,double time,string skin,bool[] active)
    {
        var definitions=new List<(string Kind,JsonObject Data,int Order)>();var known=new HashSet<string>(StringComparer.Ordinal);var selected=new HashSet<string>(StringComparer.Ordinal);
        var anim=animation is null?null:Scene.Object(scene.Data["animations"]?[animation]);
        foreach(var kind in new[]{"ik","transform","path"})
        {
            foreach(var node in Scene.Array(scene.Data[kind])??[])
            {
                var data=Scene.Object(node)??throw new InvalidDataException("Constraint object");var name=Scene.Text(data["name"]);
                if(name.Length==0||!known.Add(kind+"\0"+name))throw new InvalidDataException("Constraint name");
                var order=data["order"] is null?0:Scene.Integer(data["order"]);if(order<0)throw new InvalidDataException("Constraint order");
                definitions.Add((kind,data,order));
            }
            var channels=Scene.Object(anim?[kind]);
            if(channels is not null)foreach(var channel in channels)if(!known.Contains(kind+"\0"+channel.Key))throw new InvalidDataException("Constraint timeline reference");
            foreach(var definition in Scene.Array(scene.Data["skins"])??[])if(Scene.Text(definition?["name"])==skin)
                foreach(var entry in Scene.Array(definition?[kind])??[])selected.Add(kind+"\0"+Scene.Text(entry));
        }
        var remaining=2_000_000;
        if(definitions.Count==0)return;
        var names=scene.Bones.Select((b,i)=>(b.Name,i)).ToDictionary(p=>p.Name,p=>p.i,StringComparer.Ordinal);
        void Consume(int work){remaining-=work;if(remaining<0)throw new InvalidDataException("Constraint work limit");}
        void Rebuild(){Consume(scene.Bones.Length);PoseMath.Rebuild(scene.Bones,locals,world,active);}
        foreach(var entry in definitions.OrderBy(d=>d.Order))
        {
            var c=entry.Data;var kind=entry.Kind;var name=Scene.Text(c["name"]);
            string[] modernMixes=["mixRotate","mixX","mixY","mixScaleX","mixScaleY","mixShearY"];
            var modernTransform=kind=="transform"&&c.Any(p=>modernMixes.Contains(p.Key,StringComparer.Ordinal));
            if(modernTransform&&(scene.Version is not ("4.1.11" or "4.1.17")||modernMixes.Any(field=>c[field] is null)))throw new NotSupportedException("Explicit 4.1.11/4.1.17 Transform mixes required; omitted defaults pending");
            var fields=kind=="ik"?new[]{"name","order","skin","bones","target","mix","softness","bendPositive","compress","stretch","uniform"}:kind=="transform"?new[]{"name","order","skin","bones","bone","target","local","relative","rotation","x","y","scaleX","scaleY","shearY"}.Concat(modernTransform?modernMixes:["rotateMix","translateMix","scaleMix","shearMix"]).ToArray():new[]{"name","order","skin","bones","target","positionMode","spacingMode","rotateMode","rotation","position","spacing","rotateMix","translateMix","mixRotate","mixX","mixY"};
            if(c.Any(p=>!fields.Contains(p.Key,StringComparer.Ordinal)))throw new NotSupportedException("Unverified "+kind+" constraint field");
            var enabled=!Flag(c["skin"])||selected.Contains(kind+"\0"+name);
            var boneNodes=Scene.Array(c["bones"]);
            if(boneNodes is null&&kind=="transform"&&c["bone"] is not null)boneNodes=new JsonArray(c["bone"]!.DeepClone());
            if(boneNodes is null||boneNodes.Count==0||boneNodes.Count>4096||kind=="ik"&&boneNodes.Count>2)throw new InvalidDataException("Constraint bones");
            var bones=boneNodes.Select(n=>names.TryGetValue(Scene.Text(n),out var index)?index:throw new InvalidDataException("Constraint bone reference")).ToArray();
            if(bones.Distinct().Count()!=bones.Length)throw new InvalidDataException("Duplicate constraint bone");
            var channel=Scene.Object(anim?[kind])?[name];
            if(kind=="path"){ApplyPath(scene,c,bones,Scene.Object(channel),locals,world,animation,time,skin,Consume,Rebuild,enabled,active);continue;}
            var keys=Scene.Array(channel);
            var hasCurveArray=(keys??[]).Any(k=>k?["curve"] is JsonArray);var absoluteIk=kind=="ik"&&scene.Version is "4.1.11" or "4.1.17"&&hasCurveArray;
            var absoluteTransform=modernTransform&&scene.Version is "4.1.11" or "4.1.17"&&hasCurveArray;
            if(scene.Version.StartsWith("4.1.")&&hasCurveArray&&!absoluteIk&&!absoluteTransform)throw new NotSupportedException("4.1 constraint curve schema pending");
            var keyFields=kind=="ik"?new[]{"time","curve","mix","softness","bendPositive","compress","stretch"}:new[]{"time","curve"}.Concat(modernTransform?modernMixes:["rotateMix","translateMix","scaleMix","shearMix"]).ToArray();
            var previousTime=double.NegativeInfinity;
            if(keys is not null)foreach(var node in keys)
            {
                var key=Scene.Object(node)??throw new InvalidDataException("Constraint frame object");
                if(key.Any(p=>!keyFields.Contains(p.Key,StringComparer.Ordinal)))throw new NotSupportedException("Unverified constraint key");
                if(modernTransform&&modernMixes.Any(field=>key[field] is null))throw new NotSupportedException("Explicit 4.1 Transform key mixes required; omitted defaults pending");
                var keyTime=Scene.Number(key["time"]);if(keyTime<0||keyTime<=previousTime)throw new InvalidDataException("Timeline order");previousTime=keyTime;
                if(absoluteTransform)
                {
                    if(key["curve"] is JsonArray curve)
                    {
                        if(ReferenceEquals(node,keys![keys.Count-1]))throw new NotSupportedException("Absolute Transform curve requires next key");var controls=Scene.Values(curve);if(controls.Length!=24)throw new InvalidDataException("Absolute Transform curve components");
                        for(var component=0;component<6;component++){if(controls[component*4+1]<-1||controls[component*4+1]>1||controls[component*4+3]<-1||controls[component*4+3]>1)throw new InvalidDataException("Absolute Transform mix controls");}
                    }
                    else Timeline.Fraction(key["curve"],0);
                }
                else if(absoluteIk&&key["curve"] is JsonArray)
                {
                    var controls=Scene.Values(key["curve"]);if(controls.Length!=8)throw new InvalidDataException("Absolute curve components");if(ReferenceEquals(node,keys[keys.Count-1]))throw new NotSupportedException("IK absolute curve requires next key");if(controls[1] is <0 or >1||controls[3] is <0 or >1)throw new InvalidDataException("IK absolute mix controls");
                    // shortcut: pinned4.1.17 IK array evidence covers constant softness0; extend after an independent softness formula.
                    if(controls[5]!=0||controls[7]!=0)throw new NotSupportedException("IK absolute softness pending");
                }
                else Timeline.Fraction(key["curve"],0);
                foreach(var p in key)
                {
                    if(p.Key is "bendPositive" or "compress" or "stretch"){Flag(p.Value);continue;}
                    if(p.Key is "time" or "curve")continue;
                    var value=Scene.Number(p.Value);if(kind=="transform"&&modernTransform)TransformMix(value,scene.Version);else if(kind=="transform"&&p.Key=="translateMix")TransformTranslationMix(value,scene.Version);else if(p.Key.EndsWith("Mix",StringComparison.Ordinal)||p.Key=="mix")Ratio(value);
                    if(p.Key=="softness"&&value<0)throw new InvalidDataException("IK softness range");
                }
                if(absoluteIk&&Scene.Number(key["softness"])!=0)throw new NotSupportedException("IK absolute softness pending");
            }
            bool Step(string field,bool setup)
            {
                var value=setup;if(keys is not null)foreach(var key in keys)if(Scene.Number(key!["time"])<=time)value=Flag(key[field],field=="bendPositive");return value;
            }
            if(!names.TryGetValue(Scene.Text(c["target"]),out var target))throw new InvalidDataException("Constraint target reference");
            if(kind=="ik")
            {
                if(bones.Length==2&&scene.Bones[bones[1]].Parent!=bones[0])throw new InvalidDataException("IK bones must form direct chain");
                for(var ancestor=target;ancestor>=0;ancestor=scene.Bones[ancestor].Parent)
                {Consume(1);if(bones.Contains(ancestor))throw new InvalidDataException("IK target depends on constrained bone");}
            }
            enabled=enabled&&active[target]&&bones.Any(b=>active[b]);
            if(kind=="ik")
            {
                var setupSoftness=Scene.Number(c["softness"]);if(setupSoftness<0)throw new InvalidDataException("IK softness range");
                var mix=Ratio(SampleConstraint(keys,time,"mix",Ratio(Scene.Number(c["mix"],1)),1,absoluteIk,0,2));var softness=SampleConstraint(keys,time,"softness",setupSoftness,0,absoluteIk,1,2);
                if(softness<0)throw new InvalidDataException("IK softness range");
                var positive=Step("bendPositive",Flag(c["bendPositive"],true));var compress=Step("compress",Flag(c["compress"]));var stretch=Step("stretch",Flag(c["stretch"]));var uniform=Flag(c["uniform"]);
                if(!enabled||mix==0)continue;
                if(bones.Any(b=>!active[b]))throw new NotSupportedException("Partially active IK chain");
                for(var i=0;i<bones.Length;i++)
                {
                    var bone=scene.Bones[bones[i]];var mode=bone.Transform;
                    if(i==1&&(bone.Length==0||locals[bones[i]].Scale.X==0))continue;
                    if(bone.Parent<0||mode is "normal" or "onlyTranslation")continue;
                    if(i==0&&mode is "noRotationOrReflection" or "noScale" or "noScaleOrReflection")continue;
                    if(i==1&&mode is "noScale" or "noScaleOrReflection")continue;
                    if(i==1&&mode=="noRotationOrReflection"&&ConformalRootFrame(scene.Bones[bones[0]],world))
                    {
                        continue;
                    }
                    throw new NotSupportedException("IK with advanced inheritance");
                }
                var goal=new Point(world[target].X,world[target].Y);
                if(softness!=0&&bones.Length==2){goal=SoftIkGoal(scene,bones[0],bones[1],goal,softness,locals,world);stretch=false;}
                if(bones.Length==1)AimOne(scene,bones[0],goal,mix,compress,stretch,uniform,locals,world);
                else SolveTwo(scene,bones[0],bones[1],goal,mix,stretch,positive,locals,world,Rebuild,Consume,uniform:uniform);
                Rebuild();
            }
            else
            {
                double Mix(string legacy,string modern)
                {
                    double Validate(double value)=>modernTransform?TransformMix(value,scene.Version):Ratio(value);
                    return Validate(SampleConstraint(keys,time,modernTransform?modern:legacy,Validate(Scene.Number(c[modernTransform?modern:legacy],1)),1,absoluteTransform,Array.IndexOf(modernMixes,modern),6));
                }
                var rotate=Mix("rotateMix","mixRotate");var translate=modernTransform?Mix("translateMix","mixX"):TransformTranslationMix(SampleConstraint(keys,time,"translateMix",TransformTranslationMix(Scene.Number(c["translateMix"],1),scene.Version),1),scene.Version);
                var translateY=modernTransform?Mix("translateMix","mixY"):translate;var scale=Mix("scaleMix","mixScaleX");var scaleY=modernTransform?Mix("scaleMix","mixScaleY"):scale;var shear=Mix("shearMix","mixShearY");
                var local=Flag(c["local"]);var relative=Flag(c["relative"]);
                if(!local&&shear<0)shear=0;
                var offset=new Point(Scene.Number(c["x"]),Scene.Number(c["y"]));
                var rotationOffset=Scene.Number(c["rotation"]);var scaleOffset=new Point(Scene.Number(c["scaleX"]),Scene.Number(c["scaleY"]));var shearOffset=Scene.Number(c["shearY"]);
                if(!enabled)continue;
                bones=bones.Where(b=>active[b]).ToArray();var targetPose=local?locals[target]:PoseMath.Decompose(world[target]);
                var position=local?targetPose.Translation+offset:world[target].Apply(offset);
                position.Validate();
                foreach(var bone in bones)
                {
                    var current=local?locals[bone]:PoseMath.Decompose(world[bone]);
                    var rotation=Affine.DegreeSum(targetPose.Rotation,rotationOffset);var scaleTarget=targetPose.Scale+scaleOffset;var shearTarget=Affine.DegreeSum(targetPose.Shear.Y,shearOffset);
                    scaleTarget.Validate();
                    if(rotate==0&&translate==0&&translateY==0&&scale==0&&scaleY==0&&shear==0)continue;
                    double AbsoluteScale(double currentScale,double targetScale,double amount)
                    {
                        if(amount==0||local&&modernTransform&&currentScale==0)return currentScale;
                        var blended=Curves.Interpolate(currentScale,targetScale,amount);
                        return local&&modernTransform?blended/currentScale:blended;
                    }
                    var desired=new LocalPose(relative?current.Translation+new Point(position.X*translate,position.Y*translateY):new Point(Curves.Interpolate(current.Translation.X,position.X,translate),Curves.Interpolate(current.Translation.Y,position.Y,translateY)),
                        relative?current.Rotation+Math.IEEERemainder(rotation,360)*rotate:AngleMix(current.Rotation,rotation,rotate),
                        relative?new(current.Scale.X*(1+(scaleTarget.X-1)*scale),current.Scale.Y*(1+(scaleTarget.Y-1)*scaleY)):new Point(AbsoluteScale(current.Scale.X,scaleTarget.X,scale),AbsoluteScale(current.Scale.Y,scaleTarget.Y,scaleY)),
                        new(current.Shear.X,relative?current.Shear.Y+Math.IEEERemainder(shearTarget,360)*shear:AngleMix(current.Shear.Y,shearTarget,shear)));
                    desired.Matrix.Validate();locals[bone]=local?desired:PoseMath.FromWorld(scene.Bones[bone],desired.Matrix,world);Rebuild();
                }
            }
        }
    }
    static Point SoftIkGoal(Scene scene,int root,int child,Point goal,double softness,LocalPose[] locals,Affine[] world)
    {
        // shortcut: softness uses a circular normal-inheritance chain; reject other frames until independently verified.
        if(scene.Bones[child].Parent!=root||scene.Bones[root].Transform!="normal"||scene.Bones[child].Transform!="normal"||locals[child].Translation.Y!=0||!ConformalRootFrame(scene.Bones[root],world)||Math.Abs(Math.Abs(locals[root].Scale.X)-Math.Abs(locals[root].Scale.Y))>1e-10||locals[root].Shear!=default||locals[child].Shear!=default)throw new NotSupportedException("IK softness requires a conformal normal chain");
        var origin=world[root].Apply(new());var delta=goal-origin;var distance=delta.Length;
        var first=(world[child].Apply(new())-origin).Length;var second=scene.Bones[child].Length*new Point(world[child].A,world[child].C).Length;
        var width=softness*new Point(world[root].A,world[root].C).Length*(Math.Abs(locals[child].Scale.X)+1)/2;
        var excess=distance-first-second+width;
        if(width==0||distance==0||excess<=0)return goal;
        // A quadratic fillet joins the straight distance response to the reach plateau with continuous slope.
        var reduction=excess<2*width?excess*(excess/width)/4:excess-width;
        var result=origin+delta*((distance-reduction)/distance);result.Validate();return result;
    }
    static double LocalAxisRotation(Affine parent,double worldAngle)
    {
        var local=(parent with{X=0,Y=0}).Inverse().Apply(Affine.DegreeAxis(worldAngle));return Math.Atan2(local.Y,local.X)*180/Math.PI;
    }
    static void AimOne(Scene scene,int bone,Point goal,double mix,bool compress,bool stretch,bool uniform,LocalPose[] locals,Affine[] world)
    {
        var b=scene.Bones[bone];var current=locals[bone];Point delta;
        if(b.Parent>=0&&b.Transform=="normal")delta=world[b.Parent].Inverse().Apply(goal)-current.Translation;
        else if(b.Parent>=0&&b.Transform=="noRotationOrReflection")delta=PoseMath.Inherited(world[b.Parent],b.Transform,0).Inverse().Apply(goal-new Point(world[bone].X,world[bone].Y));
        else delta=goal-new Point(world[bone].X,world[bone].Y);
        delta.Validate();var angle=delta==new Point()?current.Rotation:Math.Atan2(delta.Y,delta.X)*180/Math.PI-current.Shear.X-(current.Scale.X<0?180:0);
        if(b.Parent>=0&&b.Transform is "noScale" or "noScaleOrReflection"&&delta!=new Point())
        {
            var parent=world[b.Parent] with{X=0,Y=0};var orientation=b.Transform=="noScaleOrReflection"?1:parent.DeterminantSign();
            angle=LocalAxisRotation(parent,Math.Atan2(delta.Y,delta.X)*180/Math.PI-orientation*current.Shear.X-(current.Scale.X<0?180:0));
        }
        var scale=current.Scale;var length=b.Length*Math.Abs(scale.X);
        if(compress||stretch)
        {
            if(length==0)throw new InvalidDataException("IK zero length scale");var distance=delta.Length;
            if(compress&&distance<length||stretch&&distance>length){var factor=1+(distance/length-1)*mix;scale=new(scale.X*factor,uniform?scale.Y*factor:scale.Y);}
        }
        locals[bone]=current with{Rotation=AngleMix(current.Rotation,angle,mix),Scale=scale};
    }
    static Point[] EllipseExtremum(Affine basis,Point target,bool maximum,Action<int> consume)
    {
        basis.Validate();target.Validate();var scale=Math.Max(Math.Max(Math.Abs(target.X),Math.Abs(target.Y)),Math.Max(Math.Max(Math.Abs(basis.A),Math.Abs(basis.B)),Math.Max(Math.Abs(basis.C),Math.Abs(basis.D))));
        if(scale==0)throw new InvalidDataException("IK ellipse numeric range");
        var a=basis.A/scale;var b=basis.B/scale;var c=basis.C/scale;var d=basis.D/scale;
        var xx=a*a+b*b;var yy=c*c+d*d;var xy=a*c+b*d;var major=Math.Sqrt((xx+yy+double.Hypot(xx-yy,2*xy))/2);var minor=Math.Abs(a*d-b*c)/major;
        var major2=major*major;var minor2=minor*minor;
        // shortcut: extreme axis/target relative scales can underflow here; use exponent-aware math when that range is required.
        if(!double.IsFinite(major)||!double.IsFinite(minor)||major2==0||minor2==0)throw new InvalidDataException("IK ellipse numeric range");
        double cosine,sine;
        if(xy==0){cosine=xx>=yy?1:0;sine=xx>=yy?0:1;}
        else{var angle=.5*Math.Atan2(2*xy,xx-yy);cosine=Math.Cos(angle);sine=Math.Sin(angle);}
        var tx=target.X/scale;var ty=target.Y/scale;
        var x=cosine*tx+sine*ty;var y=-sine*tx+cosine*ty;
        // Preserve symmetric-axis ties within eight machine epsilons of the projected target norm.
        var projectedRoundoff=RoundoffFactor*double.Hypot(tx,ty);if(Math.Abs(x)<=projectedRoundoff)x=0;if(Math.Abs(y)<=projectedRoundoff)y=0;
        var outside=double.Hypot(x/major,y/minor)>1;var difference=Math.Max(0,major2-minor2);
        var offsetX=maximum?0:outside?major2:difference;var offsetY=maximum?difference:outside?minor2:0;var direction=maximum?-1d:1d;
        Point Map(double qx,double qy)
        {
            var boundary=double.Hypot(qx/major,qy/minor);
            if(!double.IsFinite(boundary)||Math.Abs(boundary-1)>1e-10)throw new InvalidDataException("IK ellipse convergence");
            var result=new Point((cosine*qx-sine*qy)*scale,(sine*qx+cosine*qy)*scale);result.Validate();return result;
        }
        double Limit(double value,double offset)=>offset==0?(value==0?0:double.PositiveInfinity):value/offset;
        if((offsetX==0||offsetY==0)&&double.Hypot(Limit(major*x,offsetX),Limit(minor*y,offsetY))<=1)
        {
            if(difference==0)return [Map(major,0),Map(-major,0)];
            if(maximum)
            {
                var qy=-minor2*y/difference;var qx=major*Math.Sqrt(Math.Max(0,1-(qy/minor)*(qy/minor)));return [Map(qx,qy),Map(-qx,qy)];
            }
            else
            {
                var qx=major2*x/difference;var qy=minor*Math.Sqrt(Math.Max(0,1-(qx/major)*(qx/major)));return [Map(qx,qy),Map(qx,-qy)];
            }
        }
        // Shift the multiplier to its globally minimizing/maximizing branch; its boundary equation is monotone.
        double lo=0,hi=!maximum&&!outside?minor2:major*double.Hypot(x,y);
        for(var iteration=0;iteration<1024;iteration++)
        {
            var mid=lo+(hi-lo)/2;if(mid==lo||mid==hi)break;consume(1);
            if(double.Hypot(major*x/(mid+offsetX),minor*y/(mid+offsetY))>1)lo=mid;else hi=mid;
        }
        var multiplier=lo+(hi-lo)/2;return [Map(direction*major2*x/(multiplier+offsetX),direction*minor2*y/(multiplier+offsetY))];
    }
    static void SolveTwo(Scene scene,int root,int child,Point goal,double mix,bool stretch,bool positive,LocalPose[] locals,Affine[] world,Action rebuild,Action<int> consume,Affine? virtualFrame=null,string? virtualChildMode=null,bool uniform=true)
    {
        var originalRoot=locals[root];var originalChild=locals[child];var rootBone=scene.Bones[root];var childBone=scene.Bones[child];
        if(stretch&&!uniform)
        {
            SolveTwo(scene,root,child,goal,1,true,positive,locals,world,rebuild,consume,virtualFrame,virtualChildMode);
            var scale=locals[root].Scale with{Y=originalRoot.Scale.Y};
            if(locals[root].Scale!=scale)
            {
                // Re-evaluate the pose with only X stretched, then mix the controls once.
                locals[root]=originalRoot with{Scale=scale};locals[child]=originalChild;rebuild();
                SolveTwo(scene,root,child,goal,1,false,positive,locals,world,rebuild,consume,virtualFrame,virtualChildMode);
            }
            var stretchedRoot=locals[root];var stretchedChild=locals[child];
            locals[root]=originalRoot with{Rotation=AngleMix(originalRoot.Rotation,stretchedRoot.Rotation,mix),Scale=originalRoot.Scale+(stretchedRoot.Scale-originalRoot.Scale)*mix};
            locals[child]=originalChild with{Rotation=AngleMix(originalChild.Rotation,stretchedChild.Rotation,mix)};return;
        }
        void SolveChildControl(double rotation,Func<double,double> control,string mode)
        {
            locals[child]=originalChild with{Rotation=rotation};
            void RebuildChild()
            {
                rebuild();var saved=locals[child];locals[child]=saved with{Rotation=control(saved.Rotation)};
                try{rebuild();}finally{locals[child]=saved;}
            }
            SolveTwo(scene,root,child,goal,1,stretch,positive,locals,world,RebuildChild,consume,virtualFrame,mode);
            var solvedRoot=locals[root];var childControl=control(locals[child].Rotation);
            locals[root]=originalRoot with{Rotation=AngleMix(originalRoot.Rotation,solvedRoot.Rotation,mix),Scale=originalRoot.Scale+(solvedRoot.Scale-originalRoot.Scale)*mix};
            locals[child]=originalChild with{Rotation=AngleMix(originalChild.Rotation,childControl,mix)};
        }
        var zeroTip=childBone.Length==0||originalChild.Scale.X==0;
        if(virtualChildMode is null&&originalChild.Translation==new Point()&&(zeroTip||childBone.Transform is "onlyTranslation" or "noScale" or "noScaleOrReflection"))
        {
            if(zeroTip)return;
            var origin=new Point(world[root].X,world[root].Y);var target=goal-origin;target.Validate();var distance=target.Length;var radius=childBone.Length*Math.Abs(originalChild.Scale.X);
            if(!double.IsFinite(distance)||!double.IsFinite(radius)||radius<=0)throw new InvalidDataException("IK numeric range");
            if(distance==0)return;
            var orientation=childBone.Transform=="noScale"?world[root].DeterminantSign():1;
            var angle=Affine.DegreeSum(Affine.DegreeSum(Math.Atan2(target.Y,target.X)*180/Math.PI,-orientation*originalChild.Shear.X),originalChild.Scale.X<0?-180:0);
            if(childBone.Transform!="onlyTranslation")angle=LocalAxisRotation(world[root],angle);
            angle=BendTie(originalChild.Rotation,angle,positive);
            locals[child]=originalChild with{Rotation=AngleMix(originalChild.Rotation,angle,mix)};rebuild();
            if(mix==1)
            {
                var actual=world[child].Apply(new(childBone.Length,0));var expected=origin+new Point(target.X/distance,target.Y/distance)*radius;expected.Validate();
                if(!IkEndpointMatches(actual,expected))throw new InvalidDataException("IK zero joint convergence");
            }
            return;
        }
        if(virtualChildMode is null&&zeroTip)
        {
            var tipParent=rootBone.Parent<0?Affine.Identity:world[rootBone.Parent];var dynamicTip=rootBone.Parent>=0&&rootBone.Transform is "noScale" or "noScaleOrReflection";
            var link=(originalRoot with{Translation=new(),Rotation=0}).Matrix.Apply(originalChild.Translation);
            if(dynamicTip&&rootBone.Transform=="noScale")link=new(link.X,tipParent.DeterminantSign()*link.Y);
            var radius=link.Length;
            var origin=new Point(world[root].X,world[root].Y);var target=goal-origin;target.Validate();var distance=target.Length;
            if(radius==0)throw new InvalidDataException("IK zero link length");
            if(!double.IsFinite(radius)||!double.IsFinite(distance))throw new InvalidDataException("IK numeric range");
            var tipFrame=RotationFrame(rootBone,world);
            var tipLinear=tipFrame with{X=0,Y=0};var tipInverse=tipLinear.Inverse();
            var circle=CircleFrame(tipLinear);
            if(distance==0&&circle)return;
            var radial=tipInverse.Apply(target).Length;var factor=stretch&&radial>radius?radial/radius:1;
            if(!double.IsFinite(radial)||!double.IsFinite(factor))throw new InvalidDataException("IK stretch numeric range");
            Point[] joints=factor>1?[target]:circle?[new Point(target.X/distance,target.Y/distance)*tipLinear.Apply(link).Length]:EllipseExtremum(tipLinear*new Affine(radius,0,0,radius,0,0),target,false,consume);
            double Control(Point joint)
            {
                var direction=tipInverse.Apply(joint);var angle=(Math.Atan2(direction.Y,direction.X)-Math.Atan2(link.Y,link.X))*180/Math.PI;
                if(dynamicTip)angle=LocalAxisRotation(tipParent,angle);
                return angle;
            }
            var best=PickIkTurn(joints,Control,originalRoot.Rotation,positive);
            var scale=originalRoot.Scale*factor;scale.Validate();
            locals[root]=originalRoot with{Rotation=AngleMix(originalRoot.Rotation,best.Angle,mix),Scale=new(Curves.Interpolate(originalRoot.Scale.X,scale.X,mix),Curves.Interpolate(originalRoot.Scale.Y,scale.Y,mix))};
            rebuild();
            if(mix==1)
            {
                var actual=world[child].Apply(new(childBone.Length,0));var expected=origin+best.Point;expected.Validate();
                if(!IkEndpointMatches(actual,expected))throw new InvalidDataException("IK collapsed tip convergence");
            }
            return;
        }
        if(virtualChildMode is null&&originalChild.Translation==new Point()&&(childBone.Transform=="normal"&&ConformalLocal(originalRoot)||childBone.Transform=="noRotationOrReflection"&&CircleFrame(RotationFrame(rootBone,world))))
        {
            var origin=new Point(world[root].X,world[root].Y);var target=goal-origin;target.Validate();var distance=target.Length;var radius=childBone.Length*Math.Abs(originalChild.Scale.X);
            if(!double.IsFinite(distance)||!double.IsFinite(radius)||radius<=0)throw new InvalidDataException("IK numeric range");
            var basis=childBone.Transform=="normal"?world[root] with{X=0,Y=0}:PoseMath.Inherited(world[root],childBone.Transform,0);var shape=RotationFrame(rootBone,world);
            var circle=childBone.Transform=="normal"?CircleFrame(shape):ConformalLocal(originalRoot)||CircleFrame(basis);var worldRadius=new Point(basis.A,basis.C).Length*radius;
            if(circle&&(!double.IsFinite(worldRadius)||worldRadius<=0))throw new InvalidDataException("IK numeric range");
            if(circle&&distance==0)return;
            var inverse=basis.Inverse();var radial=inverse.Apply(target).Length;
            var factor=stretch?Math.Max(1,circle?distance/worldRadius:radial/radius):1;
            if(!double.IsFinite(radial)||!double.IsFinite(factor))throw new InvalidDataException("IK stretch numeric range");
            Point[] points=factor>1?[target]:circle?[new Point(target.X/distance,target.Y/distance)*worldRadius]:EllipseExtremum(basis*new Affine(radius,0,0,radius,0,0),target,false,consume);
            double Control(Point point){var direction=inverse.Apply(point);return Affine.DegreeSum(Affine.DegreeSum(Math.Atan2(direction.Y,direction.X)*180/Math.PI,-originalChild.Shear.X),originalChild.Scale.X<0?-180:0);}
            var best=PickIkTurn(points,Control,originalChild.Rotation,positive);var scale=originalRoot.Scale*factor;scale.Validate();
            locals[root]=originalRoot with{Scale=new(Curves.Interpolate(originalRoot.Scale.X,scale.X,mix),Curves.Interpolate(originalRoot.Scale.Y,scale.Y,mix))};locals[child]=originalChild with{Rotation=AngleMix(originalChild.Rotation,best.Angle,mix)};rebuild();
            if(mix==1)
            {
                var actual=world[child].Apply(new(childBone.Length,0));var expected=origin+best.Point;expected.Validate();
                if(!IkEndpointMatches(actual,expected))throw new InvalidDataException("IK zero joint convergence");
            }
            return;
        }
        if(virtualChildMode is null&&originalChild.Translation==new Point()&&childBone.Transform=="normal")
        {
            SolveZeroJointAnnulus(scene,root,child,goal,mix,stretch,positive,locals,world,rebuild,consume);return;
        }
        if(virtualChildMode is null&&childBone.Transform=="noRotationOrReflection")
        {
            if(!ConformalLocal(originalRoot)||!ConformalRootFrame(rootBone,world))
            {
                var generalFrame=world[root];var generalBasis=PoseMath.Inherited(generalFrame,"noRotationOrReflection",0);
                var generalVector=(generalFrame with{X=0,Y=0}).Apply(originalChild.Translation);var generalLength=generalVector.Length;
                var generalRadius=childBone.Length*Math.Abs(originalChild.Scale.X);
                if(generalLength==0||generalRadius==0)throw new InvalidDataException("IK zero link length");
                var generalScale=generalLength/childBone.Length;
                if(!double.IsFinite(generalLength)||!double.IsFinite(generalRadius)||!double.IsFinite(generalScale))throw new InvalidDataException("IK numeric range");
                var generalFactor=1d;
                if(stretch)
                {
                    var ellipse=generalBasis*new Affine(generalRadius,0,0,generalRadius,0,0);
                    var target=goal-new Point(generalFrame.X,generalFrame.Y);target.Validate();
                    var radial=ellipse.Inverse().Apply(target).Length;
                    if(!double.IsFinite(radial))throw new InvalidDataException("IK stretch numeric range");
                    double Distance(double factor)
                    {
                        consume(1);var scaledTarget=target*(1/factor);
                        return (scaledTarget-EllipseExtremum(ellipse,scaledTarget,false,consume)[0]).Length;
                    }
                    if(radial>1&&Distance(1)>generalLength)
                    {
                        // Both links scale together; keep the search outside/on the original ellipse.
                        var lower=1d;var upper=Math.Min(2,radial);var distance=Distance(upper);
                        for(var step=0;step<1024&&distance>generalLength&&upper<radial;step++)
                        {lower=upper;upper=upper>=radial/2?radial:upper*2;distance=Distance(upper);}
                        if(!double.IsFinite(distance)||distance>generalLength)throw new InvalidDataException("IK stretch convergence");
                        for(var step=0;step<128;step++)
                        {
                            var mid=lower+(upper-lower)/2;if(mid==lower||mid==upper)break;
                            if(Distance(mid)>generalLength)lower=mid;else upper=mid;
                        }
                        if(upper-lower>RoundoffFactor*upper)throw new InvalidDataException("IK stretch convergence");
                        generalFactor=upper;
                    }
                }
                var generalActualRoot=originalRoot with{Scale=originalRoot.Scale*generalFactor};generalActualRoot.Scale.Validate();
                generalBasis=generalBasis*new Affine(generalFactor,0,0,generalFactor,0,0);generalBasis.Validate();
                generalScale*=generalFactor;if(!double.IsFinite(generalScale))throw new InvalidDataException("IK stretch numeric range");
                var generalZero=(originalRoot with{Translation=new(),Rotation=0}).Matrix.Apply(originalChild.Translation);
                var generalParent=rootBone.Parent<0?Affine.Identity:world[rootBone.Parent];
                var generalOrientation=rootBone.Parent>=0&&rootBone.Transform=="noScale"?generalParent.DeterminantSign():1;
                var generalSign=originalChild.Scale.X<0?180:0;
                double RootControl(double alpha)
                {
                    if(rootBone.Parent>=0&&rootBone.Transform is "noScale" or "noScaleOrReflection")
                        return LocalAxisRotation(generalParent,alpha-Math.Atan2(generalOrientation*generalZero.Y,generalZero.X)*180/Math.PI);
                    if(rootBone.Parent>=0&&rootBone.Transform=="normal")alpha=LocalAxisRotation(generalParent,alpha);
                    return alpha-Math.Atan2(generalZero.Y,generalZero.X)*180/Math.PI;
                }
                locals[root]=new(originalRoot.Translation,originalChild.Rotation+originalChild.Shear.X+generalSign,new(1,1));
                locals[child]=new(new(generalRadius,0),Math.Atan2(generalVector.Y,generalVector.X)*180/Math.PI,new(generalScale,1));
                void RebuildGeneral()
                {
                    var savedGeneralRoot=locals[root];var savedGeneralChild=locals[child];
                    locals[root]=generalActualRoot with{Rotation=RootControl(savedGeneralChild.Rotation)};
                    locals[child]=originalChild with{Rotation=savedGeneralRoot.Rotation-originalChild.Shear.X-generalSign};
                    try{rebuild();}finally{locals[root]=savedGeneralRoot;locals[child]=savedGeneralChild;}
                }
                var generalReflected=generalFrame.DeterminantSign()<0;
                // Swapping the physical joint circle and tip ellipse reverses their bend cross product.
                SolveTwo(scene,root,child,goal,1,false,generalReflected?positive:!positive,locals,world,RebuildGeneral,consume,generalBasis,"onlyTranslation");
                if(generalFactor>1)
                {
                    var endpoint=world[child].Apply(new(childBone.Length,0));
                    if(Math.Abs(endpoint.X-goal.X)>1e-12+1e-11*Math.Abs(goal.X)||Math.Abs(endpoint.Y-goal.Y)>1e-12+1e-11*Math.Abs(goal.Y))throw new InvalidDataException("IK stretch convergence");
                }
                var generalRootControl=RootControl(locals[child].Rotation);var generalChildControl=locals[root].Rotation-originalChild.Shear.X-generalSign;
                locals[root]=originalRoot with{Rotation=AngleMix(originalRoot.Rotation,generalRootControl,mix),Scale=originalRoot.Scale+(generalActualRoot.Scale-originalRoot.Scale)*mix};
                locals[child]=originalChild with{Rotation=AngleMix(originalChild.Rotation,generalChildControl,mix)};return;
            }
            double Control(double angle)
            {
                var axis=(world[root] with{X=0,Y=0}).Apply(Affine.DegreeAxis(angle+originalChild.Shear.X));
                return Math.Atan2(axis.Y,axis.X)*180/Math.PI-originalChild.Shear.X;
            }
            SolveChildControl(LocalAxisRotation(world[root],originalChild.Rotation+originalChild.Shear.X)-originalChild.Shear.X,Control,"normal");return;
        }
        if(virtualChildMode is null&&childBone.Transform is "noScale" or "noScaleOrReflection")
        {
            var basis=PoseMath.Inherited(world[root],childBone.Transform,originalChild.Rotation);var orientation=basis.DeterminantSign();
            double Control(double angle)=>LocalAxisRotation(world[root],angle+(1-orientation)*originalChild.Shear.X);
            SolveChildControl(Math.Atan2(basis.C,basis.A)*180/Math.PI+(orientation-1)*originalChild.Shear.X,Control,"onlyTranslation");return;
        }
        var childMode=virtualChildMode??childBone.Transform;
        var dynamicRoot=virtualFrame is null&&rootBone.Parent>=0&&rootBone.Transform is "noScale" or "noScaleOrReflection";
        if(dynamicRoot&&childMode=="normal")
        {
            var parent=world[rootBone.Parent];var basis=PoseMath.Inherited(parent,rootBone.Transform,originalRoot.Rotation);var orientation=basis.DeterminantSign();
            double Control(double angle)=>LocalAxisRotation(parent,orientation*angle);
            locals[root]=originalRoot with{Rotation=orientation*Math.Atan2(basis.C,basis.A)*180/Math.PI};
            void RebuildVirtual()
            {
                var saved=locals[root];locals[root]=saved with{Rotation=Control(saved.Rotation)};
                try{rebuild();}finally{locals[root]=saved;}
            }
            SolveTwo(scene,root,child,goal,1,stretch,positive,locals,world,RebuildVirtual,consume,new(1,0,0,orientation,0,0),virtualChildMode);
            var solved=locals[root];locals[root]=originalRoot with{Rotation=AngleMix(originalRoot.Rotation,Control(solved.Rotation),mix),Scale=originalRoot.Scale+(solved.Scale-originalRoot.Scale)*mix};
            locals[child]=originalChild with{Rotation=AngleMix(originalChild.Rotation,locals[child].Rotation,mix)};return;
        }
        var frame=virtualFrame??RotationFrame(rootBone,world);
        var localGoal=frame.Inverse().Apply(goal)-frame.Inverse().Apply(new(world[root].X,world[root].Y));
        var zero=(originalRoot with{Rotation=0}).Matrix;
        if(dynamicRoot)
        {
            var basis=PoseMath.Inherited(world[rootBone.Parent],rootBone.Transform,0);var orientation=basis.DeterminantSign();
            zero=new Affine(1,0,0,orientation,0,0)*(zero with{X=0,Y=0});zero=zero with{X=originalRoot.Translation.X,Y=originalRoot.Translation.Y};
        }
        var firstVector=(zero with{X=0,Y=0}).Apply(originalChild.Translation);
        var first=firstVector.Length;var second=childBone.Length*Math.Max(Math.Abs(originalRoot.Scale.X),Math.Abs(originalRoot.Scale.Y))*Math.Abs(originalChild.Scale.X);
        if(first==0||childBone.Length==0||originalChild.Scale.X==0)throw new InvalidDataException("IK zero link length");
        var scaledRoot=originalRoot;
        var classic=Math.Abs(originalRoot.Scale.X)==Math.Abs(originalRoot.Scale.Y)&&originalRoot.Shear==new Point()&&originalChild.Shear.X==0&&originalChild.Translation.Y==0;
        var refineWorldBoundary=virtualFrame is not null&&virtualChildMode=="onlyTranslation";
        if(childMode=="onlyTranslation")
        {
            var frameScale=new Point(frame.A,frame.C).Length;var ux=new Point(frame.A/frameScale,frame.C/frameScale);var uy=new Point(frame.B/frameScale,frame.D/frameScale);
            var circleFrame=double.IsFinite(frameScale)&&frameScale>0&&Math.Abs(uy.Length-1)<=RoundoffFactor&&Math.Abs(ux.X*uy.X+ux.Y*uy.Y)<=RoundoffFactor;
            // Virtual controls and nonuniform frames need analytic contact angles before local mixing.
            refineWorldBoundary|=!circleFrame;
            if(circleFrame&&!refineWorldBoundary)
            {
                var firstWorld=(frame with{X=0,Y=0}).Apply(firstVector);var firstRadius=firstWorld.Length;var secondRadius=childBone.Length*Math.Abs(originalChild.Scale.X);
                var target=goal-new Point(world[root].X,world[root].Y);target.Validate();var distance=target.Length;
                if(!double.IsFinite(distance))throw new InvalidDataException("IK numeric range");
                // This child ignores root scale, so stretch increases only the first world radius.
                if(stretch&&distance>firstRadius+secondRadius){var factor=(distance-secondRadius)/firstRadius;scaledRoot=originalRoot with{Scale=originalRoot.Scale*factor};firstRadius*=factor;}
                // Column normalization keeps reflection independent of determinant overflow/underflow.
                var parentReflection=Point.Cross(ux,uy)<0;var rootX=Math.Max(Math.Abs(zero.A),Math.Abs(zero.C));var rootY=Math.Max(Math.Abs(zero.B),Math.Abs(zero.D));
                var rootReflection=rootX>0&&rootY>0&&Point.Cross(new(zero.A/rootX,zero.C/rootX),new(zero.B/rootY,zero.D/rootY))<0;
                var solution=Curves.TwoLink(firstRadius,secondRadius,target,parentReflection!=rootReflection?!positive:positive);
                var baseAngle=Math.Atan2(firstWorld.Y,firstWorld.X)*180/Math.PI;var rootAngle=(parentReflection?-1:1)*(solution.Root-baseAngle);
                if(dynamicRoot)rootAngle=LocalAxisRotation(world[rootBone.Parent],rootAngle);
                var childAngle=solution.Root+solution.Elbow-originalChild.Shear.X-(originalChild.Scale.X<0?180:0);
                locals[root]=originalRoot with{Rotation=AngleMix(originalRoot.Rotation,rootAngle,mix),Scale=originalRoot.Scale+(scaledRoot.Scale-originalRoot.Scale)*mix};
                locals[child]=originalChild with{Rotation=AngleMix(originalChild.Rotation,childAngle,mix)};
                if(mix==1)
                {
                    var radius=Math.Clamp(distance,Math.Abs(firstRadius-secondRadius),firstRadius+secondRadius);
                    var expected=new Point(world[root].X,world[root].Y)+(distance==0?new Point(radius,0):new Point(target.X/distance,target.Y/distance)*radius);
                    expected.Validate();
                    rebuild();var actual=world[child].Apply(new(childBone.Length,0));actual.Validate();
                    if(!IkEndpointMatches(actual,expected))throw new InvalidDataException("IK convergence");
                }
                return;
            }
        }
        if(second==0)throw new InvalidDataException("IK zero link length");
        if(stretch&&classic&&childMode=="normal"&&localGoal.Length>first+second)
        {var factor=localGoal.Length/(first+second);scaledRoot=originalRoot with{Scale=originalRoot.Scale*factor};first*=factor;second*=factor;}
        var reflected=zero.DeterminantSign()<0;
        var initial=Curves.TwoLink(first,second,localGoal,reflected?!positive:positive);
        var firstAngle=Math.Atan2(firstVector.Y,firstVector.X)*180/Math.PI;var basisAngle=Math.Atan2(zero.C,zero.A)*180/Math.PI;
        locals[root]=scaledRoot with{Rotation=initial.Root-firstAngle};locals[child]=originalChild with{Rotation=(reflected?-initial.Elbow:initial.Elbow)+firstAngle-basisAngle-originalChild.Shear.X-(originalChild.Scale.X<0?180:0)};
        Point End(){var point=world[child].Apply(new(childBone.Length,0));point.Validate();return point;}
        // Virtual angles feed nonlinear local-angle mixing, so refine before converting back.
        var absoluteTolerance=virtualFrame is null&&virtualChildMode is null?1e-9:1e-12;var relativeTolerance=virtualFrame is null&&virtualChildMode is null?1e-7:1e-11;
        bool Reached(Point point)=>Math.Abs(goal.X-point.X)<=absoluteTolerance+relativeTolerance*Math.Abs(goal.X)&&Math.Abs(goal.Y-point.Y)<=absoluteTolerance+relativeTolerance*Math.Abs(goal.Y);
        for(var iteration=0;iteration<64;iteration++)
        {
            rebuild();var point=End();var error=goal-point;if(Reached(point))break;
            var a=locals[root];var b=locals[child];const double h=.01;
            locals[root]=a with{Rotation=a.Rotation+h};rebuild();var plus=End();locals[root]=a with{Rotation=a.Rotation-h};rebuild();var da=(plus-End())*(1/(2*h));locals[root]=a;
            locals[child]=b with{Rotation=b.Rotation+h};rebuild();plus=End();locals[child]=b with{Rotation=b.Rotation-h};rebuild();var db=(plus-End())*(1/(2*h));locals[child]=b;
            Point delta;
            try{delta=new Affine(da.X,db.X,da.Y,db.Y,0,0).Inverse().Apply(error);}
            catch(InvalidOperationException){break;}
            delta.Validate();delta=new(Math.Clamp(delta.X,-45,45),Math.Clamp(delta.Y,-45,45));var improved=false;
            for(var search=0;search<8;search++)
            {
                var factor=Math.Pow(.5,search);locals[root]=a with{Rotation=a.Rotation+delta.X*factor};locals[child]=b with{Rotation=b.Rotation+delta.Y*factor};rebuild();
                if((goal-End()).Length<error.Length){improved=true;break;}
            }
            if(!improved){locals[root]=a;locals[child]=b;break;}
        }
        rebuild();
        if(!Reached(End())&&(!classic&&childMode=="normal"||childMode=="onlyTranslation"))
        {
            // Eliminate child rotation by placing the residual on its endpoint circle in the inherited or world frame.
            var translationOnly=childMode=="onlyTranslation";var frameLinear=frame with{X=0,Y=0};var worldTarget=goal-new Point(world[root].X,world[root].Y);
            var rootLinearAtZero=(scaledRoot with{Translation=new(),Rotation=0}).Matrix;var searchFirst=rootLinearAtZero.Apply(originalChild.Translation);
            var inverse=translationOnly?Affine.Identity:rootLinearAtZero.Inverse();var radius=childBone.Length*Math.Abs(originalChild.Scale.X);
            var branchReflected=translationOnly?reflected!=(frame.DeterminantSign()<0):reflected;
            Point ChildTarget(double angle)
            {
                consume(1);var axis=Affine.DegreeAxis(angle);var c=axis.X;var s=axis.Y;
                return translationOnly?worldTarget-frameLinear.Apply(new(c*searchFirst.X-s*searchFirst.Y,s*searchFirst.X+c*searchFirst.Y)):inverse.Apply(new(c*localGoal.X+s*localGoal.Y,-s*localGoal.X+c*localGoal.Y))-originalChild.Translation;
            }
            double Error(double angle)=>ChildTarget(angle).Length-radius;
            var candidates=new List<(double Root,double Child,double Score)>();
            void Candidate(double angle)
            {
                var q=ChildTarget(angle);var childAngle=Math.Atan2(q.Y,q.X)*180/Math.PI-originalChild.Shear.X-(originalChild.Scale.X<0?180:0);
                var rootLinear=(scaledRoot with{Translation=new(),Rotation=angle}).Matrix;var childLinear=(originalChild with{Translation=new(),Rotation=childAngle}).Matrix;
                var endpoint=translationOnly?frameLinear.Apply(rootLinear.Apply(originalChild.Translation))+childLinear.Apply(new(childBone.Length,0)):frameLinear.Apply((rootLinear*(originalChild with{Rotation=childAngle}).Matrix).Apply(new(childBone.Length,0)));
                var worldPoint=new Point(world[root].X,world[root].Y)+endpoint;if(!Reached(worldPoint))return;
                var firstPoint=translationOnly?frameLinear.Apply(rootLinear.Apply(originalChild.Translation)):Affine.Local(0,0,angle,1,1,0,0).Apply(firstVector);var bendTarget=translationOnly?worldTarget:localGoal;var cross=Point.Cross(firstPoint,bendTarget-firstPoint);
                var branch=cross==0||(cross>0)==(branchReflected?!positive:positive);var score=(branch?0:10000)+Math.Abs(Math.IEEERemainder(angle-(initial.Root-firstAngle),360));
                candidates.Add((angle,childAngle,score));
            }
            var left=-180d;var leftError=Error(left);var previous=left-.5;var previousError=Error(previous);
            for(var step=1;step<=720;step++)
            {
                var right=-180+step*.5;var rightError=Error(right);
                if(leftError==0)Candidate(left);
                // Tangent roots at reach limits do not change sign; refine each sampled local minimum of the residual too.
                if(Math.Abs(leftError)<=Math.Abs(previousError)&&Math.Abs(leftError)<=Math.Abs(rightError)&&(Math.Abs(leftError)<Math.Abs(previousError)||Math.Abs(leftError)<Math.Abs(rightError)))
                {
                    var lo=previous;var hi=right;
                    for(var i=0;i<64;i++){var a=lo+(hi-lo)/3;var b=hi-(hi-lo)/3;if(Math.Abs(Error(a))>Math.Abs(Error(b)))lo=a;else hi=b;}
                    var angle=(lo+hi)/2;if(Math.Abs(Error(angle))<=1e-12+1e-12*radius)Candidate(angle);
                }
                if((leftError<0)!=(rightError<0))
                {
                    var lo=left;var hi=right;var loError=leftError;
                    for(var i=0;i<128;i++){var mid=(lo+hi)/2;if(mid==lo||mid==hi)break;var error=Error(mid);if((error<0)==(loError<0)){lo=mid;loError=error;}else hi=mid;}
                    Candidate((lo+hi)/2);
                }
                previous=left;previousError=leftError;left=right;leftError=rightError;
            }
            if(candidates.Count>0)
            {
                var best=candidates.MinBy(c=>c.Score);locals[root]=scaledRoot with{Rotation=best.Root};locals[child]=originalChild with{Rotation=best.Child};rebuild();
            }
        }
        var clamped=false;
        if(childMode=="onlyTranslation"&&(!Reached(End())||refineWorldBoundary))
        {
            var frameLinear=frame with{X=0,Y=0};var rootVector=(originalRoot with{Translation=new(),Rotation=0}).Matrix.Apply(originalChild.Translation);var length=rootVector.Length;
            var origin=new Point(world[root].X,world[root].Y);var target=goal-origin;
            var basis=frameLinear*new Affine(length,0,0,length,0,0);var radius=childBone.Length*Math.Abs(originalChild.Scale.X);var inverseFrame=frameLinear.Inverse();var stretched=false;
            var joints=EllipseExtremum(basis,target,false,consume);var nearestDistance=(target-joints[0]).Length;
            var boundaryRoundoff=refineWorldBoundary?RoundoffFactor*Math.Max(radius,Math.Max(target.Length,joints[0].Length)):0;
            if(stretch&&nearestDistance>radius)
            {
                var radial=inverseFrame.Apply(target);radial.Validate();var upper=radial.Length/length;if(!double.IsFinite(upper))throw new InvalidDataException("IK stretch numeric range");
                if(upper>1)
                {
                    Affine Expand(double factor)=>basis with{A=basis.A*factor,B=basis.B*factor,C=basis.C*factor,D=basis.D*factor};
                    var highJoint=EllipseExtremum(Expand(upper),target,false,consume)[0];var highDistance=(target-highJoint).Length;
                    if(!double.IsFinite(highDistance)||highDistance>radius)throw new InvalidDataException("IK stretch numeric range");
                    var lower=1d;
                    for(var iteration=0;iteration<128;iteration++)
                    {
                        var mid=lower+(upper-lower)/2;if(mid==lower||mid==upper)break;consume(1);var joint=EllipseExtremum(Expand(mid),target,false,consume)[0];
                        if((target-joint).Length>radius)lower=mid;else{upper=mid;highJoint=joint;}
                    }
                    scaledRoot=originalRoot with{Scale=originalRoot.Scale*upper};scaledRoot.Scale.Validate();joints=[highJoint];stretched=true;
                }
            }
            if(!stretched&&(refineWorldBoundary?nearestDistance<radius-boundaryRoundoff:nearestDistance<=radius))
            {
                joints=EllipseExtremum(basis,target,true,consume);var farthestDistance=(target-joints[0]).Length;
                if(refineWorldBoundary?farthestDistance>radius+boundaryRoundoff:farthestDistance>=radius)joints=[];
            }
            if(joints.Length>0)
            {
                var parentReflected=frame.DeterminantSign()<0;var branchPositive=reflected!=parentReflected?!positive:positive;var angleRoundoff=RoundoffFactor*180;
                var candidates=new List<(double Root,double Child,Point Expected,double Score)>();
                foreach(var joint in joints)
                {
                    var residual=target-joint;residual.Validate();var distance=residual.Length;if(!double.IsFinite(distance)||distance==0)throw new InvalidDataException("IK ellipse numeric range");
                    var rotated=inverseFrame.Apply(joint);rotated.Validate();var rootAngle=(Math.Atan2(rotated.Y,rotated.X)-Math.Atan2(rootVector.Y,rootVector.X))*180/Math.PI;
                    var childAngle=Math.Atan2(residual.Y,residual.X)*180/Math.PI-originalChild.Shear.X-(originalChild.Scale.X<0?180:0);var expected=stretched?goal:origin+joint+residual*(radius/distance);expected.Validate();
                    if(mix<1)
                    {
                        if(Math.Abs(Math.Abs(Math.IEEERemainder(rootAngle-originalRoot.Rotation,360))-180)<=angleRoundoff)rootAngle=originalRoot.Rotation+(positive?180:-180);
                        if(Math.Abs(Math.Abs(Math.IEEERemainder(childAngle-originalChild.Rotation,360))-180)<=angleRoundoff)childAngle=originalChild.Rotation+(positive?180:-180);
                    }
                    var cross=Point.Cross(joint,residual);var branch=cross==0||(cross>0)==branchPositive;var score=(branch?0:10000)+Math.Abs(Math.IEEERemainder(rootAngle-originalRoot.Rotation,360));
                    candidates.Add((rootAngle,childAngle,expected,score));
                }
                bool TurnsWithBend(double angle){var turn=Math.IEEERemainder(angle-originalRoot.Rotation,360);return positive?turn>0:turn<0;}
                var best=candidates[0];foreach(var candidate in candidates)if(candidate.Score<best.Score-angleRoundoff||Math.Abs(candidate.Score-best.Score)<=angleRoundoff&&TurnsWithBend(candidate.Root)&&!TurnsWithBend(best.Root))best=candidate;
                locals[root]=scaledRoot with{Rotation=best.Root};locals[child]=originalChild with{Rotation=best.Child};rebuild();var actual=End();
                clamped=IkEndpointMatches(actual,best.Expected);
            }
        }
        if(!Reached(End())&&childMode=="normal")
        {
            var rootLinear=(scaledRoot with{Translation=new(),Rotation=0}).Matrix;
            Point RadiusVector(double angle){consume(1);var point=(rootLinear*(originalChild with{Rotation=angle}).Matrix).Apply(new(childBone.Length,0));point.Validate();return point;}
            double Radius(double angle)=>RadiusVector(angle).Length;
            var samples=Enumerable.Range(0,721).Select(i=>Radius(-180+i*.5)).ToArray();
            var minimum=(Angle:-180d,Radius:samples[0]);var maximum=minimum;
            var extrema=new List<(double Angle,double Radius)>();
            for(var i=0;i<720;i++)
            {
                var previous=samples[(i+719)%720];var value=samples[i];var next=samples[i+1];
                foreach(var max in new[]{false,true})
                {
                    if(max?value<previous||value<next:value>previous||value>next)continue;
                    var lo=-180+i*.5-.5;var hi=lo+1;
                    for(var iteration=0;iteration<64;iteration++){var a=lo+(hi-lo)/3;var b=hi-(hi-lo)/3;var av=Radius(a);var bv=Radius(b);if(max?av<bv:av>bv)lo=a;else hi=b;}
                    var angle=(lo+hi)/2;var radius=Radius(angle);extrema.Add((angle,radius));if(radius<minimum.Radius)minimum=(angle,radius);if(radius>maximum.Radius)maximum=(angle,radius);
                }
            }
            var distance=localGoal.Length;var bound=distance<minimum.Radius?minimum:maximum;
            if(distance<minimum.Radius||distance>maximum.Radius)
            {
                double BoundaryScore(double angle)
                {
                    var vector=RadiusVector(angle);var turn=distance==0?originalRoot.Rotation:(Math.Atan2(localGoal.Y,localGoal.X)-Math.Atan2(vector.Y,vector.X))*180/Math.PI;
                    var joint=Affine.Local(0,0,turn,1,1,0,0).Apply(firstVector);
                    var cross=distance==0?0:Point.Cross(new(joint.X/first,joint.Y/first),new(localGoal.X/distance,localGoal.Y/distance));
                    var branch=cross==0||(cross>0)==(reflected?!positive:positive);
                    return (branch?0:10000)+Math.Abs(Math.IEEERemainder(angle-originalChild.Rotation,360));
                }
                var boundaryRadius=bound.Radius;var bestScore=BoundaryScore(bound.Angle);
                foreach(var candidate in extrema)
                {
                    if(Math.Abs(candidate.Radius-boundaryRadius)>RoundoffFactor*boundaryRadius)continue;
                    var score=BoundaryScore(candidate.Angle);if(score<bestScore){bound=candidate;bestScore=score;}
                }
                var childAngle=originalChild.Rotation+Math.IEEERemainder(bound.Angle-originalChild.Rotation,360);
                if(Math.Abs(Math.IEEERemainder(childAngle-originalChild.Rotation,360))==180)childAngle=originalChild.Rotation+(positive?180:-180);
                var vector=RadiusVector(childAngle);var rootAngle=distance==0?originalRoot.Rotation:(Math.Atan2(localGoal.Y,localGoal.X)-Math.Atan2(vector.Y,vector.X))*180/Math.PI;
                var radius=bound.Radius;
                if(stretch&&distance>maximum.Radius)
                {
                    // Uniform root scale multiplies the entire normal-child reach set.
                    var factor=distance/maximum.Radius;if(!double.IsFinite(factor))throw new InvalidDataException("IK stretch numeric range");
                    scaledRoot=scaledRoot with{Scale=scaledRoot.Scale*factor};scaledRoot.Scale.Validate();radius=distance;
                }
                var desired=distance==0?Affine.Local(0,0,rootAngle,1,1,0,0).Apply(vector):new Point(localGoal.X/distance,localGoal.Y/distance)*radius;
                var expected=new Point(world[root].X,world[root].Y)+(frame with{X=0,Y=0}).Apply(desired);
                locals[root]=scaledRoot with{Rotation=rootAngle};locals[child]=originalChild with{Rotation=childAngle};rebuild();var actual=End();
                clamped=IkEndpointMatches(actual,expected);
            }
        }
        if(!Reached(End())&&!clamped)throw new InvalidDataException("IK convergence");
        var solvedRoot=locals[root];var solvedChild=locals[child];
        locals[root]=originalRoot with{Rotation=AngleMix(originalRoot.Rotation,solvedRoot.Rotation,mix),Scale=originalRoot.Scale+(scaledRoot.Scale-originalRoot.Scale)*mix};
        locals[child]=originalChild with{Rotation=AngleMix(originalChild.Rotation,solvedChild.Rotation,mix)};
    }
}

