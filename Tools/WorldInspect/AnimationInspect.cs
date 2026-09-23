using ZenKit;
using System.Numerics;

static class AnimationInspect
{
 public static void Inspect(string install)
 {
  Directory.CreateDirectory(".cache");
  var vfs=new Vfs();
  foreach(var path in Directory.EnumerateFiles(Path.Combine(install,"Data"),"*.VDF")) vfs.MountDisk(path,VfsOverwriteBehavior.Older);
  var files=new Dictionary<string,VfsNode>(StringComparer.OrdinalIgnoreCase);
  void Visit(VfsNode n) { foreach(var c in n.Children) {if(c.IsDir()) Visit(c); else files[c.Name]=c;} }
  Visit(vfs.Root);
  foreach(var name in new[]{"HUMANS","SCAVENGER"}) {
   var h=new ModelHierarchy(files[name+".MDH"].Buffer).Cache();
   Console.WriteLine($"{name}: bones={h.Nodes.Count} checksum={h.Checksum} root={h.RootTranslation}");
   foreach(var n in h.Nodes.Take(3)) Console.WriteLine($"  {n.Name} parent={n.ParentIndex} transform={n.Transform}");
   foreach(var file in files.Keys.Where(f=>f.StartsWith(name+"-")&&f.EndsWith(".MAN")).Take(8)) {
    var a=new ModelAnimation(files[file].Buffer).Cache();
    Console.WriteLine($"  {file}: {a.Name} next={a.Next} frames={a.FrameCount} nodes={a.NodeCount} fps={a.Fps} checksum={a.Checksum} samples={a.Samples.Count} indices={string.Join(',',a.NodeIndices.Take(8))}");
    foreach(var s in a.Samples.Take(3)) Console.WriteLine($"    {s.Position} / {s.Rotation}");
   }
  }
  File.WriteAllLines(".cache/animation-files.txt",files.Keys.Where(f=>f.EndsWith(".MAN")||f.EndsWith(".MSB")));
 }
}
