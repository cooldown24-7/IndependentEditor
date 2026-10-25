namespace IndependentDev;

internal static class PoseMath
{
    internal static Affine Inherited(Affine parent,string mode,double rotation)
    {
        parent=parent with{X=0,Y=0};if(mode=="normal")return parent;if(mode=="onlyTranslation")return Affine.Identity;
        var determinant=parent.DeterminantSign();
        if(determinant==0)throw new InvalidOperationException("Singular inheritance parent");
        if(mode is "noScale" or "noScaleOrReflection")
        {
            var axis=parent.Apply(Affine.DegreeAxis(rotation));var length=axis.Length;
            if(length==0||!double.IsFinite(length))throw new InvalidDataException("Inheritance axis range");axis=new(axis.X/length,axis.Y/length);
            var sign=mode=="noScaleOrReflection"?1:Math.Sign(determinant);return new(axis.X,-axis.Y*sign,axis.Y,axis.X*sign,0,0);
        }
        if(mode=="noRotationOrReflection")
        {
            var sx=double.Hypot(parent.A,parent.C);if(sx==0)throw new InvalidOperationException("Singular inheritance parent");if(!double.IsFinite(sx))throw new InvalidDataException("Inheritance axis range");
            var x=parent.A/sx;var y=parent.C/sx;var projection=x*parent.B+y*parent.D;var height=-y*parent.B+x*parent.D;
            // A rounded normalized axis can erase a nonzero QR height.
            if(height==0)height=parent.DeterminantOver(sx)??throw new InvalidDataException("Inheritance axis range");
            return new(sx,projection*Math.Sign(height),0,Math.Abs(height),0,0);
        }
        throw new NotSupportedException("Bone inheritance "+mode);
    }
    public static void Rebuild(Joint[] bones,LocalPose[] locals,Affine[] world,bool[]? active=null)
    {
        for(var i=0;i<bones.Length;i++)
        {
            if(active is not null&&!active[i]){world[i]=Affine.Identity;continue;}
            var bone=bones[i];var local=locals[i].Matrix;
            if(bone.Parent<0)world[i]=local;
            else
            {
                var parent=world[bone.Parent];var position=parent.Apply(locals[i].Translation);var noScale=bone.Transform is "noScale" or "noScaleOrReflection";
                var basis=Inherited(parent,bone.Transform,locals[i].Rotation);var linear=(noScale?locals[i] with{Translation=new(),Rotation=0}:locals[i] with{Translation=new()}).Matrix;
                world[i]=basis*linear;world[i]=world[i] with{X=position.X,Y=position.Y};
            }
            world[i].Validate();
        }
    }
    public static LocalPose Decompose(Affine matrix)
    {
        matrix.Validate();var sx=double.Hypot(matrix.A,matrix.C);var sy=double.Hypot(matrix.B,matrix.D);
        if(!double.IsFinite(sx)||!double.IsFinite(sy))throw new InvalidDataException("Constraint matrix range");
        if(matrix.DeterminantSign()<0)sy=-sy;
        var angle=sx==0?Math.Atan2(-matrix.B,matrix.D)*180/Math.PI:Math.Atan2(matrix.C,matrix.A)*180/Math.PI;
        var shear=sy==0?0:Math.IEEERemainder(Math.Atan2(matrix.D*Math.Sign(sy),matrix.B*Math.Sign(sy))*180/Math.PI-angle-90,360);
        return new(new(matrix.X,matrix.Y),angle,new(sx,sy),new(0,shear));
    }
    public static LocalPose FromWorld(Joint bone,Affine matrix,Affine[] world)
    {
        if(bone.Parent<0)return Decompose(matrix);
        var parent=world[bone.Parent];
        if(bone.Transform=="normal")return Decompose(parent.Inverse()*matrix);
        if(bone.Transform=="onlyTranslation")
        {var position=parent.Inverse().Apply(new(matrix.X,matrix.Y));return Decompose(matrix with{X=position.X,Y=position.Y});}
        var positionAdvanced=parent.Inverse().Apply(new(matrix.X,matrix.Y));double rotation=0;
        if(bone.Transform is "noScale" or "noScaleOrReflection")
        {var direction=(parent with{X=0,Y=0}).Inverse().Apply(new(matrix.A,matrix.C));rotation=Math.Atan2(direction.Y,direction.X)*180/Math.PI;}
        var basis=Inherited(parent,bone.Transform,rotation);var local=Decompose(basis.Inverse()*(matrix with{X=0,Y=0}));
        return local with{Translation=positionAdvanced,Rotation=local.Rotation+rotation};
    }
}
