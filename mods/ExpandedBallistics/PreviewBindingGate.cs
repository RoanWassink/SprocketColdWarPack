namespace SprocketExpandedBallistics;
// Native context signatures include owner, era, date and enabled technologies.
internal sealed class PreviewBindingGate
{
 private readonly Dictionary<IntPtr,string> signatures=new();
 internal bool Changed(IntPtr overlay,string? signature)
 {
  if(signature==null)return signatures.Remove(overlay);
  if(signatures.TryGetValue(overlay,out var previous)&&previous==signature)return false;
  signatures[overlay]=signature;return true;
 }
 internal void Clear()=>signatures.Clear();
 internal static bool OwnerAllowed(bool factoryBound,IntPtr overlay,IntPtr current,bool requireActive,IntPtr expectedOwner,IntPtr actualOwner)=>
  expectedOwner!=IntPtr.Zero&&expectedOwner==actualOwner&&((factoryBound&&!requireActive)||ExactOverlay(overlay,current));
 internal static bool ExactOverlay(IntPtr expected,IntPtr current)=>expected!=IntPtr.Zero&&expected==current;
 internal static bool Inspect(long now,long nextCheck,bool identityChanged)=>identityChanged||now>=nextCheck;
 internal static long NextCheck(long now)=>now+250;

}
