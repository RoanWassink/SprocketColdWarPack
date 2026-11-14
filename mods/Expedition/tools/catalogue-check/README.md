# catalogue-check

Static checker and deterministic sweep for authored encounter catalogues.
This tool never touches a game installation, native assemblies or player files.

```sh
dotnet run --project tools/catalogue-check/CatalogueCheck.csproj -- \
  content/example.catalogue.json
```

Optional arguments:

```sh
dotnet run --project tools/catalogue-check/CatalogueCheck.csproj -- \
  content/example.catalogue.json --seeds 64 --csv /tmp/catalogue-summary.csv
```

It checks:

- strict JSON shape (unknown/duplicate properties, safe local references)
- fixture labelling and release qualification separation
- minimum 3 qualified maps and 4 qualified enemy designs per native era
- positive curated threat and checked prices in the catalogue unit
- required-mod and content-hash validity
- three distinct offers, one per risk, within the 12-enemy cap and threat target
- round/risk/count/threat range summaries over a synthetic fixture budget sweep

`content/example.catalogue.json` is a **TEST FIXTURE NOT PLAYABLE**. Its budgets,
blueprints, maps and hashes are synthetic. It must never be shipped as verified
playable content, and release qualification still requires real checked maps and
enemy designs for every native era.
