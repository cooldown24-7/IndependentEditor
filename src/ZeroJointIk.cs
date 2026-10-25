namespace IndependentDev;

internal static partial class Constraints
{
    static void SolveZeroJointAnnulus(Scene scene,int root,int child,Point goal,double mix,bool stretch,bool positive,LocalPose[] locals,Affine[] world,Action rebuild,Action<int> consume)
    {
        var originalRoot=locals[root];var originalChild=locals[child];var rootBone=scene.Bones[root];var childBone=scene.Bones[child];
        var radius=childBone.Length*Math.Abs(originalChild.Scale.X);
        if(!double.IsFinite(radius)||radius<=0)throw new InvalidDataException("IK numeric range");
        var frame=RotationFrame(rootBone,world) with{X=0,Y=0};var inverseFrame=frame.Inverse();
        var dynamic=rootBone.Parent>=0&&rootBone.Transform is "noScale" or "noScaleOrReflection";var parent=rootBone.Parent<0?Affine.Identity:world[rootBone.Parent];
        var u=(originalRoot with{Translation=new(),Rotation=0}).Matrix;
        if(dynamic&&rootBone.Transform=="noScale"){var sign=parent.DeterminantSign();u=new(u.A,u.B,u.C*sign,u.D*sign,0,0);}
        var inverseU=u.Inverse();var ellipse=u*new Affine(radius,0,0,radius,0,0);
        var maximum=EllipseExtremum(ellipse,new(),true,consume)[0];var minimum=EllipseExtremum(ellipse,new(),false,consume)[0];var outer=maximum.Length;var inner=minimum.Length;
        if(!double.IsFinite(outer)||!double.IsFinite(inner)||inner<=0||outer<inner)throw new InvalidDataException("IK annulus numeric range");
        var origin=new Point(world[root].X,world[root].Y);var target=goal-origin;target.Validate();var distance=inverseFrame.Apply(target).Length;
        if(!double.IsFinite(distance))throw new InvalidDataException("IK numeric range");
        var factor=stretch&&distance>outer?distance/outer:1;
        if(!double.IsFinite(factor))throw new InvalidDataException("IK stretch numeric range");
        var required=distance>outer?outer:distance<inner?inner:distance;var center=distance==0&&CircleFrame(frame);
        if(center&&inner==outer)return;
        Point[] points=center?[]:factor>1?[target]:distance>outer?EllipseExtremum(frame*new Affine(outer,0,0,outer,0,0),target,false,consume):distance<inner?EllipseExtremum(frame*new Affine(inner,0,0,inner,0,0),target,false,consume):[target];
        Point[] vectors;
        if(inner==outer)vectors=[u.Apply(Affine.DegreeAxis(Affine.DegreeSum(originalChild.Rotation,originalChild.Shear.X))*(childBone.Length*originalChild.Scale.X))];
        else if(required==outer)vectors=[maximum,maximum*-1];
        else if(required==inner)vectors=[minimum,minimum*-1];
        else
        {
            var ratio=required/outer;var low=inner/outer;var cosine=Math.Sqrt(Math.Clamp((ratio-low)*(ratio+low)/((1-low)*(1+low)),0,1));var sine=Math.Sqrt(Math.Max(0,1-cosine*cosine));
            vectors=[maximum*cosine+minimum*sine,maximum*cosine-minimum*sine,maximum*(-cosine)+minimum*sine,maximum*(-cosine)-minimum*sine];
        }
        var found=false;double bestRoot=0,bestChild=0,bestRootTurn=0,bestChildTurn=0;var bestPoint=new Point();var angularRoundoff=RoundoffFactor*180;
        void Candidate(Point vector,Point point)
        {
            consume(1);var axis=inverseU.Apply(vector);var childAngle=Affine.DegreeSum(Affine.DegreeSum(Math.Atan2(axis.Y,axis.X)*180/Math.PI,-originalChild.Shear.X),originalChild.Scale.X<0?-180:0);childAngle=BendTie(originalChild.Rotation,childAngle,positive);
            var childAxis=Affine.DegreeAxis(Affine.DegreeSum(childAngle,originalChild.Shear.X))*(childBone.Length*originalChild.Scale.X);var actualVector=u.Apply(childAxis);actualVector.Validate();
            if(actualVector.Length==0||!double.IsFinite(actualVector.Length))throw new InvalidDataException("IK annulus numeric range");
            double rootAngle;
            if(center)
            {
                rootAngle=originalRoot.Rotation;var direction=(world[root] with{X=0,Y=0}).Apply(childAxis);var length=direction.Length;var worldRadius=inner*new Point(frame.A,frame.C).Length;
                if(length==0||!double.IsFinite(length)||!double.IsFinite(worldRadius)||worldRadius<=0)throw new InvalidDataException("IK annulus numeric range");
                point=new Point(direction.X/length,direction.Y/length)*worldRadius;
            }
            else
            {
                var local=inverseFrame.Apply(point);rootAngle=(Math.Atan2(local.Y,local.X)-Math.Atan2(actualVector.Y,actualVector.X))*180/Math.PI;
                if(dynamic)rootAngle=LocalAxisRotation(parent,rootAngle);
                rootAngle=BendTie(originalRoot.Rotation,rootAngle,positive);
            }
            point.Validate();var rootTurn=Math.IEEERemainder(rootAngle-originalRoot.Rotation,360);var childTurn=Math.IEEERemainder(childAngle-originalChild.Rotation,360);
            var sameRoot=Math.Abs(Math.Abs(rootTurn)-Math.Abs(bestRootTurn))<=angularRoundoff;var sameChild=Math.Abs(Math.Abs(childTurn)-Math.Abs(bestChildTurn))<=angularRoundoff;
            var preferred=positive?rootTurn>bestRootTurn+angularRoundoff||Math.Abs(rootTurn-bestRootTurn)<=angularRoundoff&&childTurn>bestChildTurn:rootTurn<bestRootTurn-angularRoundoff||Math.Abs(rootTurn-bestRootTurn)<=angularRoundoff&&childTurn<bestChildTurn;
            if(!found||Math.Abs(rootTurn)<Math.Abs(bestRootTurn)-angularRoundoff||sameRoot&&(Math.Abs(childTurn)<Math.Abs(bestChildTurn)-angularRoundoff||sameChild&&preferred))
            {found=true;bestRoot=rootAngle;bestChild=childAngle;bestRootTurn=rootTurn;bestChildTurn=childTurn;bestPoint=point;}
        }
        foreach(var vector in vectors){if(center)Candidate(vector,new());else foreach(var point in points)Candidate(vector,point);}
        if(!found)throw new InvalidDataException("IK annulus convergence");
        var scale=originalRoot.Scale*factor;scale.Validate();locals[root]=originalRoot with{Rotation=bestRoot,Scale=scale};locals[child]=originalChild with{Rotation=bestChild};rebuild();
        var actual=world[child].Apply(new(childBone.Length,0));var expected=origin+bestPoint;expected.Validate();
        if(!IkEndpointMatches(actual,expected))throw new InvalidDataException("IK annulus convergence");
        locals[root]=originalRoot with{Rotation=AngleMix(originalRoot.Rotation,bestRoot,mix),Scale=new(Curves.Interpolate(originalRoot.Scale.X,scale.X,mix),Curves.Interpolate(originalRoot.Scale.Y,scale.Y,mix))};locals[child]=originalChild with{Rotation=AngleMix(originalChild.Rotation,bestChild,mix)};
    }
}
