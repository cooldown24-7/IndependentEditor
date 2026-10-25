namespace IndependentDev;

internal readonly record struct LocalPose(Point Translation, double Rotation, Point Scale, Point Shear = default)
{
    public Affine Matrix => Affine.Local(Translation.X, Translation.Y, Rotation, Scale.X, Scale.Y, Shear.X, Shear.Y);
}
internal static partial class Constraints
{
    public static double Aim(Point origin, Point target)
    {
        origin.Validate(); target.Validate(); var delta = target - origin; delta.Validate();
        if (delta == new Point()) throw new InvalidDataException("Undefined aim for coincident target");
        return Math.Atan2(delta.Y, delta.X) * 180 / Math.PI;
    }
}
