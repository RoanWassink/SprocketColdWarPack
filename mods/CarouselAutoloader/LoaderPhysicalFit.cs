namespace SprocketCarouselAutoloader;

// Only converts canonical API dimensions to native packer units; contains no packing formula.
internal static class LoaderPhysicalFit
{
    internal static bool Valid(int status,double projectile,double charge,double diameter) =>
        (status==0||status==1)&&double.IsFinite(projectile)&&projectile>0&&double.IsFinite(charge)&&charge>=0&&
        double.IsFinite(diameter)&&diameter>0;
    internal static bool NativePackDimensions(double projectile,double charge,double diameter,out ushort nativeDiameter,out ushort nativeLength)
    {
        nativeDiameter=nativeLength=0;
        if(!Valid(1,projectile,charge,diameter)||diameter>ushort.MaxValue||projectile+charge>ushort.MaxValue) return false;
        nativeDiameter=(ushort)Math.Ceiling(diameter);nativeLength=(ushort)Math.Ceiling(projectile+charge);
        return true;
    }
}
