using System;
using System.Collections;
using System.IO;

namespace GothicClassicMount;

internal static class GothicWorldObjects
{
	private const float GothicScale = 0.36f;
	private const string ImportTag = "gothic_world_objects";

	public static void Populate( GothicClassicMount host, string worldPath, GameObject worldRoot )
	{
		// Preserve edits to existing prefab instances when rebuilding the world mesh.
		var existing = worldRoot.Children.FirstOrDefault( child => child.Tags.Has( ImportTag ) );
		if ( existing is not null )
		{
			RefreshCollision( host, worldPath, existing );
			Log.Info( $"Gothic world objects already imported: Instances={existing.Children.Count()} LinkedPrefabs={existing.Children.Count( child => child.IsPrefabInstanceRoot )}" );
		}
		var world = host.CreateWorld( worldPath );
		if ( ZenKitRuntime.GetProperty( world, "RootObjects" ) is not IEnumerable roots ) return;
		var models = host.MountedModelPaths
			.Where( p => p.EndsWith( ".MRM", StringComparison.OrdinalIgnoreCase ) || p.EndsWith( ".MSH", StringComparison.OrdinalIgnoreCase ) )
			.GroupBy( p => Path.GetFileNameWithoutExtension( p ), StringComparer.OrdinalIgnoreCase )
			.ToDictionary( g => g.Key, g => g.OrderBy( p => p.EndsWith( ".MRM", StringComparison.OrdinalIgnoreCase ) ? 0 : 1 ).First(), StringComparer.OrdinalIgnoreCase );
		var prefabs = new Dictionary<string, PrefabFile>( StringComparer.OrdinalIgnoreCase );
		var failedModels = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var existingVobs = existing?.Children.SelectMany( child => child.Tags )
			.Where( tag => tag.StartsWith( "gothic_vob_", StringComparison.Ordinal ) ).ToHashSet() ?? new HashSet<string>();
		var skipped = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		var imported = 0;
		var container = existing;
		if ( container is null )
		{
			container = new GameObject( "Gothic Objects" );
			container.Parent = worldRoot;
			container.LocalTransform = Transform.Zero;
			container.Tags.Add( ImportTag );
		}
		foreach ( var root in roots ) Visit( root );
		Log.Info( $"Gothic world prefabs [{worldPath}]: Instances={imported} UniqueModels={prefabs.Count} SkippedVisuals={skipped.Values.Sum()}" );
		foreach ( var item in skipped.OrderByDescending( p => p.Value ).Take( 12 ) )
			Log.Info( $"Gothic unsupported world visual [{worldPath}]: {item.Key} Count={item.Value}" );

		void Visit( object vob )
		{
			var vobTag = $"gothic_vob_{ZenKitRuntime.GetProperty<int>( vob, "Id" )}";
			var visual = ZenKitRuntime.GetProperty( vob, "Visual" );
			var visualName = ZenKitRuntime.GetProperty<string>( visual, "Name" );
			if ( !existingVobs.Contains( vobTag ) && ZenKitRuntime.GetProperty<bool>( vob, "ShowVisual" ) && !string.IsNullOrWhiteSpace( visualName ) )
			{
				var extension = Path.GetExtension( visualName );
				if ( (extension.Equals( ".3DS", StringComparison.OrdinalIgnoreCase ) || extension.Equals( ".MRM", StringComparison.OrdinalIgnoreCase ) || extension.Equals( ".MSH", StringComparison.OrdinalIgnoreCase )) &&
					models.TryGetValue( Path.GetFileNameWithoutExtension( visualName ), out var modelPath ) && !failedModels.Contains( modelPath ) )
				{
					try
					{
						if ( !prefabs.TryGetValue( modelPath, out var prefab ) )
						{
							prefab = PrefabFile.Load( host.GetMountedResourceUri( host.GetMountedPrefabResourcePath( modelPath ) ) );
							if ( prefab is null ) throw new InvalidOperationException( $"Prefab could not be loaded: {modelPath}" );
							prefabs.Add( modelPath, prefab );
						}
						var transform = GetTransform( vob );
						var name = ZenKitRuntime.GetProperty<string>( vob, "Name" );
						if ( string.IsNullOrWhiteSpace( name ) ) name = Path.GetFileNameWithoutExtension( visualName );
						var instance = GameObject.Clone( prefab, new Transform(), container, true, name );
						instance.LocalTransform = transform;
						instance.Tags.Add( vobTag );
						SetCollision( instance, ZenKitRuntime.GetProperty<bool>( vob, "CdDynamic" ) );
						imported++;
						existingVobs.Add( vobTag );
					}
					catch ( Exception exception )
					{
						failedModels.Add( modelPath );
						Log.Warning( $"Gothic world prefab '{visualName}' failed: {exception.Message}" );
						Skip( visualName );
					}
				}
				else Skip( visualName );
			}
			// Gothic stores absolute transforms, even for nested VOBs. Flatten beneath the world root.
			if ( ZenKitRuntime.GetProperty( vob, "Children" ) is IEnumerable children )
				foreach ( var child in children ) Visit( child );
		}

		void Skip( string visual ) => skipped[visual] = skipped.GetValueOrDefault( visual ) + 1;
	}

	private static void SetCollision( GameObject instance, bool enabled )
	{
		var renderer = instance.Components.Get<ModelRenderer>( FindMode.InSelf | FindMode.Enabled | FindMode.Disabled );
		if ( renderer?.Model is null ) return;
		var colliders = instance.Components.GetAll<ModelCollider>( FindMode.InSelf | FindMode.Enabled | FindMode.Disabled ).ToArray();
		var collider = colliders.FirstOrDefault() ?? instance.Components.Create<ModelCollider>();
		foreach ( var duplicate in colliders.Skip( 1 ).Where( c => !c.Enabled && c.Static && c.Model?.Name == renderer.Model.Name ) )
			duplicate.Destroy();
		collider.Model = renderer.Model;
		collider.Static = true;
		// cdDyn controls collisions with the player/NPCs; cdStatic is a Spacer placement flag.
		collider.Enabled = enabled;
	}

	private static void RefreshCollision( GothicClassicMount host, string worldPath, GameObject container )
	{
		var world = host.CreateWorld( worldPath );
		if ( ZenKitRuntime.GetProperty( world, "RootObjects" ) is not IEnumerable roots ) return;
		var sources = new List<object>();
		foreach ( var root in roots ) Visit( root );
		var byId = sources.ToDictionary( v => $"gothic_vob_{ZenKitRuntime.GetProperty<int>( v, "Id" )}" );
		var byPosition = sources.GroupBy( v => GetTransform( v ).Position )
			.ToDictionary( g => g.Key, g => g.ToList() );
		var paths = host.MountedModelPaths
			.Where( p => p.EndsWith( ".MRM", StringComparison.OrdinalIgnoreCase ) || p.EndsWith( ".MSH", StringComparison.OrdinalIgnoreCase ) )
			.GroupBy( p => host.GetMountedResourceUri( host.GetMountedModelResourcePath( p ) ), StringComparer.OrdinalIgnoreCase )
			.ToDictionary( g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase );
		var models = new Dictionary<string, Model>( StringComparer.OrdinalIgnoreCase );
		var enabledCount = 0;
		var disabledCount = 0;
		var unmatched = 0;
		foreach ( var instance in container.Children )
		{
			var renderer = instance.Components.Get<ModelRenderer>();
			if ( renderer?.Model is null || !paths.TryGetValue( renderer.Model.Name, out var path ) ) continue;
			object vob = null;
			foreach ( var tag in instance.Tags )
				if ( byId.TryGetValue( tag, out vob ) ) break;
			// Migrate older imports without IDs only when model and original position match.
			if ( vob is null && byPosition.TryGetValue( instance.LocalPosition, out var candidates ) )
				vob = candidates.FirstOrDefault( v => string.Equals(
					Path.GetFileNameWithoutExtension( ZenKitRuntime.GetProperty<string>( ZenKitRuntime.GetProperty( v, "Visual" ), "Name" ) ),
					Path.GetFileNameWithoutExtension( path ), StringComparison.OrdinalIgnoreCase ) );
			if ( vob is null )
			{
				var nearby = sources.Where( v => (GetTransform( v ).Position - instance.LocalPosition).LengthSquared < 0.0001f &&
					string.Equals( Path.GetFileNameWithoutExtension( ZenKitRuntime.GetProperty<string>( ZenKitRuntime.GetProperty( v, "Visual" ), "Name" ) ),
						Path.GetFileNameWithoutExtension( path ), StringComparison.OrdinalIgnoreCase ) ).Take( 2 ).ToList();
				if ( nearby.Count == 1 ) vob = nearby[0];
			}
			if ( vob is null )
			{
				unmatched++;
				Log.Warning( $"Gothic collision source not found for '{instance.Name}' at {instance.LocalPosition}; existing collision preserved." );
				continue;
			}
			if ( !models.TryGetValue( path, out var model ) )
			{
				model = GothicGeometryBuilder.BuildModel( host, path, renderer.Model.Name );
				models.Add( path, model );
			}
			renderer.Model = model;
			instance.Tags.Add( $"gothic_vob_{ZenKitRuntime.GetProperty<int>( vob, "Id" )}" );
			var enabled = ZenKitRuntime.GetProperty<bool>( vob, "CdDynamic" );
			SetCollision( instance, enabled );
			if ( enabled ) enabledCount++; else disabledCount++;
		}
		Log.Info( $"Gothic object collision [{worldPath}]: Enabled={enabledCount} Disabled={disabledCount} Unmatched={unmatched} Models={models.Count}" );

		void Visit( object vob )
		{
			sources.Add( vob );
			if ( ZenKitRuntime.GetProperty( vob, "Children" ) is IEnumerable children )
				foreach ( var child in children ) Visit( child );
		}
	}

	internal static Transform GetTransform( object vob )
	{
		dynamic position = ZenKitRuntime.GetProperty( vob, "Position" );
		var matrix = ZenKitRuntime.GetProperty( vob, "Rotation" );
		float M( string field ) => ZenKitRuntime.GetFieldValue<float>( matrix, field );
		// Conjugate the Gothic basis by the Y/Z swap used for imported model vertices.
		var forward = new Vector3( M( "M11" ), M( "M31" ), M( "M21" ) );
		var left = new Vector3( M( "M13" ), M( "M33" ), M( "M23" ) );
		var up = new Vector3( M( "M12" ), M( "M32" ), M( "M22" ) );
		var translation = new Vector3( (float)position.X, (float)position.Z, (float)position.Y ) * GothicScale;
		var scale = new Vector3( forward.Length, left.Length, up.Length );
		if ( Vector3.Dot( Vector3.Cross( forward, left ), up ) < 0f ) scale.y = -scale.y;
		return new Transform( translation, Rotation.LookAt( forward, up ), scale );
	}
}
