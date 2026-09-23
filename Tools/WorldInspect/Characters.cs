using ZenKit;
using GothicClassicMount;
using System.Numerics;

static class Characters
{
 public static void Inspect(string install)
 {
  Directory.CreateDirectory(".cache");
  var vfs = new Vfs();
  foreach(var path in Directory.EnumerateFiles(Path.Combine(install,"Data"), "*.VDF")) vfs.MountDisk(path,VfsOverwriteBehavior.Older);
  var definitions = GothicCharacterCatalog.Read(typeof(Vfs).Assembly, vfs, install);
  File.WriteAllText(".cache/characters.json",System.Text.Json.JsonSerializer.Serialize(definitions,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
  Console.WriteLine($"NPCs={definitions.Count} errors={definitions.Count(x=>x.Error!=null)} armored={definitions.Count(x=>x.ArmorVisual!=null)}");
  var reader = new GothicCharacterMeshReader(typeof(Vfs).Assembly,vfs);
  int ok=0;
  var visuals=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var d in definitions) {
   try {
    var parts=reader.Read(d);
    var bones=reader.ReadSkeleton(d);
    foreach(var part in parts)
     for(int i=0;i<part.Positions.Length;i++) {
      var weights=part.Weights[i];
      if(weights.Any(w=>w.Bone<0||w.Bone>=bones.Length)||Math.Abs(weights.Sum(w=>w.Weight)-1)>0.0001f) throw new Exception("Invalid weights");
     }
    var clips=reader.ReadAnimations(d);
    if(clips.Count==0) throw new Exception("No animations for "+d.Visual);
    if(visuals.Add(d.Visual)) Console.WriteLine($"RIG {d.Visual}: bones={bones.Length} clips={string.Join(',',clips.Select(c=>$"{c.Name}:{c.Frames.Length}"))}");
    var points=parts.SelectMany(x=>x.Positions).ToArray();
    if(points.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z))) throw new Exception("Non-finite geometry");
    var min=points.Aggregate(Vector3.Min); var max=points.Aggregate(Vector3.Max);
    if((max-min).Length() > 1500) throw new Exception("Implausible bounds "+min+" / "+max);
    ok++;
    if(d.Name=="Diego"||d.Name=="Xardas"||d.Instance=="PC_HERO"||d.Instance=="SCAVENGER") Console.WriteLine($"{d.Instance} {d.Name} bounds={min}/{max} textures={string.Join(',',parts.Select(p=>Path.GetFileName(p.Texture)))}");
   } catch(FileNotFoundException e) {Console.WriteLine($"UNAVAILABLE {d.Instance}: {e.Message}");}
   catch(Exception e) {Console.WriteLine($"FAIL {d.Instance}: {e}"); Environment.ExitCode=1;}
  }
  Console.WriteLine($"Geometry: {ok}/{definitions.Count}");
 }
}
