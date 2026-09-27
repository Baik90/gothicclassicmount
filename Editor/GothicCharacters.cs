using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NMatrix = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;
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
		if ( IsHumanoidCharacter( d ) )
		{
			var physicsBodyCount = AddHumanoidRagdollPhysics( builder, bones );
			if ( physicsBodyCount == 0 )
				Log.Warning( $"Gothic character model '{d.Instance}' has no mapped ragdoll bones; check HUMANS.MDH bone names." );
			else
				Log.Info( $"Gothic character model '{d.Instance}': added {physicsBodyCount} model-level ragdoll bodies." );
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
		var model = builder.Create();
		GothicRootMotionLibrary.Register( model, _characterMeshes.ReadAnimations(d).Select( animation =>
			new GothicRootMotionClip( animation.Name, animation.Fps, animation.Looping,
				animation.RootMotion.Select( p => new Vector3( p.X, p.Y, p.Z ) ).ToArray() ) ) );
		return model;
	}

	private static bool IsHumanoidCharacter( GothicCharacterDefinition d ) =>
		string.Equals( System.IO.Path.GetFileNameWithoutExtension( d.Visual ), "HUMANS", StringComparison.OrdinalIgnoreCase );

	private sealed record RagdollBone( string Key, string[] Names, string Parent, float Radius, float Mass, bool Sphere = false );
	private sealed record RagdollBody( RagdollBone Definition, int BoneIndex, int BodyIndex );

	private static readonly RagdollBone[] HumanoidRagdollBones =
	[
		new( "pelvis", ["BIP01 PELVIS"], null, 6.5f, 5f ),
		new( "spine", ["BIP01 SPINE"], "pelvis", 5.2f, 3f ),
		new( "spine1", ["BIP01 SPINE1"], "spine", 4.8f, 3f ),
		new( "spine2", ["BIP01 SPINE2"], "spine1", 4.4f, 2.5f ),
		new( "neck", ["BIP01 NECK"], "spine2", 2.5f, 0.8f ),
		new( "head", ["BIP01 HEAD"], "neck", 4.5f, 2f, Sphere: true ),
		new( "l_upperarm", ["BIP01 L UPPERARM", "BIP01 L ARM"], "spine2", 2.8f, 1.8f ),
		new( "l_forearm", ["BIP01 L FOREARM"], "l_upperarm", 2.3f, 1.2f ),
		new( "l_hand", ["BIP01 L HAND"], "l_forearm", 1.8f, 0.5f, Sphere: true ),
		new( "r_upperarm", ["BIP01 R UPPERARM", "BIP01 R ARM"], "spine2", 2.8f, 1.8f ),
		new( "r_forearm", ["BIP01 R FOREARM"], "r_upperarm", 2.3f, 1.2f ),
		new( "r_hand", ["BIP01 R HAND"], "r_forearm", 1.8f, 0.5f, Sphere: true ),
		new( "l_thigh", ["BIP01 L THIGH"], "pelvis", 4f, 4f ),
		new( "l_calf", ["BIP01 L CALF"], "l_thigh", 3f, 2.5f ),
		new( "l_foot", ["BIP01 L FOOT"], "l_calf", 2.8f, 0.8f, Sphere: true ),
		new( "r_thigh", ["BIP01 R THIGH"], "pelvis", 4f, 4f ),
		new( "r_calf", ["BIP01 R CALF"], "r_thigh", 3f, 2.5f ),
		new( "r_foot", ["BIP01 R FOOT"], "r_calf", 2.8f, 0.8f, Sphere: true )
	];

	private static int AddHumanoidRagdollPhysics( ModelBuilder builder, GothicCharacterBone[] bones )
	{
		var boneIndices = bones.Select( ( bone, index ) => (bone.Name, index) )
			.ToDictionary( x => x.Name, x => x.index, StringComparer.OrdinalIgnoreCase );
		var present = HumanoidRagdollBones
			.Select( definition => (Definition: definition, BoneIndex: definition.Names.Select( name => boneIndices.TryGetValue( name, out var index ) ? index : -1 ).FirstOrDefault( index => index >= 0, -1 )) )
			.Where( x => x.BoneIndex >= 0 )
			.ToArray();
		if ( !present.Any( x => x.Definition.Key == "pelvis" ) || !present.Any( x => x.Definition.Key == "head" ) )
			return 0;

		var bodies = new List<RagdollBody>();
		foreach ( var item in present )
		{
			var bone = bones[item.BoneIndex];
			var body = builder.AddBody( item.Definition.Mass, null, bone.Name );
			var child = present.FirstOrDefault( candidate => candidate.Definition.Parent == item.Definition.Key );
			if ( item.Definition.Sphere || child.Definition is null )
			{
				body.AddSphere( new Sphere( Vector3.Zero, item.Definition.Radius ), Transform.Zero );
			}
			else
			{
				var childPosition = new NVector3( bones[child.BoneIndex].Global.M41, bones[child.BoneIndex].Global.M42, bones[child.BoneIndex].Global.M43 );
				if ( !NMatrix.Invert( bone.Global, out var inverseBone ) )
					throw new InvalidDataException( $"Cannot build ragdoll segment for bone {bone.Name}." );
				var localEnd = NVector3.Transform( childPosition, inverseBone );
				var end = new Vector3( localEnd.X, localEnd.Y, localEnd.Z );
				if ( end.Length < 0.1f ) end = Vector3.Up * (item.Definition.Radius * 1.5f);
				body.AddCapsule( new Capsule( Vector3.Zero, end, item.Definition.Radius ), Transform.Zero );
			}

			bodies.Add( new RagdollBody( item.Definition, item.BoneIndex, bodies.Count ) );
		}

		foreach ( var child in bodies )
		{
			var parentKey = child.Definition.Parent;
			while ( parentKey is not null )
			{
				var parent = bodies.FirstOrDefault( body => body.Definition.Key == parentKey );
				if ( parent is not null )
				{
					var childOrigin = new NVector3( bones[child.BoneIndex].Global.M41, bones[child.BoneIndex].Global.M42, bones[child.BoneIndex].Global.M43 );
					if ( !NMatrix.Invert( bones[parent.BoneIndex].Global, out var inverseParent ) )
						throw new InvalidDataException( $"Cannot build ragdoll joint for bone {bones[child.BoneIndex].Name}." );
					var localAnchor = NVector3.Transform( childOrigin, inverseParent );
					var frame1 = new Transform( new Vector3( localAnchor.X, localAnchor.Y, localAnchor.Z ), Rotation.Identity );
					builder.AddBallJoint( parent.BodyIndex, child.BodyIndex, frame1, Transform.Zero, collision: false )
						.WithSwingLimit( 45f ).WithTwistLimit( -35f, 35f );
					break;
				}
				parentKey = HumanoidRagdollBones.FirstOrDefault( definition => definition.Key == parentKey )?.Parent;
			}
		}

		return bodies.Count;
	}

	private static Transform CharacterTransform(System.Numerics.Matrix4x4 matrix)
	{
		if(!System.Numerics.Matrix4x4.Decompose(matrix,out _,out var q,out var p)) throw new InvalidDataException("Invalid bone transform.");
		return new Transform(new Vector3(p.X,p.Y,p.Z),new Rotation(q.X,q.Y,q.Z,q.W));
	}
	private string CharacterIdle(GothicCharacterDefinition d)
	{
		var animations = _characterMeshes.ReadAnimations(d);
		var isHuman = string.Equals( System.IO.Path.GetFileNameWithoutExtension( d.Visual ), "HUMANS", StringComparison.OrdinalIgnoreCase );
		return (isHuman ? animations.FirstOrDefault( a => a.Name == "S_FIST" ) : null)?.Name ?? animations.FirstOrDefault()?.Name;
	}
	private static string CharacterScale(GothicCharacterDefinition d) => FormattableString.Invariant($"{d.ScaleX},{d.ScaleZ},{d.ScaleY}");

	internal JsonObject CharacterRoot( GothicCharacterDefinition d ) => new()
	{
		["__guid"] = JsonValue.Create(CharacterId(d.Instance,"root")), ["__version"]=2,
		["Name"] = $"{d.Name} ({d.Instance})", ["Enabled"]=true,
		["Position"]="0,0,0", ["Rotation"]="0,0,0,1", ["Scale"]=CharacterScale(d), ["Tags"]="gothic_character",
		["Components"]=CharacterComponents(d), ["Children"]=CharacterChildren(d)
	};

	private JsonArray CharacterComponents( GothicCharacterDefinition d )
	{
		var isPlayerHero = string.Equals( d.Instance, "PC_HERO", StringComparison.OrdinalIgnoreCase );
		var components = new JsonArray();
		if ( !isPlayerHero )
		{
			components.Add( new JsonObject
			{
				["__type"]="Sandbox.SkinnedModelRenderer", ["__guid"]=JsonValue.Create(CharacterId(d.Instance,"renderer")),
				["__enabled"]=true, ["Model"]=GetMountedResourceUri(CharacterModelPath(d)),
				["UseAnimGraph"]=false, ["CreateBoneObjects"]=false,
				["Sequence"]=new JsonObject { ["Name"]=CharacterIdle(d), ["Looping"]=true }
			} );
			components.Add( CharacterAnimatorComponent(d, "root") );
			if ( IsHumanoidCharacter( d ) )
				components.Add( CharacterModelPhysicsComponent( d, "root" ) );
		}

		if ( isPlayerHero )
		{
			components.Add( new JsonObject
			{
				["__type"]="Sandbox.PlayerController", ["__guid"]=JsonValue.Create(CharacterId(d.Instance,"player_controller")), ["__enabled"]=true,
				["UseInputControls"]=false, ["UseLookControls"]=true, ["UseCameraControls"]=true,
				["UseAnimatorControls"]=false, ["ThirdPerson"]=true, ["CameraOffset"]="220,0,35",
				["WalkSpeed"]=110, ["RunSpeed"]=260, ["JumpSpeed"]=260, ["BodyRadius"]=18, ["BodyHeight"]=72,
				["UseButton"]="Use", ["EnablePressing"]=true,
				["Renderer"]=CharacterComponentReference(d, "renderer", "model", "SkinnedModelRenderer")
			} );
			components.Add( new JsonObject
			{
				["__type"]=typeof(GothicCharacterAI).FullName, ["__guid"]=JsonValue.Create(CharacterId(d.Instance,"gothic_ai")), ["__enabled"]=true,
				["Controller"]=CharacterComponentReference(d, "player_controller", "root", "PlayerController"),
				["Renderer"]=CharacterComponentReference(d, "renderer", "model", "SkinnedModelRenderer"),
				["Animator"]=CharacterComponentReference(d, "animator", "model", nameof(GothicAnimator)),
				["BackwardSpeedScale"]=0.65f, ["StrafeSpeedScale"]=0.75f, ["TurnSpeed"]=540f
			} );
			components.Add( new JsonObject
			{
				["__type"]=typeof(GothicHeroController).FullName, ["__guid"]=JsonValue.Create(CharacterId(d.Instance,"gothic_controller")), ["__enabled"]=true,
				["CharacterAI"]=CharacterComponentReference(d, "gothic_ai", "root", nameof(GothicCharacterAI)),
				["Controller"]=CharacterComponentReference(d, "player_controller", "root", "PlayerController"),
				["HeroRenderer"]=CharacterComponentReference(d, "renderer", "model", "SkinnedModelRenderer"),
				["Animator"]=CharacterComponentReference(d, "animator", "model", nameof(GothicAnimator))
			} );
		}

		return components;
	}

	private JsonArray CharacterChildren( GothicCharacterDefinition d )
	{
		if ( !string.Equals( d.Instance, "PC_HERO", StringComparison.OrdinalIgnoreCase ) )
			return new JsonArray();

		var components = new JsonArray( new JsonObject
		{
			["__type"]="Sandbox.SkinnedModelRenderer", ["__guid"]=JsonValue.Create(CharacterId(d.Instance,"renderer")),
			["__enabled"]=true, ["Model"]=GetMountedResourceUri(CharacterModelPath(d)),
			["UseAnimGraph"]=false, ["CreateBoneObjects"]=false,
			["Sequence"]=new JsonObject { ["Name"]=CharacterIdle(d), ["Looping"]=true }
		} );
		components.Add( CharacterAnimatorComponent(d, "model") );
		components.Add( CharacterModelPhysicsComponent( d, "model" ) );

		return new JsonArray( new JsonObject
		{
			["__guid"]=JsonValue.Create(CharacterId(d.Instance,"model")), ["__version"]=2,
			["Name"]="PC_HERO Model", ["Enabled"]=true, ["Position"]="0,0,0", ["Rotation"]="0,0,0,1", ["Scale"]="1,1,1",
			["Components"]=components, ["Children"]=new JsonArray()
		} );
	}

	private JsonObject CharacterAnimatorComponent( GothicCharacterDefinition d, string rendererOwner ) => new()
	{
		["__type"]=typeof(GothicAnimator).FullName, ["__guid"]=JsonValue.Create(CharacterId(d.Instance,"animator")), ["__enabled"]=true,
		["Renderer"]=CharacterComponentReference(d, "renderer", rendererOwner, "SkinnedModelRenderer"),
		["Profile"]=string.Equals(System.IO.Path.GetFileNameWithoutExtension(d.Visual), "HUMANS", StringComparison.OrdinalIgnoreCase) ? 0 : 1,
		["CreatureMovement"]=(int)InferCreatureMovement(d), ["PlaybackRate"]=1f, ["MovementThreshold"]=0.1f
	};

	// Physics shapes and joints live in the model resource. This component only
	// instantiates them and keeps their bodies following the animated bones.
	private JsonObject CharacterModelPhysicsComponent( GothicCharacterDefinition d, string rendererOwner ) => new()
	{
		["__type"]="Sandbox.ModelPhysics", ["__guid"]=JsonValue.Create(CharacterId(d.Instance,"model_physics")), ["__enabled"]=true,
		["Renderer"]=CharacterComponentReference(d, "renderer", rendererOwner, "SkinnedModelRenderer"),
		["MotionEnabled"]=false, ["IgnoreRoot"]=true
	};

	private static GothicCreatureMovement InferCreatureMovement( GothicCharacterDefinition d )
	{
		var visual = System.IO.Path.GetFileNameWithoutExtension( d.Visual ?? string.Empty );
		if ( new[] { "BLOODFLY", "HARPY", "DRAGON", "WISP" }.Any( name => visual.Contains( name, StringComparison.OrdinalIgnoreCase ) ) )
			return GothicCreatureMovement.Flying;
		if ( visual.Contains( "SWAMP", StringComparison.OrdinalIgnoreCase ) || visual.Contains( "WATER", StringComparison.OrdinalIgnoreCase ) )
			return GothicCreatureMovement.Swimming;
		return GothicCreatureMovement.Ground;
	}

	private static JsonObject CharacterComponentReference( GothicCharacterDefinition d, string component, string gameObject, string componentType ) => new()
	{
		["_type"]="component", ["component_id"]=CharacterId(d.Instance,component).ToString(),
		["go"]=CharacterId(d.Instance,gameObject).ToString(), ["component_type"]=componentType
	};
	private static Guid CharacterId( string instance, string part ) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"gothicclassic:character:{instance}:{part}")).AsSpan(0,16));

	public void ExportCharacterPrefabs()
	{
		var root = System.IO.Path.Combine(Project.Current.GetAssetsPath(),"characters","gothic_classic");
		System.IO.Directory.CreateDirectory(root);
		var options = new JsonSerializerOptions { WriteIndented=true };
		File.WriteAllText(System.IO.Path.Combine(root,"catalog.json"),JsonSerializer.Serialize(Characters,options),Encoding.UTF8);
		var created=0; var existing=0; var upgraded=0; var animatorAdded=0;
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
				var changed=false;
				var childModel=(rootObject?["Children"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(c=>c["__guid"]?.ToString()==CharacterId(d.Instance,"model").ToString());
				var rendererOwner=childModel is null ? "root" : "model";
				var rendererComponents=(rendererOwner=="root" ? rootObject?["Components"] : childModel?["Components"]) as JsonArray;
				if(rendererComponents is null && d.Instance.Equals("PC_HERO",StringComparison.OrdinalIgnoreCase))
				{
					childModel=new JsonObject
					{
						["__guid"]=CharacterId(d.Instance,"model").ToString(), ["__version"]=2,
						["Name"]="PC_HERO Model", ["Enabled"]=true, ["Position"]="0,0,0", ["Rotation"]="0,0,0,1", ["Scale"]="1,1,1",
						["Components"]=new JsonArray(new JsonObject
						{
							["__type"]="Sandbox.SkinnedModelRenderer", ["__guid"]=CharacterId(d.Instance,"renderer").ToString(),
							["__enabled"]=true, ["Model"]=GetMountedResourceUri(CharacterModelPath(d)),
							["UseAnimGraph"]=false, ["CreateBoneObjects"]=false,
							["Sequence"]=new JsonObject { ["Name"]=CharacterIdle(d), ["Looping"]=true }
						}), ["Children"]=new JsonArray()
					};
					(rootObject["Children"] as JsonArray)?.Add(childModel);
					rendererOwner="model";
					rendererComponents=childModel["Components"] as JsonArray;
					changed=true;
				}
				var animatorExists=(rendererComponents?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>()).Any(c=>
					c["__guid"]?.ToString()==CharacterId(d.Instance,"animator").ToString());
				if(!animatorExists && rendererComponents is not null)
				{
					rendererComponents.Add(CharacterAnimatorComponent(d,rendererOwner));
					animatorAdded++;
					changed=true;
				}
				if(renderer is not null)
				{
					renderer["__type"]="Sandbox.SkinnedModelRenderer";
					renderer["UseAnimGraph"]=false; renderer["CreateBoneObjects"]=false;
					renderer["Sequence"]=new JsonObject { ["Name"]=CharacterIdle(d), ["Looping"]=true };
					if(rootObject["Scale"]?.ToString()=="1,1,1") rootObject["Scale"]=CharacterScale(d);
					upgraded++;
					changed=true;
				}
				else existing++;
				if(changed)
				{
					File.WriteAllText(path,saved.ToJsonString(options),Encoding.UTF8);
					CompileCharacterPrefab(path);
				}
				continue;
			}
			var json=new JsonObject { ["RootObject"]=CharacterRoot(d), ["ResourceVersion"]=2, ["__version"]=2, ["ShowInMenu"]=true, ["MenuPath"]="Gothic/Characters" };
			File.WriteAllText(path,json.ToJsonString(options),Encoding.UTF8);
			CompileCharacterPrefab(path); created++;
		}
		Log.Info($"Gothic character prefabs: created={created}, upgraded={upgraded}, animators added={animatorAdded}, existing={existing}, unavailable={Characters.Count(d=>d.Error is not null)}. Assets/characters/gothic_classic");
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
