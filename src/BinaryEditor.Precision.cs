namespace IndependentDev;

internal sealed partial class BinaryEditor
{
    internal static Point WorldRoundingBound(Mesh ideal,Mesh stored,int vertex,Affine[] pose,int slotBone)
    {
        Point Term(Point intended,Point encoded,Affine transform,double weight)
        {
            if(encoded!=new Point(Float32(intended.X),Float32(intended.Y)))throw new InvalidDataException("SKEL coordinate encoding mismatch");
            var dx=Math.Abs(encoded.X-intended.X);var dy=Math.Abs(encoded.Y-intended.Y);
            return new((Math.Abs(transform.A)*dx+Math.Abs(transform.C)*dy)*Math.Abs(weight),(Math.Abs(transform.B)*dx+Math.Abs(transform.D)*dy)*Math.Abs(weight));
        }
        if(ideal.Weights.Length==0)return Term(ideal.Local[vertex],stored.Local[vertex],pose[slotBone],1);
        var bound=new Point();var expected=ideal.Weights[vertex];var actual=stored.Weights[vertex];
        if(expected.Length!=actual.Length)throw new InvalidDataException("SKEL influence count changed");
        for(var i=0;i<expected.Length;i++)
        {
            if(expected[i].Bone!=actual[i].Bone||expected[i].Weight!=actual[i].Weight)throw new InvalidDataException("SKEL influence changed");
            if(expected[i].Weight!=0)bound+=Term(expected[i].Local,actual[i].Local,pose[expected[i].Bone],expected[i].Weight);
            else if(actual[i].Local!=new Point(Float32(expected[i].Local.X),Float32(expected[i].Local.Y)))throw new InvalidDataException("SKEL coordinate encoding mismatch");
        }
        return bound;
    }
    internal static void RequireRoundedTarget(Point actual,Point target,Point current,Point rounding)
    {
        actual.Validate();rounding.Validate();var tolerance=Editor.TargetTolerance(target,current);var error=new Point(Math.Abs(actual.X-target.X),Math.Abs(actual.Y-target.Y));
        if(error.X<=tolerance.X&&error.Y<=tolerance.Y)return;
        // Only proven float32 rounding may extend the target tolerance, capped at 0.0001 source pixels.
        if(rounding.X<0||rounding.Y<0||rounding.X>1e-4||rounding.Y>1e-4||error.X>tolerance.X+rounding.X||error.Y>tolerance.Y+rounding.Y)throw new InvalidOperationException("Edit target numeric precision");
    }
}
