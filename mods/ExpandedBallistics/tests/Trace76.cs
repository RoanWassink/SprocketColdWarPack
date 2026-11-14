using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.Json;
namespace SprocketExpandedBallistics;
internal static class Trace76
{
 internal static void Run()
 {
  var output=Fix02228Tests.OutputRoot();string json=File.ReadAllText(Path.Combine(output,"user-original.json"));var mesh=LayerMesh.Read(json);var node=JsonNode.Parse(json)!;var v=node["vertices"]!.AsArray().Select(x=>x!.GetValue<float>()).ToArray();Vector3 P(int i)=>new(v[i*3],v[i*3+1],v[i*3+2]);var fs=node["faces"]!.AsArray().Select(f=>f!["v"]!.AsArray().Select(x=>x!.GetValue<int>()).ToArray()).ToArray();var mask=JsonSerializer.Deserialize<bool[]>(File.ReadAllText(Path.Combine(output,"user-selection-forward.json")))!;var pieces=LayerFaces.ClosedPieces(json);var ns=mesh.Polygons.Select(p=>Vector3.Normalize(CheekShellFill.Newell(p))).ToArray();
  foreach(int vertex in new[]{41,42,67,68,171,17,23,182})
  {
   var at=Enumerable.Range(0,mask.Length).Where(i=>mask[i]&&fs[i].Contains(vertex)).ToArray();var n=at.Aggregate(Vector3.Zero,(s,i)=>s+CheekShellFill.Newell(mesh.Polygons[i]));var u=-Vector3.Normalize(n);Console.WriteLine($"vertex {vertex}, point {P(vertex)}, fronts {string.Join(",",at.Select(i=>i+1))}, pieces {string.Join(",",at.Select(i=>pieces[i]))}, inward {u}, dots {string.Join(",",at.Select(i=>Vector3.Dot(u,ns[i])))}");
   var hits=Enumerable.Range(0,fs.Length).SelectMany(i=>Enumerable.Range(1,fs[i].Length-2).Select(k=>(face:i,hit:ComposerFill.Ray(P(vertex),u,new[]{new FillTriangle(P(fs[i][0]),P(fs[i][k]),P(fs[i][k+1]))},50)))).Where(t=>t.hit!=null).OrderBy(t=>t.hit).ToArray();Console.WriteLine("hits "+string.Join("; ",hits.Take(6).Select(h=>$"face {h.face+1} piece {pieces[h.face]} selected {mask[h.face]} dist {h.hit}")));
  }
 }
}
