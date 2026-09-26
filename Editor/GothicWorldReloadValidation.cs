using System;
using System.Text.Json.Nodes;

namespace GothicClassicMount;

public static class GothicWorldReloadValidation
{
	/// <summary>
	/// Round-trip the active WORLD scene through JSON and a fresh mount. The
	/// temporary scene must resolve saved prefab overrides without a warm cache.
	/// </summary>
	[ConCmd( "gothic_validate_world_reload" )]
	public static async void ValidateReload()
	{
		try
		{
			var mount = Sandbox.Mounting.Directory.Get( "gothicclassic" ) as GothicClassicMount;
			var active = SceneEditorSession.Active?.Scene;
			if ( mount is null || active is null ) return;
			if ( !active.GetAllComponents<SkyBox2D>().Any() )
				throw new InvalidOperationException( "Open the imported WORLD scene with its sky before running this check." );

			var before = active.GetAllComponents<ModelRenderer>()
				.ToDictionary( r => r.Id, r => (r.Model?.Name, r.GameObject.WorldTransform) );
			string json;
			using ( active.Push() )
			{
				var snapshot = new SceneFile
				{
					GameObjects = active.Children.Select( c => c.Serialize() ).Where( j => j is not null ).ToArray(),
					SceneProperties = new JsonObject()
				};
				json = snapshot.Serialize().ToJsonString();
			}

			await mount.RefreshInternal();
			var worldSource = mount.MountedWorldPaths.First( GothicWorldSky.IsColony );
			var worldUri = mount.GetMountedResourceUri( mount.GetMountedWorldSceneResourcePath( worldSource ) );
			var generated = await mount.GetByPath( worldUri ).GetOrCreate() as SceneFile;
			var generatedJson = generated?.GameObjects is null ? string.Empty : string.Join( "", generated.GameObjects.Select( node => node.ToJsonString() ) );
			if ( !generatedJson.Contains( "Gothic Barrier Hemisphere", StringComparison.Ordinal ) )
				throw new InvalidOperationException( "The regenerated mounted WORLD scene has no barrier hemisphere." );
			var file = new SceneFile();
			file.LoadFromJson( json );
			var test = new Scene();
			try
			{
				using var scope = test.Push();
				if ( !test.Load( file ) ) throw new InvalidOperationException( "Could not load the scene snapshot." );
				var renderers = test.GetAllComponents<ModelRenderer>().ToArray();
				var mismatches = renderers.Count( r => r.Model is null || r.Model.IsError ||
					!before.TryGetValue( r.Id, out var old ) || old.Item1 != r.Model.Name || old.Item2 != r.GameObject.WorldTransform );
				var skies = test.GetAllComponents<SkyBox2D>().ToArray();
				var skyPath = mount.GetMountedResourceUri( GothicWorldSky.MaterialPath );
				Log.Info( $"Gothic reload validation: Before={before.Count} After={renderers.Length} Mismatches={mismatches} Skies={skies.Length} SkyMaterial={skies.FirstOrDefault()?.SkyMaterial?.Name}" );
				if ( mismatches != 0 || before.Count != renderers.Length || skies.Length != 1 || skies[0].SkyMaterial?.Name != skyPath )
					throw new InvalidOperationException( "World reload validation failed." );
			}
			finally
			{
				test.Destroy();
			}
		}
		catch ( Exception exception )
		{
			Log.Error( exception );
		}
	}
}
