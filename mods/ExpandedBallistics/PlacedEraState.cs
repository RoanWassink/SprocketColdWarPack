namespace SprocketExpandedBallistics;
// Remembers only the ownership keys needed for scoped reset. Spent authority stays in EraCells.
internal sealed class PlacedEraState
{
    private readonly EraCells owned=new();
    internal static EraCell Key(long spawn,int element)=>new(spawn,element,0,0,0);
    internal static EraCell Key(long spawn,int element,int surface,int x,int y)=>new(spawn,element,surface,x,y);
    internal void Track(EraCell cell)=>owned.Consume(cell);
    internal bool Spent(EraCells shared,long spawn,int element)=>shared.Contains(Key(spawn,element));
    internal bool Spent(EraCells shared,long spawn,int element,int surface,int x,int y)
        =>shared.Contains(Key(spawn,element,surface,x,y));
    internal EraCell[] SpentCells(EraCells shared,long spawn,int element)=>shared.Cells(spawn,element);
    internal void Reset(EraCells shared)
    {foreach(var cell in owned.Snapshot())shared.Remove(cell);owned.Clear();}
    internal void RemoveSpawn(long spawn)=>owned.RemoveSpawn(spawn);
    internal void RemoveElement(long spawn,int element)=>owned.RemoveElement(spawn,element);
}
