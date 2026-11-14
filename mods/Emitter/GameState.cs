using UnityEngine;
using Sprocket.GameControl;
using Sprocket;
using Object=UnityEngine.Object;
namespace SprocketEmitter;
internal static class GameState
{
    private static GameController? controller;
    private static float next;
    internal static bool Paused() {
        if(controller==null||controller.WasCollected) {
            if(Time.unscaledTime>=next){next=Time.unscaledTime+2;var found=Object.FindObjectsByType<GameController>(FindObjectsSortMode.None);controller=found.Length>0?found[0]:null;}
        }
        if(controller!=null)try{return controller.PauseState!=PauseState.Unpaused||Time.timeScale<=0;}catch{}
        return Time.timeScale<=0;
    }
    internal static void Reset(){controller=null;next=0;}
}
