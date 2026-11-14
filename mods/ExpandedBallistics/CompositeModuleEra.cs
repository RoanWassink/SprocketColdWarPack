using SprocketExpandedBallistics.Era;
namespace SprocketExpandedBallistics;

// Component-local adapter. The response catalogue, cell ledger, native reduction,
// continuation propagation and notification path remain the existing placed ERA path.
internal sealed record CompositeModuleEra(bool Enabled=true,string ResponseId="kontakt5Active",string MaterialId="kontakt5Active",
    double ActiveThicknessMm=13,double ActiveDensity=3171.788731181019,double ActiveRhaFactor=.15,double ActiveSpallFactor=.25,
    double HousingWallMm=2,double CoverDepthMm=50,double PanelWidthM=.294,double PanelLengthM=.292,int Columns=2,int Rows=3)
{
    internal double PanelArea=>Columns*Rows*PanelWidthM*PanelLengthM;
    internal double HousingVolume=>Columns*Rows*(PanelWidthM*PanelLengthM*CoverDepthMm*.001-
        (PanelWidthM-2*HousingWallMm*.001)*(PanelLengthM-2*HousingWallMm*.001)*(CoverDepthMm-2*HousingWallMm)*.001);
    internal double HousingMass=>HousingVolume*7850;
    internal double CellMass=>PanelArea*ActiveThicknessMm*.001*ActiveDensity;
    internal double PassiveCoverEquivalentMm=>(HousingMass/7.85+PanelArea*ActiveThicknessMm*ActiveRhaFactor)/CompositeModule.AreaM2;
    internal bool Valid=>!string.IsNullOrWhiteSpace(ResponseId)&&!string.IsNullOrWhiteSpace(MaterialId)&&Columns==2&&Rows==3&&
        new[]{ActiveThicknessMm,ActiveDensity,ActiveRhaFactor,ActiveSpallFactor,HousingWallMm,CoverDepthMm,PanelWidthM,PanelLengthM}.All(x=>double.IsFinite(x)&&x>0)&&
        PanelWidthM>2*HousingWallMm*.001&&PanelLengthM>2*HousingWallMm*.001&&CoverDepthMm>2*HousingWallMm&&ActiveThicknessMm<=CoverDepthMm-2*HousingWallMm;
    internal EraPartBinding Binding=>new(CompositeModule.ComponentId,"compositeSideModuleBacking",MaterialId,ResponseId,"heavyEra");
}

internal static class CompositeModuleEraGeometry
{
    // Six authored cover panels, transformed by Rz(+90). Cells stretch with their
    // panel in X/Z, with signed inverse coordinates retaining exact mirror identity.
    internal static bool TryCell(double x,double z,out int column,out int row)
    {
        column=x>=-.306&&x<=-.012?0:x>=0&&x<=.294?1:-1;
        row=z>=-.444&&z<=-.152?0:z>=-.14&&z<=.152?1:z>=.164&&z<=.456?2:-1;
        return column>=0&&row>=0;
    }
    internal static bool Exterior(double normalY,double directionY)=>normalY>.5&&directionY<0;
    internal static EraCellLayout Layout(double x,double z)=>new(-.306*Math.Abs(x),-.444*Math.Abs(z),.3*Math.Abs(x),2,3,.001,.304*Math.Abs(z));
    internal static double PassiveFactor(CompositeModulePreset p,bool heat)
        =>!p.Era.Enabled?p.Factor(heat):(p.EquivalentMm(heat)-p.Layers.Where(l=>l.Name=="passive cover plates").Sum(l=>l.ThicknessMm*l.Density/7850*(heat?l.HeatMassEfficiency:l.KeMassEfficiency))+p.Era.PassiveCoverEquivalentMm)/CompositeModule.DepthMm;
    internal static double Mass(CompositeModulePreset p,double x=1,double y=1,double z=1)
        =>(!p.Era.Enabled?p.Mass():p.Mass()-CompositeModule.AreaM2*p.Layers.Where(l=>l.Name=="passive cover plates").Sum(l=>l.ThicknessMm*.001*l.Density)+p.Era.HousingMass+p.Era.CellMass)*Math.Abs(x*z);
    internal static CompositeModulePreset Effective(CompositeModulePreset p)
    {
        if(!p.Era.Enabled)return p;
        // The unchanged native envelope is an interval-weighted passive block:
        // composite/backplate + thin steel cover walls + the active cell's native
        // passive recipe. Reaction is paid before that block by RuntimeArmourResponses.
        var e=p.Era;var equivalent=e.PassiveCoverEquivalentMm;
        var density=(e.HousingMass+e.CellMass)/CompositeModule.AreaM2;
        return p with{Layers=p.Layers.Where(l=>l.Name!="passive cover plates").Append(new CompositeModuleLayer("ERA housing and passive cell",1,density*1000,equivalent*7.85/density,equivalent*7.85/density)).ToArray()};
    }
}
