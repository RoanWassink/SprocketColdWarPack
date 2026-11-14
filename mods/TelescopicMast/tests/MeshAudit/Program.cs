using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SprocketTelescopicMast;
using UnityEngine;
CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
var baseline=args.Contains("--baseline");
var dir=baseline?"logs/mesh-baseline-0.9.6":"logs/mast-obj";Directory.CreateDirectory(dir);
var meshes=new Dictionary<string,Mesh> {
 ["sleeve"]=CylinderMesh.Create(true),["piston"]=CylinderMesh.Create(false),
 ["telescope_base"]=CylinderMesh.Create(true,true),["telescope_part"]=CylinderMesh.Create(false,true),
 ["hinge_base"]=HingeMesh.CreateBase(),["hinge_leaf"]=HingeMesh.CreateLeaf(),["hinge_mount"]=HingeMesh.CreateMount(),
 ["rotator_base"]=RotatorMesh.Create(false,1,1),["rotator_plate"]=RotatorMesh.Create(true,1,1)};
var report=new Dictionary<string,object>();
foreach(var (name,m) in meshes) {
 byte[] V(Vector3[] vs)=>vs.SelectMany(p=>new[]{p.x,p.y,p.z}).SelectMany(BitConverter.GetBytes).ToArray();
 string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));
 var hashes=new[]{Hash(V(m.vertices)),Hash(m.triangles.SelectMany(BitConverter.GetBytes).ToArray()),Hash(V(m.normals))};
 var signature=string.Join("\n",hashes)+"\n";
 if(!baseline&&File.ReadAllText("logs/mesh-baseline-0.9.6/"+name+".sha256")!=signature)throw new Exception(name+" geometry changed");
 File.WriteAllText(dir+"/"+name+".sha256",signature);
 var lines=new List<string>{"# Exact float32 production mesh; Y-up; UV0; authored normals"};
 foreach(var p in m.vertices)lines.Add($"v {p.x:R} {p.y:R} {p.z:R}");
 foreach(var p in m.uv)lines.Add($"vt {p.x:R} {p.y:R}");
 foreach(var p in m.normals)lines.Add($"vn {p.x:R} {p.y:R} {p.z:R}");
 for(int i=0;i<m.triangles.Length;i+=3) lines.Add("f "+string.Join(" ",m.triangles.Skip(i).Take(3).Select(j=>m.uv.Length>0?$"{j+1}/{j+1}/{j+1}":$"{j+1}//{j+1}")));
 File.WriteAllLines(dir+"/"+name+".obj",lines);
 var ratios=new List<double>();
 if(!baseline&&m.uv.Length>0) {
  if(m.uv.Any(p=>!float.IsFinite(p.x)||!float.IsFinite(p.y)))throw new Exception(name+" nonfinite UV");
  for(int i=0;i<m.triangles.Length;i+=3) {
   var a=m.vertices[m.triangles[i]];var b=m.vertices[m.triangles[i+1]];var c=m.vertices[m.triangles[i+2]];
   double x=b.x-a.x,y=b.y-a.y,z=b.z-a.z,u=c.x-a.x,v=c.y-a.y,w=c.z-a.z;
   double area=Math.Sqrt(Math.Pow(y*w-z*v,2)+Math.Pow(z*u-x*w,2)+Math.Pow(x*v-y*u,2));
   var au=m.uv[m.triangles[i]];var bu=m.uv[m.triangles[i+1]];var cu=m.uv[m.triangles[i+2]];
   double uvArea=Math.Abs((double)(bu.x-au.x)*(cu.y-au.y)-(double)(bu.y-au.y)*(cu.x-au.x));
   double ratio=uvArea/area;ratios.Add(ratio);
   if(uvArea<1e-14||Math.Abs(ratio-1)>.15)throw new Exception($"{name} triangle {i/3} UV/3D {ratio}");
  }
 }
 report[name]=new{vertices=m.vertices.Length,indices=m.triangles.Length,positionSHA256=hashes[0],indexSHA256=hashes[1],normalSHA256=hashes[2],minRatio=ratios.Count>0?ratios.Min():0,maxRatio=ratios.Count>0?ratios.Max():0};
 Console.WriteLine($"PASS {name}: {m.vertices.Length} vertices, {m.triangles.Length/3} triangles, unchanged positions/indices/normals; UV/3D {(ratios.Count>0?$"{ratios.Min():F6}..{ratios.Max():F6}":"baseline/hidden")}");
}
File.WriteAllText(baseline?"logs/mesh-baseline-0.9.6.json":"logs/mesh-audit-0.9.7.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
namespace UnityEngine {public struct Vector2 { public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;} }}
