using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Sandbox.Mounting;

namespace GothicClassicMount;

public sealed partial class GothicClassicMount
{
	private List<GothicCharacterDefinition> _characters;
	private GothicCharacterMeshReader _characterMeshes;
	public IReadOnlyList<GothicCharacterDefinition> Characters => _characters ??= ReadCharacters();
	private void ResetCharacters() { _characters = null; _characterMeshes = null; }

	private List<GothicCharacterDefinition> ReadCharacters()
	{
		var vfs = RequireVfs();
		var assembly = vfs.GetType().Assembly;
		var definitions = GothicCharacterCatalog.Read( assembly, vfs, InstallDirectory );
		_characterMeshes = new GothicCharacterMeshReader( assembly, vfs );
		foreach ( var d in definitions.Where( d => d.Error is null ) )
		{
			try { _characterMeshes.Read( d ); _characterMeshes.ReadSkeleton(d); }
			catch ( Exception e ) { d.Error = (e.InnerException ?? e).Message; }
		}
		return definitions;
	}

	private void RegisterCharacters( MountContext context )
	{
		try
		{
			foreach ( var d in Characters.Where( d => d.Error is null ) )
			{
				context.Add( ResourceType.Model, CharacterModelPath(d), new GothicCharacterModelResource(d.Instance) );
				context.Add( ResourceType.PrefabFile, CharacterPrefabPath(d), new GothicCharacterPrefabResource(d.Instance) );
			}
			Log.Info( $"Gothic characters: {Characters.Count} definitions, {Characters.Count(d=>d.Error is null)} prefabs; {Characters.Count(d=>d.Error is not null)} unavailable. See the exported character catalog for details." );
		}
		catch ( Exception e ) { Log.Warning( $"Gothic character catalog unavailable: {e}" ); }
	}

	internal static string CharacterFileName( GothicCharacterDefinition d ) =>
		Regex.Replace( $"{d.Instance}_{d.Name}", @"[^\p{L}\p{N}_-]+", "_" ).Trim('_').ToLowerInvariant();
	internal static string CharacterModelPath( GothicCharacterDefinition d ) => $"_gothicruntime/characters/gothic_classic/{CharacterFileName(d)}.vmdl";
	internal static string CharacterPrefabPath( GothicCharacterDefinition d ) => $"_gothicruntime/prefabs/characters/gothic_classic/{CharacterFileName(d)}.prefab";
	internal GothicCharacterDefinition Character( string instance ) => Characters.Single( d => d.Instance == instance );

	internal Model BuildCharacter( GothicCharacterDefinition d, string path )
	{
		if ( d.Error is not null ) throw new InvalidDataException( d.Error );
		var builder = Model.Builder.WithName( path );
		var bones = _characterMeshes.ReadSkeleton(d);
		foreach(var bone in bones)
		{
			// The native runtime builder consumes model-space bind transforms (verified by round-trip),
			// despite the managed AddBone parameter documentation describing parent space.
			var t = CharacterTransform(bone.Global);
			builder.AddBone(bone.Name,t.Position,t.Rotation,bone.Parent<0 ? null : bones[bone.Parent].Name);
		}
		foreach(var animation in _characterMeshes.ReadAnimations(d))
		{
			var clip=builder.AddAnimation(animation.Name,animation.Fps).WithLooping(animation.Looping);
			foreach(var frame in animation.Frames) clip.AddFrame(frame.Select(CharacterTransform).ToList());
		}
		foreach ( var part in _characterMeshes.Read(d) )
		{
			if ( part.Positions.Length == 0 ) continue;
			var vertices = part.Positions.Select( (p,i) => new GothicVertex
			{
				position = new Vector3(p.X,p.Y,p.Z), texcoord = new Vector2(part.Uvs[i].X,part.Uvs[i].Y)
			} ).ToList();
			var indices = Enumerable.Range(0,vertices.Count).ToList();
			GothicGeometryBuilder.SmoothNormals(vertices,indices);
			var skinned = vertices.Select((v,i)=>
			{
				var weights=part.Weights[i];
				var ids=new byte[4]; var packed=new byte[4];
				int remaining=255;
				for(int w=0;w<weights.Length;w++)
				{
					ids[w]=checked((byte)weights[w].Bone);
					packed[w]=(byte)(w==weights.Length-1 ? remaining : Math.Clamp((int)MathF.Round(weights[w].Weight*255),0,remaining));
					remaining-=packed[w];
				}
				return new GothicSkinnedVertex {Position=v.position,Normal=v.normal,TexCoord=v.texcoord,
					BoneIndices=new Color32(ids[0],ids[1],ids[2],ids[3]),BoneWeights=new Color32(packed[0],packed[1],packed[2],packed[3])};
			}).ToList();
			var material = LoadMaterial(new GothicMaterialDescriptor
			{
				MaterialName = System.IO.Path.GetFileNameWithoutExtension(part.Texture), TexturePath=part.Texture,
				CacheKey="character_"+part.Texture, DebugSource=d.Instance
			});
			var mesh = new Sandbox.Mesh(material);
			// Explicit UInt8 indices: the attribute-inferred Color32 layout normalizes indices.
#pragma warning disable CS0618
			mesh.CreateVertexBuffer(skinned.Count,GothicSkinnedVertex.Layout,skinned);
#pragma warning restore CS0618
			mesh.CreateIndexBuffer(indices.Count,indices);
			mesh.SetIndexRange(0,indices.Count);
			mesh.Bounds = BBox.FromPoints(vertices.Select(v=>v.position),0);
			builder.AddMesh(mesh);
		}
		return builder.Create();
	}
	private static Transform CharacterTransform(System.Numerics.Matrix4x4 matrix)
	{
		if(!System.Numerics.Matrix4x4.Decompose(matrix,out _,out var q,out var p)) throw new InvalidDataException("Invalid bone transform.");
		return new Transform(new Vector3(p.X,p.Y,p.Z),new Rotation(q.X,q.Y,q.Z,q.W));
	}
	private string CharacterIdle(GothicCharacterDefinition d) => _characterMeshes.ReadAnimations(d).FirstOrDefault()?.Name;
	private static string CharacterScale(GothicCharacterDefinition d) => FormattableString.Invariant($"{d.ScaleX},{d.ScaleZ},{d.ScaleY}");

	internal JsonObject CharacterRoot( GothicCharacterDefinition d ) => new()
	{
		["__guid"] = JsonValue.Create(CharacterId(d.Instance,"root")), ["__version"]=2,
		["Name"] = $"{d.Name} ({d.Instance})", ["Enabled"]=true,
		["Position"]="0,0,0", ["Rotation"]="0,0,0,1", ["Scale"]=CharacterScale(d), ["Tags"]="gothic_character",
		["Components"]=new JsonArray(new JsonObject
		{
			["__type"]="Sandbox.SkinnedModelRenderer", ["__guid"]=JsonValue.Create(CharacterId(d.Instance,"renderer")),
			["__enabled"]=true, ["Model"]=GetMountedResourceUri(CharacterModelPath(d)),
			["UseAnimGraph"]=false, ["CreateBoneObjects"]=false,
			["Sequence"]=new JsonObject { ["Name"]=CharacterIdle(d), ["Looping"]=true }
		}), ["Children"]=new JsonArray()
	};
	private static Guid CharacterId( string instance, string part ) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"gothicclassic:character:{instance}:{part}")).AsSpan(0,16));

	public void ExportCharacterPrefabs()
	{
		var root = System.IO.Path.Combine(Project.Current.GetAssetsPath(),"characters","gothic_classic");
		System.IO.Directory.CreateDirectory(root);
		var options = new JsonSerializerOptions { WriteIndented=true };
		File.WriteAllText(System.IO.Path.Combine(root,"catalog.json"),JsonSerializer.Serialize(Characters,options),Encoding.UTF8);
		var created=0; var existing=0; var upgraded=0;
		foreach(var d in Characters.Where(d=>d.Error is null))
		{
			var path=System.IO.Path.Combine(root,CharacterFileName(d)+".prefab");
			// Only migrate the generated renderer. Preserve edited models, other components and transforms.
			if(File.Exists(path))
			{
				var saved=JsonNode.Parse(File.ReadAllText(path));
				var rootObject=saved?["RootObject"];
				var renderer=(rootObject?["Components"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(c=>
					c["__guid"]?.ToString()==CharacterId(d.Instance,"renderer").ToString() &&
					c["__type"]?.ToString()=="Sandbox.ModelRenderer" && c["Model"]?.ToString()==GetMountedResourceUri(CharacterModelPath(d)));
				if(renderer is not null)
				{
					renderer["__type"]="Sandbox.SkinnedModelRenderer";
					renderer["UseAnimGraph"]=false; renderer["CreateBoneObjects"]=false;
					renderer["Sequence"]=new JsonObject { ["Name"]=CharacterIdle(d), ["Looping"]=true };
					if(rootObject["Scale"]?.ToString()=="1,1,1") rootObject["Scale"]=CharacterScale(d);
					File.WriteAllText(path,saved.ToJsonString(options),Encoding.UTF8);
					CompileCharacterPrefab(path);
					upgraded++;
				}
				else existing++;
				continue;
			}
			var json=new JsonObject { ["RootObject"]=CharacterRoot(d), ["ResourceVersion"]=2, ["__version"]=2, ["ShowInMenu"]=true, ["MenuPath"]="Gothic/Characters" };
			File.WriteAllText(path,json.ToJsonString(options),Encoding.UTF8);
			CompileCharacterPrefab(path); created++;
		}
		Log.Info($"Gothic character prefabs: created={created}, upgraded={upgraded}, existing={existing}, unavailable={Characters.Count(d=>d.Error is not null)}. Assets/characters/gothic_classic");
	}
	private static void CompileCharacterPrefab(string path)
	{
		var asset=AssetSystem.RegisterFile(path);
		if(asset is null || !asset.Compile(true) || asset.IsCompileFailed) throw new InvalidDataException($"Could not compile character prefab {path}.");
	}
}

internal sealed class GothicCharacterModelResource( string instance ) : ResourceLoader<GothicClassicMount>
{
	protected override object Load() => Host.BuildCharacter(Host.Character(instance),Path);
}

internal sealed class GothicCharacterPrefabResource( string instance ) : ResourceLoader<GothicClassicMount>
{
	private PrefabFile _prefab;
	protected override object Load()
	{
		var builder=new PrefabBuilder().WithName(Path);
		using(builder.Scope()) new GameObject().Deserialize(Host.CharacterRoot(Host.Character(instance)));
		_prefab=builder.Create();
		_prefab.RootObject=Host.CharacterRoot(Host.Character(instance));
		return _prefab;
	}
	protected override void Shutdown()
	{
		if(_prefab is not null) PrefabBuilder.Destroy(_prefab);
		_prefab=null;
	}
}

public static class GothicCharacterMenu
{
	[ConCmd("gothic_validate_characters")]
	public static void Validate()
	{
		var mount=Sandbox.Mounting.Directory.Get("gothicclassic") as GothicClassicMount;
		if(mount is null) return;
		int passed=0, failed=0;
		var checkedSkeletons=new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach(var d in mount.Characters.Where(d=>d.Error is null))
		{
			try
			{
				var uri=mount.GetMountedResourceUri(GothicClassicMount.CharacterPrefabPath(d));
				if(mount.GetByPath(uri) is not GothicCharacterPrefabResource) throw new InvalidDataException("Character prefab loader not registered; refresh the mount.");
				var prefab=PrefabFile.Load(uri);
				if(prefab?.RootObject?["__guid"]?.ToString()!=mount.CharacterRoot(d)["__guid"].ToString()) throw new InvalidDataException("Unstable prefab source ID.");
				var model=Model.Load(mount.GetMountedResourceUri(GothicClassicMount.CharacterModelPath(d)));
				if(model is null || model.IsError) throw new InvalidDataException("Character model did not load.");
				if(model.BoneCount==0 || model.AnimationCount==0) throw new InvalidDataException("Skeleton or animations missing.");
				mount.ValidateCharacterRig(d,model,checkedSkeletons.Add(d.Visual));
				var localPath=$"characters/gothic_classic/{GothicClassicMount.CharacterFileName(d)}.prefab";
				if(File.Exists(System.IO.Path.Combine(Project.Current.GetAssetsPath(),localPath)))
				{
					var local=PrefabFile.Load(localPath);
					if(!(local?.RootObject?["Components"] as JsonArray)?.OfType<JsonObject>().Any(c=>c["__type"]?.ToString()=="Sandbox.SkinnedModelRenderer") ?? true)
						throw new InvalidDataException("Local character prefab has no skinned renderer.");
				}
				passed++;
			}
			catch(Exception e) { failed++; Log.Warning($"Gothic character validation {d.Instance}: {e.Message}"); }
		}
		Log.Info($"Gothic character validation: passed={passed}, failed={failed}, skeletons={checkedSkeletons.Count}, unavailable={mount.Characters.Count(d=>d.Error is not null)}");
	}
	[ConCmd("gothic_refresh_mount")]
	public static void RefreshMount() => Sandbox.Mounting.Directory.Get("gothicclassic")?.RefreshInternal();

	[ConCmd("gothic_import_character_prefabs")]
	[Menu("Editor","Gothic Classic Mount/Import Character Prefabs")]
	public static void Import()
	{
		var mount=Sandbox.Mounting.Directory.Get("gothicclassic") as GothicClassicMount;
		if(mount is null || Project.Current is null) return;
		mount.ExportCharacterPrefabs();
	}
}
