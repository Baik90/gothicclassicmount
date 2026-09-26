using ZenKit;

static class SkyInspect
{
 public static void Inspect(string install)
 {
  var vfs=new Vfs();
  foreach(var path in Directory.EnumerateFiles(Path.Combine(install,"Data"),"*.VDF")) vfs.MountDisk(path,VfsOverwriteBehavior.Older);
  void Visit(VfsNode n,string prefix)
  {
   foreach(var c in n.Children)
   {
    var path=prefix+c.Name;
    if(c.IsDir()) Visit(c,path+"/");
    else if(new[]{"SKY","BARR","DOME","THUNDER","LIGHTNING"}.Any(s=>c.Name.Contains(s,StringComparison.OrdinalIgnoreCase)))
    {
     Console.WriteLine(path);
     if(c.Name.EndsWith(".MSH",StringComparison.OrdinalIgnoreCase))
     {
      var m=new Mesh(c.Buffer);
      Console.WriteLine($"  vertices={m.Positions.Count} polys={m.Polygons.Count} min={m.Positions.Aggregate(System.Numerics.Vector3.Min)} max={m.Positions.Aggregate(System.Numerics.Vector3.Max)}");
      foreach(var mat in m.Materials) Console.WriteLine($"  material {mat.Name} texture={mat.Texture}");
     }
    }
   }
  }
  Visit(vfs.Root,"");
 }
}
