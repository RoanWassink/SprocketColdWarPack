namespace SprocketExpandedBallistics;

// Recipe identity is separate from the temporary resistance native is about to charge.
internal readonly record struct MaterialFingerprint(float Density,float RhaFactor,float SpallFactor)
{
    internal static MaterialFingerprint BeforeWrites(IntPtr array,int index,MaterialFingerprint live,IEnumerable<MaterialFingerprintWrite> writes)
    {
        // Oldest first: a nested simulation may share the same native array.
        foreach(var write in writes)
        {
            if(write.Array==array&&write.Index==index)return write.Original;
            if(write.Extra!=IntPtr.Zero&&write.Extra==array&&write.Index==index)return write.ExtraOriginal;
        }
        return live;
    }
}
internal readonly record struct MaterialFingerprintWrite(IntPtr Array,int Index,MaterialFingerprint Original,IntPtr Extra,MaterialFingerprint ExtraOriginal);
