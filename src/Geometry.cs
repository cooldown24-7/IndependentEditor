namespace IndependentDev;

internal readonly record struct Point(double X, double Y)
{
    public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
    public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
    public static Point operator *(Point a, double s) => new(a.X * s, a.Y * s);
    [System.Text.Json.Serialization.JsonIgnore]
    public double Length => double.Hypot(X, Y);
    public static double Cross(Point a, Point b)
    {
        var value=a.X*b.Y-a.Y*b.X;
        return double.IsFinite(value)?value:Numeric.OppositeProducts(a.X,b.Y,-a.Y,b.X)?0:throw new InvalidDataException("Cross product numeric range");
    }
    public static int Orientation(Point a,Point b,Point c)
    {
        if(a==b||a==c||b==c){a.Validate();b.Validate();c.Validate();return 0;}
        var p=(b.X-a.X)*(c.Y-a.Y);var q=(b.Y-a.Y)*(c.X-a.X);var cross=p-q;
        if(double.IsFinite(cross)&&Math.Abs(cross)>8*2.220446049250313e-16*(Math.Abs(p)+Math.Abs(q)))return Math.Sign(cross);
        // Near a line, use the exact binary input values rather than snapping with an epsilon.
        var values=new[]{a.X,a.Y,b.X,b.Y,c.X,c.Y}.Select(value=>
        {
            if(!double.IsFinite(value))throw new InvalidDataException("Mesh orientation numeric range");var bits=BitConverter.DoubleToInt64Bits(value);var exponent=(int)((bits>>52)&2047);var mantissa=(bits&0xfffffffffffffL)|(exponent==0?0:1L<<52);return(Value:bits<0?-mantissa:mantissa,Exponent:exponent==0?-1074:exponent-1075);
        }).ToArray();var minimum=values.Where(v=>v.Value!=0).Select(v=>v.Exponent).DefaultIfEmpty(0).Min();
        var integers=values.Select(v=>v.Value==0?System.Numerics.BigInteger.Zero:new System.Numerics.BigInteger(v.Value)<<(v.Exponent-minimum)).ToArray();
        return ((integers[2]-integers[0])*(integers[5]-integers[1])-(integers[3]-integers[1])*(integers[4]-integers[0])).Sign;
    }
    public void Validate() { if (!double.IsFinite(X) || !double.IsFinite(Y)) throw new InvalidDataException("Nonfinite point"); }
}

internal static class Numeric
{
    internal static bool OppositeProducts(double a,double x,double b,double y)
    {
        if(!double.IsFinite(a)||!double.IsFinite(x)||!double.IsFinite(b)||!double.IsFinite(y)||a==0||x==0||b==0||y==0)return false;
        static (double High,double Low,int Exponent) Parts(double u,double v)
        {
            var eu=Math.ILogB(Math.Abs(u));var ev=Math.ILogB(Math.Abs(v));u=Math.ScaleB(u,-eu);v=Math.ScaleB(v,-ev);var high=u*v;return(high,Math.FusedMultiplyAdd(u,v,-high),eu+ev);
        }
        // Product mantissas are below4; nearby exponent alignment keeps both residuals exact.
        var p=Parts(a,x);var q=Parts(b,y);if(Math.Abs(p.Exponent-q.Exponent)>2)return false;var exponent=Math.Max(p.Exponent,q.Exponent);
        return Math.ScaleB(p.High,p.Exponent-exponent)==-Math.ScaleB(q.High,q.Exponent-exponent)&&Math.ScaleB(p.Low,p.Exponent-exponent)==-Math.ScaleB(q.Low,q.Exponent-exponent);
    }

    internal static double Derivative(double a,double b,double c,double p,double q,double size,double area)
    {
        static double Difference(double left,double right,double factor)
        {var difference=left-right;return double.IsFinite(difference)?difference*factor:left*factor-right*factor;}
        var value=((b-a)*p-(c-a)*q)*size/area;
        return double.IsFinite(value)?value:(Difference(b,a,p/area)-Difference(c,a,q/area))*size;
    }
    internal static double RetrySum(double value,ReadOnlySpan<double> terms)
    {
        if(double.IsFinite(value)&&value!=0)return value;double scale=0;foreach(var term in terms)scale=Math.Max(scale,Math.Abs(term));
        // shortcut: compensated double sums; wider exact accumulation remains separate work.
        if(!double.IsFinite(scale)||scale==0)return value;
        scale=Math.ScaleB(1,Math.ILogB(scale));
        static void Add(double term,ref double sum,ref double error)
        {
            var next=sum+term;
            error+=Math.Abs(sum)>=Math.Abs(term)?(sum-next)+term:(term-next)+sum;sum=next;
        }
        double sum=0,error=0,small=0,smallError=0;foreach(var valueTerm in terms)
        {
            var term=valueTerm/scale;
            // Keep subnormal or rounded frame terms in their original units.
            if(valueTerm!=0&&(!double.IsNormal(term)||term*scale!=valueTerm))Add(valueTerm,ref small,ref smallError);else Add(term,ref sum,ref error);
        }
        return (sum+error)*scale+(small+smallError);
    }
}

internal readonly record struct Affine(double A, double B, double C, double D, double X, double Y)
{
    public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);
    internal int DeterminantSign()
    {
        Validate();var determinant=A*D-B*C;
        if(double.IsFinite(determinant)&&determinant!=0)return Math.Sign(determinant);
        // shortcut: try binary row and column frames; extreme normalized exponent spans still need wider predicates.
        var sign=Math.Sign(BinaryRowFrame(A,B,C,D)?.Determinant??0);return sign!=0?sign:Math.Sign(BinaryRowFrame(A,C,B,D)?.Determinant??0);
    }
    static (Affine Matrix,double XScale,double YScale,double Determinant)? BinaryRowFrame(double a,double b,double c,double d)
    {
        var x=Math.Max(Math.Abs(a),Math.Abs(b));var y=Math.Max(Math.Abs(c),Math.Abs(d));if(x==0||y==0||!double.IsFinite(x)||!double.IsFinite(y))return null;
        x=Math.ScaleB(1,Math.ILogB(x));y=Math.ScaleB(1,Math.ILogB(y));
        if(a!=0&&!double.IsNormal(a/x)||b!=0&&!double.IsNormal(b/x)||c!=0&&!double.IsNormal(c/y)||d!=0&&!double.IsNormal(d/y))return null;
        a/=x;b/=x;c/=y;d/=y;
        return(new(a,b,c,d,0,0),x,y,CompensatedProductSum(a,d,-b,c));
    }
    internal double? DeterminantOver(double divisor)
    {
        var exponent=Math.ILogB(divisor);var mantissa=Math.ScaleB(divisor,-exponent);
        for(var transpose=0;transpose<2;transpose++)
        {
            var frame=transpose==0?BinaryRowFrame(A,B,C,D):BinaryRowFrame(A,C,B,D);if(frame is not{} f||f.Determinant==0)continue;
            var value=Math.ScaleB(f.Determinant/mantissa,Math.ILogB(f.XScale)+Math.ILogB(f.YScale)-exponent);
            if(double.IsFinite(value)&&value!=0)return value;
        }
        return null;
    }
    static double Coordinate(double a,double b,double offset,Point p)
    {
        var value=a*p.X+b*p.Y+offset;if(double.IsFinite(value))return value;
        value=Math.FusedMultiplyAdd(a,p.X,Math.FusedMultiplyAdd(b,p.Y,offset));
        if(double.IsFinite(value))return value;value=Math.FusedMultiplyAdd(b,p.Y,Math.FusedMultiplyAdd(a,p.X,offset));if(double.IsFinite(value))return value;
        if(!double.IsFinite(a)||!double.IsFinite(b)||!double.IsFinite(offset)||!double.IsFinite(p.X)||!double.IsFinite(p.Y))return value;
        // shortcut: exact factoring or product cancellation is recovered; other overflow still needs wider accumulation.
        for(var transpose=0;transpose<2;transpose++)
        {
            var factor=transpose==0?BinaryFactor(a,b,p.X,p.Y):BinaryFactor(p.X,p.Y,a,b);if(factor is not{} f)continue;
            var sum=f.Left+f.Right;if(!double.IsFinite(sum))continue;
            var error=Math.Abs(f.Left)>=Math.Abs(f.Right)?(f.Left-sum)+f.Right:(f.Right-sum)+f.Left;
            if(error==0)return Math.FusedMultiplyAdd(f.Factor,sum,offset);
        }
        return Numeric.OppositeProducts(a,p.X,b,p.Y)?offset:value;
    }
    static (double Factor,double Left,double Right)? BinaryFactor(double a,double b,double x,double y)
    {
        if(a==0||b==0)return null;if(Math.Abs(b)>Math.Abs(a))(a,b,x,y)=(b,a,y,x);var ea=Math.ILogB(Math.Abs(a));var eb=Math.ILogB(Math.Abs(b));
        if(Math.Abs(Math.ScaleB(a,-ea))!=Math.Abs(Math.ScaleB(b,-eb)))return null;
        var sign=Math.Sign(a)*Math.Sign(b);var right=Math.ScaleB(y,eb-ea)*sign;
        return double.IsFinite(right)&&Math.ScaleB(right,ea-eb)*sign==y?(a,x,right):null;
    }
    public Point Apply(Point p)=>new(Coordinate(A,B,X,p),Coordinate(C,D,Y,p));
    static double ProductSum(double a,double x,double b,double y)
    {var value=a*x+b*y;return double.IsFinite(value)?value:Coordinate(a,b,0,new(x,y));}
    static double CompensatedProductSum(double a,double x,double b,double y)
    {var p=a*x;var q=b*y;return (p+q)+(Math.FusedMultiplyAdd(a,x,-p)+Math.FusedMultiplyAdd(b,y,-q));}
    public static Affine operator *(Affine p, Affine q)
    {
        var position=p.Apply(new(q.X,q.Y));
        return new(ProductSum(p.A,q.A,p.B,q.C),ProductSum(p.A,q.B,p.B,q.D),ProductSum(p.C,q.A,p.D,q.C),ProductSum(p.C,q.B,p.D,q.D),position.X,position.Y);
    }
    public Affine Inverse()
    {
        var det = A * D - B * C;
        if (!double.IsFinite(det) || det == 0)
        {
            var scale=Math.Max(Math.Max(Math.Abs(A),Math.Abs(B)),Math.Max(Math.Abs(C),Math.Abs(D)));
            if(!double.IsFinite(scale)||scale==0)throw new InvalidOperationException("Singular edit transform");
            var normalized=new Affine(A/scale,B/scale,C/scale,D/scale,0,0);var nd=normalized.A*normalized.D-normalized.B*normalized.C;
            try{return ScaledInverse(normalized,nd,scale,scale,false,false);}
            catch(InvalidOperationException)
            {
                for(var transpose=0;transpose<2;transpose++)
                {
                    var frame=transpose==0?BinaryRowFrame(A,B,C,D):BinaryRowFrame(A,C,B,D);if(frame is not{} f)continue;
                    try{return ScaledInverse(f.Matrix,f.Determinant,f.XScale,f.YScale,transpose!=0,true);}
                    catch(InvalidOperationException){}
                }
                throw;
            }
        }
        var r = new Affine(D / det, -B / det, -C / det, A / det,
            (B * Y - D * X) / det, (C * X - A * Y) / det);
        return FinishInverse(r);
    }
    Affine ScaledInverse(Affine normalized,double determinant,double xScale,double yScale,bool transpose,bool compensated)
    {
        if(determinant==0||!double.IsFinite(determinant))throw new InvalidOperationException("Singular edit transform");
        try
        {
            var inverse=new Affine(normalized.D/determinant,-normalized.B/determinant,-normalized.C/determinant,normalized.A/determinant,0,0);
            var check=compensated?new Affine(CompensatedProductSum(normalized.A,inverse.A,normalized.B,inverse.C),CompensatedProductSum(normalized.A,inverse.B,normalized.B,inverse.D),CompensatedProductSum(normalized.C,inverse.A,normalized.D,inverse.C),CompensatedProductSum(normalized.C,inverse.B,normalized.D,inverse.D),0,0):normalized*inverse;check.Validate();
            if(Math.Abs(check.A-1)>1e-7||Math.Abs(check.D-1)>1e-7||Math.Abs(check.B)>1e-7||Math.Abs(check.C)>1e-7)throw new InvalidOperationException("Singular edit transform");
            var a=inverse.A/xScale;var b=inverse.B/yScale;var c=inverse.C/xScale;var d=inverse.D/yScale;if(transpose)(b,c)=(c,b);
            return FinishInverse(new(a,b,c,d,-(a*X+b*Y),-(c*X+d*Y)));
        }
        catch(InvalidDataException error){throw new InvalidOperationException("Singular edit transform",error);}
    }
    Affine FinishInverse(Affine candidate)
    {
        if(!double.IsFinite(candidate.X)||!double.IsFinite(candidate.Y))
        {
            var position=(candidate with{X=0,Y=0}).Apply(new(-X,-Y));
            candidate=candidate with{X=double.IsFinite(candidate.X)?candidate.X:position.X,Y=double.IsFinite(candidate.Y)?candidate.Y:position.Y};
        }
        candidate.Validate();return candidate;
    }
    public void Validate()
    {
        if (!double.IsFinite(A)||!double.IsFinite(B)||!double.IsFinite(C)||!double.IsFinite(D)||!double.IsFinite(X)||!double.IsFinite(Y)) throw new InvalidDataException("Nonfinite matrix");
    }
    internal static double DegreeSum(double a,double b)=>Math.Abs(a)<=360&&Math.Abs(b)<=360?a+b:Math.IEEERemainder(a,360)+Math.IEEERemainder(b,360);
    internal static Point DegreeAxis(double degrees)
    {
        var cardinal=Math.IEEERemainder(degrees,360);
        if(cardinal==0)return new(1,0);if(cardinal==90)return new(0,1);
        if(cardinal==-90)return new(0,-1);if(Math.Abs(cardinal)==180)return new(-1,0);
        var radians=(Math.Abs(degrees)>360?cardinal:degrees)*Math.PI/180;return new(Math.Cos(radians),Math.Sin(radians));
    }
    public static Affine Local(double x, double y, double rotation, double sx, double sy, double shx, double shy)
    {
        var u=DegreeAxis(DegreeSum(rotation,shx));var v=DegreeAxis(DegreeSum(DegreeSum(rotation,90),shy));
        return new(u.X*sx,v.X*sy,u.Y*sx,v.Y*sy,x,y);
    }
}

internal static class Curves
{
    internal static double Interpolate(double a,double b,double ratio)
    {
        if(ratio==0)return a;if(ratio==1)return b;
        var value=a+(b-a)*ratio;
        // A difference or extrapolation product can overflow while the final blend is finite.
        return double.IsFinite(value)?value:double.Lerp(a,b,ratio);
    }
    public static double Absolute(double time,double start,double end,double a,double b,double[] controls,int offset,bool forwardTime=false)
    {
        var duration=end-start;if(!double.IsFinite(duration)||duration<=0)throw new InvalidDataException("Curve time interval");
        var x1=(controls[offset]-start)/duration;var x2=(controls[offset+2]-start)/duration;
        var ratio=(time-start)/duration;var t=forwardTime&&(x1<0||x1>1||x2<0||x2>1)?FirstTimeParameter(ratio,x1,x2):Ease(ratio,x1,1d/3,x2,2d/3);var s=1-t;
        var c=controls[offset+1];var d=controls[offset+3];var value=a==b&&a==c&&a==d?a:a*(s*s*s)+c*(3*s*s*t)+d*(3*s*t*t)+b*(t*t*t);
        if(!double.IsFinite(value)&&double.IsFinite(a)&&double.IsFinite(b)&&double.IsFinite(c)&&double.IsFinite(d))
        {
            var ac=Interpolate(a,c,t);var cd=Interpolate(c,d,t);var db=Interpolate(d,b,t);value=Interpolate(Interpolate(ac,cd,t),Interpolate(cd,db,t),t);
        }
        return double.IsFinite(value)?value:throw new InvalidDataException("Absolute curve numeric range");
    }
    static double FirstTimeParameter(double time,double x1,double x2)
    {
        if(!double.IsFinite(time)||!double.IsFinite(x1)||!double.IsFinite(x2)||time<0||time>1)throw new InvalidDataException("Invalid forward Bezier");
        if(time==0||time==1)return time;
        var scale=Math.Max(1,Math.Max(Math.Abs(x1),Math.Abs(x2)));x1/=scale;x2/=scale;var end=1/scale;var target=time/scale;if(target==0)throw new InvalidDataException("Forward Bezier numeric range");
        var a=3*(x1-x2)+end;var b=2*(x2-2*x1);var c=x1;var cuts=new List<double>{0,1};
        void Cut(double value){if(value>0&&value<1&&double.IsFinite(value))cuts.Add(value);}
        if(a==0){if(b!=0)Cut(-c/b);}
        else
        {
            var discriminant=b*b-4*a*c;
            if(discriminant>=0){var q=-.5*(b+Math.CopySign(Math.Sqrt(discriminant),b));Cut(q/a);if(q!=0)Cut(c/q);}
        }
        cuts.Sort();double X(double u){var v=1-u;return 3*v*v*u*x1+3*v*u*u*x2+u*u*u*end;}
        // Independent preview policy: use the first intersection while traversing the original curve; no handle clamping.
        for(var i=0;i+1<cuts.Count;i++)
        {
            var lo=cuts[i];var hi=cuts[i+1];var left=X(lo);var right=X(hi);if(target<Math.Min(left,right)||target>Math.Max(left,right)||lo==hi)continue;
            if(target==left)return lo;if(target==right)return hi;var increasing=right>left;
            for(var step=0;step<56;step++){var mid=(lo+hi)/2;if((X(mid)<target)==increasing)lo=mid;else hi=mid;}return (lo+hi)/2;
        }
        throw new InvalidDataException("Forward Bezier intersection");
    }
    public static (double[] Left,double[] Right) SplitEase(double x,double[] controls)
    {
        if(controls.Length!=4||x<=0||x>=1)throw new ArgumentException("Interior Bezier split required");
        Ease(x,controls[0],controls[1],controls[2],controls[3]);
        Point a=new(),b=new(controls[0],controls[1]),c=new(controls[2],controls[3]),d=new(1,1);
        Point Mix(Point p,Point q,double t)=>p*(1-t)+q*t;
        double lo=0,hi=1;
        for(var i=0;i<56;i++){var mid=(lo+hi)/2;if(Cubic(a,b,c,d,mid).Position.X<x)lo=mid;else hi=mid;}
        var t=(lo+hi)/2;var ab=Mix(a,b,t);var bc=Mix(b,c,t);var cd=Mix(c,d,t);var abc=Mix(ab,bc,t);var bcd=Mix(bc,cd,t);var cut=Mix(abc,bcd,t);
        // shortcut: a shared curve cannot represent a flat endpoint crossing; retain the document until per-component curves exist.
        if(cut.Y==0||cut.Y==1)throw new NotSupportedException("Bezier subdivision has zero value span");
        double[] left=[ab.X/x,ab.Y/cut.Y,abc.X/x,abc.Y/cut.Y];
        double[] right=[(bcd.X-x)/(1-x),(bcd.Y-cut.Y)/(1-cut.Y),(cd.X-x)/(1-x),(cd.Y-cut.Y)/(1-cut.Y)];
        Ease(.5,left[0],left[1],left[2],left[3]);Ease(.5,right[0],right[1],right[2],right[3]);return(left,right);
    }
    public static double Ease(double t, double x1, double y1, double x2, double y2)
    {
        if (!double.IsFinite(t)||!double.IsFinite(x1)||!double.IsFinite(y1)||!double.IsFinite(x2)||!double.IsFinite(y2)|| t < 0 || t > 1
            || x1 < 0 || x1 > 1 || x2 < 0 || x2 > 1) throw new InvalidDataException("Invalid normalized Bezier");
        if (t == 0 || t == 1) return t;
        double Cubic(double s, double a, double b) => 3 * (1 - s) * (1 - s) * s * a + 3 * (1 - s) * s * s * b + s * s * s;
        double lo = 0, hi = 1;
        for (var i = 0; i < 56; i++) { var m = (lo + hi) / 2; if (Cubic(m, x1, x2) < t) lo = m; else hi = m; }
        var value=Cubic((lo + hi) / 2, y1, y2);
        return double.IsFinite(value)?value:throw new InvalidDataException("Bezier numeric range");
    }
    public static (Point Position, Point Tangent) Cubic(Point a, Point b, Point c, Point d, double t)
    {
        if (!double.IsFinite(t) || t < 0 || t > 1) throw new ArgumentOutOfRangeException(nameof(t));
        a.Validate();b.Validate();c.Validate();d.Validate();
        var s = 1 - t;
        var position=a * (s * s * s) + b * (3 * s * s * t) + c * (3 * s * t * t) + d * (t * t * t);
        position=new(a.X==b.X&&a.X==c.X&&a.X==d.X?a.X:position.X,a.Y==b.Y&&a.Y==c.Y&&a.Y==d.Y?a.Y:position.Y);
        var tangent=(b - a) * (3 * s * s) + (c - b) * (6 * s * t) + (d - c) * (3 * t * t);
        var retryPosition=!double.IsFinite(position.X)||!double.IsFinite(position.Y);var retryTangent=!double.IsFinite(tangent.X)||!double.IsFinite(tangent.Y);
        if(retryPosition||retryTangent)
        {
            Point Blend(Point p,Point q)=>new(Interpolate(p.X,q.X,t),Interpolate(p.Y,q.Y,t));
            var ab=Blend(a,b);var bc=Blend(b,c);var cd=Blend(c,d);var abc=Blend(ab,bc);var bcd=Blend(bc,cd);
            if(retryPosition)position=Blend(abc,bcd);
            if(retryTangent)tangent=(bcd-abc)*3;
        }
        position.Validate();tangent.Validate();return (position,tangent);
    }
    public static (double Root, double Elbow) TwoLink(double first, double second, Point target, bool positive)
    {
        target.Validate();
        if (!double.IsFinite(first) || !double.IsFinite(second) || first <= 0 || second <= 0) throw new InvalidDataException("Invalid IK lengths");
        // A common scale cancels out of the cosine law and keeps squares/products in range.
        var scale=Math.Max(Math.Max(first,second),Math.Max(Math.Abs(target.X),Math.Abs(target.Y)));
        var a=first/scale;var b=second/scale;var x=target.X/scale;var y=target.Y/scale;var distance=double.Hypot(x,y);
        var cosine=distance>=a+b?1:distance<=Math.Abs(a-b)?-1:Math.Clamp((x*x+y*y-a*a-b*b)/(2*a*b),-1,1);
        var elbow = Math.Acos(cosine) * (positive ? 1 : -1);
        var root = Math.Atan2(target.Y, target.X) - Math.Atan2(b * Math.Sin(elbow), a + b * Math.Cos(elbow));
        if (!double.IsFinite(root) || !double.IsFinite(elbow)) throw new InvalidDataException("IK numeric range");
        return (root * 180 / Math.PI, elbow * 180 / Math.PI);
    }
}

internal readonly record struct Vertex(Point Position, Point UV)
{
    public static Vertex Lerp(Vertex a, Vertex b, double t)
    {
        a.Position.Validate();a.UV.Validate();b.Position.Validate();b.UV.Validate();
        var position=new Point(Curves.Interpolate(a.Position.X,b.Position.X,t),Curves.Interpolate(a.Position.Y,b.Position.Y,t));
        var uv=new Point(Curves.Interpolate(a.UV.X,b.UV.X,t),Curves.Interpolate(a.UV.Y,b.UV.Y,t));
        var result = new Vertex(position,uv);
        result.Position.Validate(); result.UV.Validate(); return result;
    }
}

internal static class Polygons
{
    static Point Relative(Point point,Point origin,double scale)=>new((point.X-origin.X)/scale,(point.Y-origin.Y)/scale);
    static (Point Origin,double Scale,IReadOnlyList<Point> Points,double Extent) PredicateFrame(IReadOnlyList<Point> polygon)
    {
        var origin=polygon[0];origin.Validate();double extent=0;
        for(var i=0;i<polygon.Count;i++){var point=polygon[i];point.Validate();extent=Math.Max(extent,Math.Max(Math.Abs(point.X-origin.X),Math.Abs(point.Y-origin.Y)));}
        if(!(extent>0&&extent<1e-150))return(new(),1,polygon,extent);
        // Keep tiny predicate products out of the subnormal range without rounding by an arbitrary scale.
        var scale=Math.ScaleB(1,Math.ILogB(extent));var points=new Point[polygon.Count];for(var i=0;i<points.Length;i++)points[i]=Relative(polygon[i],origin,scale);return(origin,scale,points,extent);
    }
    internal static bool IsCollinear(IReadOnlyList<Point> polygon)
    {
        if(polygon.Count==0)return true;var frame=PredicateFrame(polygon);var points=frame.Points;var origin=points[0];Point direction=new();double size=0;
        for(var i=0;i<points.Count;i++)
        {
            var delta=points[i]-origin;var next=Math.Max(Math.Abs(delta.X),Math.Abs(delta.Y));if(next<=size)continue;direction=delta;size=next;if(frame.Scale==1)break;
        }
        for(var i=0;i<points.Count;i++)if(Point.Cross(direction,points[i]-origin)!=0)return false;return true;
    }
    public static double SignedArea(IReadOnlyList<Point> p)
    {
        double sum = 0;
        for (var i = 1; i + 1 < p.Count; i++) sum += Point.Cross(p[i] - p[0], p[i + 1] - p[0]);
        if(!double.IsFinite(sum))throw new InvalidDataException("Area numeric range");
        return sum / 2;
    }
    internal static int Winding(IReadOnlyList<Point> polygon)
    {
        var corner=Enumerable.Range(0,polygon.Count).OrderBy(i=>polygon[i].X).ThenBy(i=>polygon[i].Y).First();
        return Point.Orientation(polygon[(corner+polygon.Count-1)%polygon.Count],polygon[corner],polygon[(corner+1)%polygon.Count]);
    }
    public static int[] Triangulate(IReadOnlyList<Point> polygon,int workLimit=20_000_000)
    {
        if(workLimit is <0 or >20_000_000)throw new ArgumentOutOfRangeException(nameof(workLimit));
        if (polygon.Count < 3 || polygon.Count > 4096) throw new InvalidDataException("Polygon size");
        var unique=new HashSet<Point>();foreach(var point in polygon){point.Validate();if(!unique.Add(point))throw new InvalidDataException("Repeated polygon point");}
        var work=workLimit;void Consume(){if(--work<0)throw new InvalidDataException("Polygon work limit");}
        bool Between(Point a, Point b, Point p) => Point.Orientation(a,b,p) == 0
            && p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) && p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);
        for (var i = 0; i < polygon.Count; i++)
        for (var j = i + 1; j < polygon.Count; j++)
        {
            var nextI = (i + 1) % polygon.Count; var nextJ = (j + 1) % polygon.Count;
            if (nextI == j || nextJ == i) continue;
            Consume();
            var a = polygon[i]; var b = polygon[nextI]; var c = polygon[j]; var d = polygon[nextJ];
            var x = Point.Orientation(a,b,c); var y = Point.Orientation(a,b,d);
            var z = Point.Orientation(c,d,a); var w = Point.Orientation(c,d,b);
            if ((x*y<0 && z*w<0)
                || Between(a, b, c) || Between(a, b, d) || Between(c, d, a) || Between(c, d, b))
                throw new InvalidDataException("Self-intersecting polygon");
        }
        var sign = Winding(polygon);
        if (sign == 0) throw new InvalidDataException("Zero-area polygon");
        var ring = Enumerable.Range(0, polygon.Count).ToList(); var triangles = new List<int>();
        while (ring.Count > 3)
        {
            var found = false;
            for (var i = 0; i < ring.Count; i++)
            {
                Consume();
                var a = ring[(i + ring.Count - 1) % ring.Count]; var b = ring[i]; var c = ring[(i + 1) % ring.Count];
                if (sign * Point.Orientation(polygon[a],polygon[b],polygon[c]) <= 0) continue;
                bool InTriangle(int k) {Consume();return sign * Point.Orientation(polygon[a],polygon[b],polygon[k]) >= 0
                    && sign * Point.Orientation(polygon[b],polygon[c],polygon[k]) >= 0
                    && sign * Point.Orientation(polygon[c],polygon[a],polygon[k]) >= 0;}
                if (ring.Any(k => k != a && k != b && k != c && InTriangle(k))) continue;
                triangles.AddRange([a, b, c]); ring.RemoveAt(i); found = true; break;
            }
            if (!found) throw new InvalidDataException("Polygon cannot be triangulated without dropping vertices");
        }
        if(sign*Point.Orientation(polygon[ring[0]],polygon[ring[1]],polygon[ring[2]])<=0)throw new InvalidDataException("Degenerate polygon triangle");
        triangles.AddRange(ring); return triangles.ToArray();
    }
    static (Point Direction,double Offset) EdgeLine(Point a,Point b)
    {
        var reverse=a.X>b.X||a.X==b.X&&a.Y>b.Y;if(reverse)(a,b)=(b,a);
        var edge=b-a;var size=Math.Max(Math.Abs(edge.X),Math.Abs(edge.Y));if(size==0||!double.IsFinite(size))throw new InvalidDataException("Clip edge numeric range");
        var direction=new Point(edge.X/size,edge.Y/size);var offset=Point.Cross(direction,a);return reverse?(direction*-1,-offset):(direction,offset);
    }
    static double StableDistance(Point a,Point b,Point point)
    {
        var line=EdgeLine(a,b);var value=Point.Cross(line.Direction,point)-line.Offset;
        return double.IsFinite(value)?value:throw new InvalidDataException("Clip distance numeric range");
    }
    static Point StableIntersection(Point a,Point b,Point s,Point e)
    {
        if(a.X>b.X||a.X==b.X&&a.Y>b.Y)(a,b)=(b,a);if(s.X>e.X||s.X==e.X&&s.Y>e.Y)(s,e)=(e,s);
        var line=EdgeLine(a,b);var nx=-line.Direction.Y;var ny=line.Direction.X;var delta=e-s;delta.Validate();Point point;
        if(Math.Abs(delta.X)>=Math.Abs(delta.Y))
        {
            var slope=delta.Y/delta.X;var intercept=Math.FusedMultiplyAdd(-slope,s.X,s.Y);var x=Math.FusedMultiplyAdd(-ny,intercept,line.Offset)/Math.FusedMultiplyAdd(ny,slope,nx);point=new(x,Math.FusedMultiplyAdd(slope,x,intercept));
        }
        else
        {
            var slope=delta.X/delta.Y;var intercept=Math.FusedMultiplyAdd(-slope,s.Y,s.X);var y=Math.FusedMultiplyAdd(-nx,intercept,line.Offset)/Math.FusedMultiplyAdd(nx,slope,ny);point=new(Math.FusedMultiplyAdd(slope,y,intercept),y);
        }
        point.Validate();return point;
    }
    // shortcut: Double predicates for small simple polygons; exact predicates and general degeneracy remain separate validation work.
    public static List<Vertex[]> ClipTriangle(Vertex[] triangle, IReadOnlyList<Point> mask)
        =>ClipTriangle(triangle,mask,Triangulate(mask));
    internal static List<Vertex[]> ClipTriangle(Vertex[] triangle,IReadOnlyList<Point> mask,int[] indices)
    {
        if (triangle.Length != 3) throw new ArgumentException("Three subject vertices required");
        foreach (var p in triangle) { p.Position.Validate(); p.UV.Validate(); }
        if(indices.Length%3!=0||indices.Any(i=>i<0||i>=mask.Count))throw new InvalidDataException("Clip cell indices");
        double maskExtent=0,maskScale=1;IReadOnlyList<Point> predicateMask=mask;if(indices.Length>0)
        {
            var frame=PredicateFrame(mask);maskExtent=frame.Extent;maskScale=frame.Scale;predicateMask=frame.Points;
        }
        var origin=triangle[0];var ab=triangle[1].Position-origin.Position;var ac=triangle[2].Position-origin.Position;var determinant=Point.Cross(ab,ac);double subjectScale=1;
        var subjectExtent=Math.Max(Math.Max(Math.Abs(ab.X),Math.Abs(ab.Y)),Math.Max(Math.Abs(ac.X),Math.Abs(ac.Y)));var stableDistances=maskScale!=1||subjectExtent>0&&maskExtent>0&&(subjectExtent/maskExtent<1e-8||maskExtent/subjectExtent<1e-8);
        if(determinant==0)
        {
            if(!(subjectExtent>0&&subjectExtent<1e-150))return [];
            var frame=PredicateFrame([triangle[0].Position,triangle[1].Position,triangle[2].Position]);subjectScale=frame.Scale;ab=frame.Points[1];ac=frame.Points[2];determinant=Point.Cross(ab,ac);if(determinant==0)return [];
        }
        var largeUv=triangle.Any(v=>Math.Abs(v.UV.X)>1e8||Math.Abs(v.UV.Y)>1e8);
        double UvPlane(double aValue,double bValue,double cValue,double value,Point point)
        {
            var magnitude=Math.Max(Math.Abs(aValue),Math.Max(Math.Abs(bValue),Math.Abs(cValue)));if(magnitude<=1e8||double.IsFinite(value)&&Math.Abs(value)>=magnitude*1e-8)return value;
            var scale=Math.ScaleB(1,Math.ILogB(magnitude));var a=aValue/scale;var b=bValue/scale;var c=cValue/scale;
            var dx=Numeric.Derivative(a,b,c,ac.Y,ab.Y,1,determinant);var dy=Numeric.Derivative(a,c,b,ab.X,ac.X,1,determinant);var map=new Affine(dx,dy,0,0,a,0);var mapOrigin=new Point(origin.Position.X/subjectScale,origin.Position.Y/subjectScale);var offset=map.Apply(mapOrigin*-1).X;
            var result=(map with{X=offset}).Apply(point).X*scale;return double.IsFinite(result)?result:throw new InvalidDataException("Clip UV numeric range");
        }
        Vertex Canonical(Vertex vertex)
        {
            foreach(var source in triangle)if(vertex.Position==source.Position)return source;
            var delta=subjectScale==1?vertex.Position-origin.Position:Relative(vertex.Position,origin.Position,subjectScale);var b=Point.Cross(delta,ac)/determinant;var c=Point.Cross(ab,delta)/determinant;
            var uv=origin.UV*(1-b-c)+triangle[1].UV*b+triangle[2].UV*c;
            if(largeUv){var point=new Point(vertex.Position.X/subjectScale,vertex.Position.Y/subjectScale);uv=new(UvPlane(origin.UV.X,triangle[1].UV.X,triangle[2].UV.X,uv.X,point),UvPlane(origin.UV.Y,triangle[1].UV.Y,triangle[2].UV.Y,uv.Y,point));}
            uv.Validate();return vertex with{UV=uv};
        }
        var pieces = new List<Vertex[]>();
        for (var cell = 0; cell < indices.Length; cell += 3)
        {
            var boundary = new[] { mask[indices[cell]], mask[indices[cell + 1]], mask[indices[cell + 2]] };
            var sign = Math.Sign(SignedArea(maskScale==1?boundary:new[]{predicateMask[indices[cell]],predicateMask[indices[cell+1]],predicateMask[indices[cell+2]]}));if(sign==0)throw new InvalidDataException("Degenerate clipping cell");var output = triangle.Select((vertex,i)=>(Vertex:vertex,Edge:(i+2)%3)).ToList();
            for (var edge = 0; edge < 3 && output.Count > 0; edge++)
            {
                var input = output; output = [];
                void Add(Vertex v,int incomingEdge)
                {
                    // Every cell clips the same source triangle; derive its unique UV instead of accumulating edge-rounding differences.
                    v=Canonical(v);
                    if (output.Count == 0 || output[^1].Vertex.Position != v.Position) output.Add((v,incomingEdge));
                    else if (output[^1].Vertex.UV != v.UV) throw new InvalidDataException("Duplicate XY has conflicting UV");
                }
                var a = boundary[edge]; var b = boundary[(edge + 1) % 3];
                var s = input[^1]; var ds = sign * (!stableDistances&&subjectScale==1?Point.Cross(b-a,s.Vertex.Position-a):StableDistance(a,b,s.Vertex.Position));
                foreach (var e in input)
                {
                    var de = sign * (!stableDistances&&subjectScale==1?Point.Cross(b-a,e.Vertex.Position-a):StableDistance(a,b,e.Vertex.Position));
                    if ((ds >= 0) != (de >= 0))
                    {
                        var t=ds/(ds-de);if(!double.IsFinite(t)||t<0||t>1)throw new InvalidDataException("Invalid clip intersection");
                        var incomingEdge=ds<0?edge+3:e.Edge;
                        if((stableDistances||subjectScale!=1)&&ds!=0&&de!=0)
                        {
                            // Rounded intermediate endpoints can lose a tiny edge offset; intersect the original supporting lines.
                            var start=e.Edge<3?triangle[e.Edge].Position:boundary[e.Edge-3];var end=e.Edge<3?triangle[(e.Edge+1)%3].Position:boundary[(e.Edge-2)%3];
                            Add(new(StableIntersection(a,b,start,end),new()),incomingEdge);
                        }
                        else Add(Vertex.Lerp(s.Vertex,e.Vertex,t),incomingEdge);
                    }
                    if (de >= 0) Add(e.Vertex,e.Edge);
                    s = e; ds = de;
                }
                if (output.Count > 1 && output[0].Vertex.Position == output[^1].Vertex.Position)
                {
                    if (output[0].Vertex.UV != output[^1].Vertex.UV) throw new InvalidDataException("Closing UV mismatch");
                    output[0]=(output[0].Vertex,output[^1].Edge);output.RemoveAt(output.Count - 1);
                }
            }
            if(output.Count>=3)
            {
                var points=output.Select(v=>v.Vertex.Position).ToArray();
                var area=SignedArea(points);if(area==0)area=SignedArea(PredicateFrame(points).Points);if(area!=0)pieces.Add(output.Select(v=>v.Vertex).ToArray());
            }
        }
        return pieces;
    }
}










