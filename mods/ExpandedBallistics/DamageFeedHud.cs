using UnityEngine;
namespace SprocketExpandedBallistics;
public sealed class DamageFeedBehaviour:MonoBehaviour
{
    public DamageFeedBehaviour(IntPtr pointer):base(pointer){}
    public void LateUpdate(){try{RuntimeCassettePlateVisual.FollowVisible();}catch{}}
    public void Update(){try{RuntimeAmmoLoadout.UpdateEditor();}catch{}try{AtgmLoadedRoundVisual.Update();}catch{}try{RuntimeTopAttackHud.Update();}catch(Exception ex){try{RuntimeTopAttackHud.Failed(ex);}catch{}}try{RuntimeAtgmSightHud.Update();}catch(Exception ex){try{RuntimeAtgmSightHud.Failed(ex);}catch{}}CombatReportMenu.Update();ArmourComparisonMenu.Update();try{DamageFeedHud.Update();}catch(Exception ex){DamageFeedHud.Failed(ex);}try{CassetteComposerPanel.Update();}catch{}try{RuntimeCassettePlateVisual.Tick();}catch{}try{PlateMeshAccess.Tick();}catch{}}
}
/// <summary>The damage feed HUD is now the UI Kit hit panel (<see cref="HitPanelHud"/>, wave 2b); the TMP feed is removed.</summary>
internal static class DamageFeedHud
{
    internal static void Failed(Exception ex)=>HitPanelHud.Failed(ex);
    internal static void Destroy()=>HitPanelHud.Destroy();
    internal static void Update()=>HitPanelHud.Update();
}
