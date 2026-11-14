using SprocketExpandedBallistics.Era;

namespace SprocketExpandedBallistics.Materials;

// 0.22.73: the four Kontakt-5 variants on War Thunder T-90A sizes (turret L, turret S, hull skirt, glacis) are ordinary
// placed ERA parts: two reactive cells and a shared housing, the existing Kontakt-5 recipe (kontakt5Active) and the
// existing spent-housing look. The presets are rows in EraGeneratedDefinitions; this class only adds their exact
// component bindings to the immutable catalogue that is read from sprocket.expandedballistics.era.bindings.json, because that file in
// the config folder is user data and is never rewritten. A binding that the file already holds is left alone.
internal static class EraWtVariants
{
    internal static readonly string[] Ids={"kontakt5WtTurretL","kontakt5WtTurretS","kontakt5WtSkirt","kontakt5WtGlacis"};
    internal const string Material="kontakt5Active";
    internal static IEnumerable<EraPartBinding> Bindings()
    {
        foreach(var id in Ids)
            for(int cell=1;cell<=2;cell++)
                yield return new EraPartBinding(id+"Stage1Cell"+cell,id+"Housing",Material,Material,"heavyEra");
    }
    internal static EraPartBindingCatalogue With(EraPartBindingCatalogue catalogue)
    {
        var missing=Bindings().Where(b=>!catalogue.TryGetCassette(b.CassetteComponentId,out _)).ToList();
        return missing.Count==0?catalogue:new EraPartBindingCatalogue(catalogue.Bindings.Concat(missing).ToList());
    }
}
