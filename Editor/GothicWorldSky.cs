using System;
using System.IO;
using Sandbox.Mounting;

namespace GothicClassicMount;

internal static class GothicWorldSky
{
	internal const string MaterialPath = "_gothicruntime/materials/gothic_classic/colony_sky.vmat";
	internal const string BarrierMaterialPath = "_gothicruntime/materials/gothic_classic/colony_barrier.vmat";
	internal const string BarrierModelPath = "_gothicruntime/models/gothic_classic/colony_barrier.vmdl";
	private const string SkyTag = "gothic_world_sky";
	private const string BarrierTag = "gothic_barrier_dome";

	internal static bool IsColony(string sourcePath) =>
		string.Equals(System.IO.Path.GetFileName(sourcePath), "WORLD.ZEN", StringComparison.OrdinalIgnoreCase);

	internal static void Populate(GothicClassicMount host, string sourcePath, GameObject worldRoot)
	{
		if(!IsColony(sourcePath)) return;
		using var sceneScope = worldRoot.Scene.Push();
		var skyObject = worldRoot.Children.FirstOrDefault(c=>c.Tags.Has(SkyTag));
		if(skyObject is null)
		{
			skyObject = new GameObject("Gothic Sky and Barrier") { Parent = worldRoot };
			skyObject.Tags.Add(SkyTag);
			var sky = skyObject.Components.Create<SkyBox2D>();
			// The Gothic sky is layered 2D textures, not a cubemap suitable for ambient IBL.
			sky.SkyIndirectLighting = false;
			sky.SkyMaterial = Material.Load(host.GetMountedResourceUri(MaterialPath));
			var fog = skyObject.Components.Create<GradientFog>();
			fog.StartDistance = 10000;
			fog.Color = new Color(0.4f,0.44f,0.5f);
			fog.EndDistance = 50000;
			fog.Height = 9999999;
		}
		else
		{
			skyObject.Components.Get<SkyBox2D>().SkyMaterial = Material.Load(host.GetMountedResourceUri(MaterialPath));
		}
		var barrierObject = worldRoot.Children.FirstOrDefault( c => c.Tags.Has( BarrierTag ) )
			?? skyObject.Children.FirstOrDefault( c => c.Tags.Has( BarrierTag ) );
		if ( barrierObject is null )
		{
			barrierObject = new GameObject( "Gothic Barrier Hemisphere" ) { Parent = worldRoot };
			barrierObject.Tags.Add( BarrierTag );
		}
		else if ( barrierObject.Parent != worldRoot )
		{
			barrierObject.Parent = worldRoot;
		}
		barrierObject.LocalPosition = new Vector3( 0, 0, -2200 );
		var barrierRenderer = barrierObject.Components.Get<ModelRenderer>() ?? barrierObject.Components.Create<ModelRenderer>();
		barrierRenderer.Model = Model.Load( host.GetMountedResourceUri( BarrierModelPath ) );
		barrierRenderer.RenderOptions.Game = true;
		barrierRenderer.RenderOptions.Overlay = false;
		// A real skybox switches off the editor's fallback illumination. Supply daylight
		// for imported scenes, but retain lighting already authored by the user.
		if(!worldRoot.Scene.GetAllComponents<DirectionalLight>().Any())
		{
			var sunObject = new GameObject("Gothic Daylight") { Parent = skyObject };
			sunObject.LocalRotation = Rotation.From(new Angles(50,-35,0));
			var sun = sunObject.Components.Create<DirectionalLight>();
			sun.LightColor = new Color(0.7f,0.65f,0.56f);
			sun.SkyColor = Color.Black;
			sun.Shadows = true;
		}
		if(!worldRoot.Scene.GetAllComponents<AmbientLight>().Any())
		{
			var ambient = skyObject.Components.Create<AmbientLight>();
			ambient.Color = new Color(0.4f,0.44f,0.5f);
		}
	}

	internal static Material BuildMaterial(GothicClassicMount host, string path)
	{
		var material = Material.Create(path, "gothic_sky");
		material.Set("SkyLayer", ReadTexture("SKYDAY_LAYER0_A0-C.TEX"));
		material.Set("CloudLayer", ReadTexture("SKYDAY_LAYER1_A0-C.TEX"));
		material.Set("g_flCloudSpeed", 3.0f);
		return material;

		Texture ReadTexture(string name)
		{
			var source = host.MountedTexturePaths.FirstOrDefault(p=>string.Equals(System.IO.Path.GetFileName(p),name,StringComparison.OrdinalIgnoreCase));
			if(source is null) throw new FileNotFoundException($"Gothic sky texture {name} not found.");
			return Texture.Load(host.GetMountedResourceUri(host.GetMountedTextureResourcePath(source)));
		}
	}

	internal static Material BuildBarrierMaterial( GothicClassicMount host, string path )
	{
		// FromShader owns the hot-reload lifecycle. Material.Create registered a
		// named dynamic CMaterial2 that could outlive mount refreshes and crash the
		// material system when ShaderGraph replaced its shader resource.
		var material = Material.FromShader( "shaders/gothic_barrier.shader" );
		material.SetFeature( "F_RENDER_BACKFACES", 1 );
		// The sphere duplicates the U=0 and U=1 edge. A whole number of wave
		// periods keeps the animated UV offset identical on both vertices.
		material.Set( "g_flFreq", MathF.PI * 4f );
		var barrierTexture = ReadTexture( "BARRIERE-C.TEX" );
		material.Set( "barrier_tex1", barrierTexture );
		material.Set( "barrier_tex2", barrierTexture );
		return material;

		Texture ReadTexture( string name )
		{
			var source = host.MountedTexturePaths.FirstOrDefault( p => string.Equals( System.IO.Path.GetFileName( p ), name, StringComparison.OrdinalIgnoreCase ) );
			if ( source is null ) throw new FileNotFoundException( $"Gothic barrier texture {name} not found." );
			return Texture.Load( host.GetMountedResourceUri( host.GetMountedTextureResourcePath( source ) ) );
		}
	}

	internal static Model BuildBarrierModel( GothicClassicMount host, string path )
	{
		const int segments = 96;
		const int rings = 32;
		const float radius = 24500f;
		var vertices = new List<Vertex>( (segments + 1) * (rings + 1) );
		var positions = new List<Vector3>( (segments + 1) * (rings + 1) );
		var indices = new List<int>( segments * rings * 6 );
		for ( var ring = 0; ring <= rings; ring++ )
		{
			var v = ring / (float)rings;
			var elevation = v * MathF.PI * 0.5f;
			var horizontal = MathF.Cos( elevation );
			for ( var segment = 0; segment <= segments; segment++ )
			{
				var u = segment / (float)segments;
				var azimuth = u * MathF.PI * 2f;
				var normal = new Vector3( horizontal * MathF.Cos( azimuth ), horizontal * MathF.Sin( azimuth ), MathF.Sin( elevation ) );
				var position = normal * radius;
				positions.Add( position );
				vertices.Add( new Vertex( position, -normal, Vector3.Up, new Vector4( u, 1f - v, 0, 0 ) ) );
			}
		}
		for ( var ring = 0; ring < rings; ring++ )
		for ( var segment = 0; segment < segments; segment++ )
		{
			var a = ring * (segments + 1) + segment;
			var b = a + segments + 1;
			indices.AddRange( [a, b, a + 1, a + 1, b, b + 1] );
		}
		var mesh = new Sandbox.Mesh( Material.Load( host.GetMountedResourceUri( BarrierMaterialPath ) ) );
#pragma warning disable CS0618
		mesh.CreateVertexBuffer( vertices.Count, Vertex.Layout, vertices );
#pragma warning restore CS0618
		mesh.CreateIndexBuffer( indices.Count, indices );
		mesh.SetIndexRange( 0, indices.Count );
		mesh.Bounds = BBox.FromPoints( positions, 0 );
		var builder = Model.Builder.WithName( path );
		builder.AddMesh( mesh );
		return builder.Create();
	}
}

internal sealed class GothicSkyMaterialResource : ResourceLoader<GothicClassicMount>
{
	protected override object Load() => GothicWorldSky.BuildMaterial(Host,Path);
}

internal sealed class GothicBarrierMaterialResource : ResourceLoader<GothicClassicMount>
{
	protected override object Load() => GothicWorldSky.BuildBarrierMaterial( Host, Path );
}

internal sealed class GothicBarrierModelResource : ResourceLoader<GothicClassicMount>
{
	protected override object Load() => GothicWorldSky.BuildBarrierModel( Host, Path );
}

public static class GothicWorldSkyMenu
{
	[ConCmd("gothic_import_world_sky")]
	[Menu("Editor", "Gothic Classic Mount/Import World Sky and Barrier")]
	public static void Import()
	{
		var mount=Sandbox.Mounting.Directory.Get("gothicclassic") as GothicClassicMount;
		var session=SceneEditorSession.Active;
		if(mount is null || session?.Scene is null) return;
		using var sceneScope=session.Scene.Push();
		using var undo=session.UndoScope("Import Gothic Sky and Barrier").WithGameObjectCreations().Push();
		foreach(var renderer in session.Scene.GetAllComponents<ModelRenderer>().ToArray())
		{
			var source=mount.MountedWorldPaths.FirstOrDefault(p=>GothicWorldSky.IsColony(p) &&
				(string.Equals(renderer.Model?.Name,mount.GetMountedResourceUri(mount.GetMountedWorldResourcePath(p)),StringComparison.OrdinalIgnoreCase) ||
				string.Equals(renderer.Model?.Name,System.IO.Path.ChangeExtension(mount.GetMountedResourceUri(mount.GetMountedWorldSceneResourcePath(p)),".scene_mesh.vmdl"),StringComparison.OrdinalIgnoreCase)));
			if(source is null) continue;
			GothicWorldSky.Populate(mount,source,renderer.GameObject);
			Log.Info("Gothic sky and barrier added to WORLD.ZEN.");
		}
	}
}
