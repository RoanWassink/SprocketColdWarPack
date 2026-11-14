using System.Numerics;
namespace SprocketExpandedBallistics;

internal static class CassetteImpactPathTests
{
    internal static void Run(Action<bool,string> check,IReadOnlyDictionary<string,CassetteMaterial> materials,MaterialReactionCatalogue reactions)
    {
        var front=CassetteImpactPath.ResolveSide(-Vector3.UnitZ,new[]{Vector3.UnitZ});
        check(front.Side==CassetteSide.Front&&front.FaceIndex==0&&!front.Edge&&Vector3.Distance(front.FrontNormal,Vector3.UnitZ)<1e-6,"front face from a shot opposing the drawn outward normal");
        var back=CassetteImpactPath.ResolveSide(Vector3.UnitZ,new[]{Vector3.UnitZ});
        check(back.Side==CassetteSide.Back&&back.FaceIndex==0&&!back.Edge&&Vector3.Distance(back.FrontNormal,Vector3.UnitZ)<1e-6,"back face when the shot travels with the outward normal");
        var slanted=CassetteImpactPath.ResolveSide(-Vector3.Normalize(new Vector3(.2f,0,1)),new[]{Vector3.Normalize(new Vector3(.2f,0,1))});
        check(slanted.Side==CassetteSide.Front&&slanted.Alignment>=.7&&!slanted.Edge,"a bent face at 11 degrees still counts as the front");
        var edge=CassetteImpactPath.ResolveSide(Vector3.UnitZ,new[]{Vector3.UnitX});
        check(edge.Side==CassetteSide.Front&&edge.Edge&&edge.FaceIndex==0,"a side/edge hit is treated as front with the closest face");
        var unreadable=CassetteImpactPath.ResolveSide(Vector3.UnitZ,null);
        check(unreadable.Side==CassetteSide.Front&&unreadable.Edge,"unreadable mesh faces fall back to the front");

        var awarded=new HashSet<(IntPtr Sim,short Lineage,int Entity,int Face)>();
        var sim=new IntPtr(4242);
        check(CassetteImpactPath.Claim(awarded,sim,7,99),"first cassette hit on one plate is awarded");
        check(!CassetteImpactPath.Claim(awarded,sim,7,99),"a second hit of the same penetrator line on the same plate is not counted twice");
        check(CassetteImpactPath.Claim(awarded,sim,8,99),"another penetrator line still pays");
        check(CassetteImpactPath.Claim(awarded,sim,7,100),"another plate still pays");

    }
}
