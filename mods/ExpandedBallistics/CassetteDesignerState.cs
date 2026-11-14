namespace SprocketExpandedBallistics;

internal enum DesignerTransition{None,Entered,Left,Restarted}

/// <summary>Scene activation is fresh evidence; a previous Play notification cannot latch out the next Designer.</summary>
internal sealed class CassetteDesignerState
{
    internal bool Active{get;private set;}
    internal bool? Manual{get;private set;}
    private bool surface,blocked;
    private int scene;
    internal void PlayerState(bool designer)=>blocked=!designer;
    internal DesignerTransition Observe(bool activeSurface,int sceneHandle)
    {
        bool fresh=activeSurface&&(!surface||scene!=sceneHandle);
        if(fresh)blocked=false;
        bool next=activeSurface&&!blocked;
        var transition=next?Active?scene!=sceneHandle?DesignerTransition.Restarted:DesignerTransition.None:DesignerTransition.Entered:
            Active?DesignerTransition.Left:DesignerTransition.None;
        surface=activeSurface;scene=sceneHandle;Active=next;
        if(transition!=DesignerTransition.None)Manual=null;
        return transition;
    }
    internal bool Visible(bool disabled,bool automatic)=>Active&&!disabled&&(Manual??automatic);
    internal void Toggle(bool automatic,bool? show=null){if(Active)Manual=show??!(Manual??automatic);}
    internal void ClearManual()=>Manual=null;
    internal void Reset(){Active=false;Manual=null;surface=false;blocked=false;scene=0;}
}
