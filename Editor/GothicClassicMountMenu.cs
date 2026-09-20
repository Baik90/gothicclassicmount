using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Sandbox.Mounting;

namespace GothicClassicMount;

public static class GothicClassicMountMenu
{
	[ConCmd( "gothic_import_world_objects" )]
	[Menu( "Editor", "Gothic Classic Mount/Import World Object Prefabs" )]
	public static void ImportWorldObjectPrefabs()
	{
		var mount = Directory.Get( "gothicclassic" ) as GothicClassicMount;
		var scene = SceneEditorSession.Active?.Scene;
		if ( mount is null || scene is null ) return;
		using var scope = scene.Push();
		foreach ( var renderer in scene.GetAllComponents<ModelRenderer>().ToArray() )
		{
			var source = mount.MountedWorldPaths.FirstOrDefault( path =>
			{
				var uri = mount.GetMountedResourceUri( mount.GetMountedWorldResourcePath( path ) );
				return string.Equals( renderer.Model?.Name, uri, StringComparison.OrdinalIgnoreCase ) ||
					string.Equals( renderer.Model?.Name, System.IO.Path.ChangeExtension( uri, ".scene_mesh.vmdl" ), StringComparison.OrdinalIgnoreCase );
			} );
			if ( source is null ) continue;
			GothicWorldObjects.Populate( mount, source, renderer.GameObject );
		}
		RebuildWorldModels();
	}

	[ConCmd( "gothic_rebuild_worlds" )]
	[Menu( "Editor", "Gothic Classic Mount/Rebuild World Models" )]
	public static void RebuildWorldModels()
	{
		var mount = Directory.Get( "gothicclassic" ) as GothicClassicMount;
		var scene = SceneEditorSession.Active?.Scene;
		if ( mount is null || scene is null ) return;
		var rebuilt = 0;
		foreach ( var renderer in scene.GetAllComponents<ModelRenderer>().ToArray() )
		{
			var modelName = renderer.Model?.Name;
			var worldPath = mount.MountedWorldPaths.FirstOrDefault( path =>
				string.Equals( modelName, mount.GetMountedResourceUri( mount.GetMountedWorldResourcePath( path ) ), StringComparison.OrdinalIgnoreCase ) ||
				string.Equals( modelName, mount.GetMountedWorldResourcePath( path ), StringComparison.OrdinalIgnoreCase ) ||
				string.Equals( modelName, mount.GetEditorWorldAssetPath( path ), StringComparison.OrdinalIgnoreCase ) ||
				string.Equals( modelName, System.IO.Path.ChangeExtension( mount.GetMountedResourceUri( mount.GetMountedWorldResourcePath( path ) ), ".scene_mesh.vmdl" ), StringComparison.OrdinalIgnoreCase ) );
			if ( worldPath is null ) continue;
			var hasObjectPrefabs = renderer.GameObject.Children.Any( child => child.Tags.Has( "gothic_world_objects" ) );
			var modelPath = mount.GetMountedResourceUri( mount.GetMountedWorldResourcePath( worldPath ) );
			if ( hasObjectPrefabs ) modelPath = System.IO.Path.ChangeExtension( modelPath, ".scene_mesh.vmdl" );
			renderer.Model = GothicGeometryBuilder.BuildWorldModel( mount, worldPath, modelPath, includeTrees: !hasObjectPrefabs );
			var collider = renderer.GameObject.Components.Get<ModelCollider>() ?? renderer.GameObject.Components.Create<ModelCollider>();
			collider.Model = renderer.Model;
			collider.Static = true;
			rebuilt++;
		}
		Log.Info( $"Rebuilt {rebuilt} Gothic world renderers in the active scene." );
	}

	[Menu( "Editor", "Gothic Classic Mount/Mount Assets" )]
	public static void MountAssets()
	{
		var mount = Directory.Get( "gothicclassic" );
		if ( mount is null )
		{
			EditorUtility.DisplayDialog( "Gothic Classic Mount", "Mount 'gothicclassic' wurde nicht gefunden.", "OK" );
			return;
		}

		if ( !mount.IsMounted )
			Directory.Mount( "gothicclassic" );

		if ( mount is GothicClassicMount gothicMount )
			GothicEditorAssetSync.EnsureSynced( gothicMount );

		var resourceCount = mount.Resources?.Count() ?? 0;
		EditorUtility.DisplayDialog( "Gothic Classic Mount", $"Mount aktiv. Ressourcen: {resourceCount}", "OK" );
	}

	[Menu( "Editor", "Gothic Classic Mount/Run Diagnostics" )]
	public static void RunDiagnostics()
	{
		var mount = Directory.Get( "gothicclassic" ) as GothicClassicMount;
		if ( mount is null )
		{
			EditorUtility.DisplayDialog( "Gothic Classic Mount", "Mount 'gothicclassic' wurde nicht gefunden.", "OK" );
			return;
		}

		if ( !mount.IsMounted )
			Directory.Mount( "gothicclassic" );

		GothicEditorAssetSync.EnsureSynced( mount );

		var firstTexturePath = mount.MountedTexturePaths.FirstOrDefault();
		var firstModelPath = mount.MountedModelPaths.FirstOrDefault();
		var firstWorldPath = mount.MountedWorldPaths.FirstOrDefault();

		try
		{
			if ( !string.IsNullOrWhiteSpace( firstTexturePath ) )
			{
				mount.LoadTexture( firstTexturePath );
				Log.Info( $"Gothic texture diagnostic succeeded: {firstTexturePath}" );
			}

			if ( !string.IsNullOrWhiteSpace( firstModelPath ) )
			{
				DumpModelMaterials( mount, firstModelPath );
				GothicGeometryBuilder.BuildModel( mount, firstModelPath, mount.GetMountedModelResourcePath( firstModelPath ) );
				Log.Info( $"Gothic model diagnostic succeeded: {firstModelPath}" );
			}

			RunWorldDiagnostics( mount, firstWorldPath );

			EditorUtility.DisplayDialog( "Gothic Classic Mount", "Diagnostik ausgefuehrt. Details stehen in der Console.", "OK" );
		}
		catch ( Exception exception )
		{
			Log.Warning( $"Gothic diagnostics failed: {exception}" );
			EditorUtility.DisplayDialog( "Gothic Classic Mount", "Diagnostik fehlgeschlagen. Details stehen in der Console.", "OK" );
		}
	}

	[Menu( "Editor", "Gothic Classic Mount/Validate Mounted Resource Paths" )]
	public static void ValidateMountedResourcePaths()
	{
		var mount = Directory.Get( "gothicclassic" ) as GothicClassicMount;
		if ( mount is null )
		{
			EditorUtility.DisplayDialog( "Gothic Classic Mount", "Mount 'gothicclassic' wurde nicht gefunden.", "OK" );
			return;
		}

		if ( !mount.IsMounted )
			Directory.Mount( "gothicclassic" );

		GothicEditorAssetSync.EnsureSynced( mount );

		try
		{
			var firstTexturePath = mount.MountedTexturePaths.FirstOrDefault();
			if ( !string.IsNullOrWhiteSpace( firstTexturePath ) )
			{
				var editorTexturePath = mount.GetEditorTextureAssetPath( firstTexturePath );
				var runtimeTexturePath = mount.GetMountedTextureResourcePath( firstTexturePath );
				var runtimeTextureUri = mount.GetMountedResourceUri( runtimeTexturePath );
				var textureAsset = AssetSystem.FindByPath( editorTexturePath );
				var textureAssetByUri = AssetSystem.FindByPath( runtimeTextureUri );
				var loadedEditorTexture = Texture.Load( editorTexturePath, false );
				var loadedRuntimeTexture = Texture.Load( runtimeTextureUri, false );
				Log.Info( $"Gothic mounted texture validation: EditorPath={editorTexturePath}; RuntimeUri={runtimeTextureUri}; AssetPath={(textureAsset is null ? "<null>" : textureAsset.Path)}; AssetUri={(textureAssetByUri is null ? "<null>" : textureAssetByUri.Path)}; LoadedEditor={(loadedEditorTexture is null ? "<null>" : loadedEditorTexture.GetType().FullName)}; LoadedRuntime={(loadedRuntimeTexture is null ? "<null>" : loadedRuntimeTexture.GetType().FullName)}" );
			}

			var firstModelPath = mount.MountedModelPaths.FirstOrDefault();
			if ( !string.IsNullOrWhiteSpace( firstModelPath ) )
			{
				var editorModelPath = mount.GetEditorModelAssetPath( firstModelPath );
				var runtimeModelPath = mount.GetMountedModelResourcePath( firstModelPath );
				var runtimeModelUri = mount.GetMountedResourceUri( runtimeModelPath );
				var modelAsset = AssetSystem.FindByPath( editorModelPath );
				var modelAssetByUri = AssetSystem.FindByPath( runtimeModelUri );
				var loadedEditorModel = Model.Load( editorModelPath );
				var loadedRuntimeModel = Model.Load( runtimeModelUri );
				Log.Info( $"Gothic mounted model validation: EditorPath={editorModelPath}; RuntimeUri={runtimeModelUri}; AssetPath={(modelAsset is null ? "<null>" : modelAsset.Path)}; AssetUri={(modelAssetByUri is null ? "<null>" : modelAssetByUri.Path)}; LoadedEditor={(loadedEditorModel is null ? "<null>" : loadedEditorModel.Name)}; LoadedRuntime={(loadedRuntimeModel is null ? "<null>" : loadedRuntimeModel.Name)}" );
			}

			var firstWorldPath = mount.MountedWorldPaths.FirstOrDefault();
			if ( !string.IsNullOrWhiteSpace( firstWorldPath ) )
			{
				var runtimeWorldScenePath = mount.GetMountedWorldSceneResourcePath( firstWorldPath );
				var runtimeWorldSceneUri = mount.GetMountedResourceUri( runtimeWorldScenePath );
				var worldSceneAssetByUri = AssetSystem.FindByPath( runtimeWorldSceneUri );
				var loadedRuntimeScene = SceneFile.Load( runtimeWorldSceneUri );
				Log.Info( $"Gothic mounted world scene validation: RuntimeUri={runtimeWorldSceneUri}; AssetUri={(worldSceneAssetByUri is null ? "<null>" : worldSceneAssetByUri.Path)}; LoadedRuntimeScene={(loadedRuntimeScene is null ? "<null>" : loadedRuntimeScene.ResourcePath)}" );
			}

			EditorUtility.DisplayDialog( "Gothic Classic Mount", "Mounted-Path-Validierung ausgefuehrt. Details stehen in der Console.", "OK" );
		}
		catch ( Exception exception )
		{
			Log.Warning( $"Gothic mounted path validation failed: {exception}" );
			EditorUtility.DisplayDialog( "Gothic Classic Mount", "Mounted-Path-Validierung fehlgeschlagen. Details stehen in der Console.", "OK" );
		}
	}

	[Menu( "Editor", "Gothic Classic Mount/Analyze World Visuals" )]
	public static void AnalyzeWorldVisuals()
	{
		var mount = Directory.Get( "gothicclassic" ) as GothicClassicMount;
		if ( mount is null )
		{
			EditorUtility.DisplayDialog( "Gothic Classic Mount", "Mount 'gothicclassic' wurde nicht gefunden.", "OK" );
			return;
		}

		if ( !mount.IsMounted )
			Directory.Mount( "gothicclassic" );

		var worldPath = mount.MountedWorldPaths
			.OrderByDescending( path => path.EndsWith( "/WORLD.ZEN", StringComparison.OrdinalIgnoreCase ) )
			.ThenBy( path => path, StringComparer.OrdinalIgnoreCase )
			.FirstOrDefault();

		if ( string.IsNullOrWhiteSpace( worldPath ) )
		{
			EditorUtility.DisplayDialog( "Gothic Classic Mount", "Keine gemountete Welt gefunden.", "OK" );
			return;
		}

		try
		{
			AnalyzeWorldVisuals( mount, worldPath );
			EditorUtility.DisplayDialog( "Gothic Classic Mount", "Welt-Analyse ausgefuehrt. Details stehen in der Console.", "OK" );
		}
		catch ( Exception exception )
		{
			Log.Warning( $"Gothic world visual analysis failed: {exception}" );
			EditorUtility.DisplayDialog( "Gothic Classic Mount", "Welt-Analyse fehlgeschlagen. Details stehen in der Console.", "OK" );
		}
	}

	[Menu( "Editor", "Gothic Classic Mount/Analyze World Mesh Overlap" )]
	public static void AnalyzeWorldMeshOverlap()
	{
		var mount = Directory.Get( "gothicclassic" ) as GothicClassicMount;
		if ( mount is null )
		{
			EditorUtility.DisplayDialog( "Gothic Classic Mount", "Mount 'gothicclassic' wurde nicht gefunden.", "OK" );
			return;
		}

		if ( !mount.IsMounted )
			Directory.Mount( "gothicclassic" );

		var worldPath = mount.MountedWorldPaths
			.OrderByDescending( path => path.EndsWith( "/WORLD.ZEN", StringComparison.OrdinalIgnoreCase ) )
			.ThenBy( path => path, StringComparer.OrdinalIgnoreCase )
			.FirstOrDefault();

		if ( string.IsNullOrWhiteSpace( worldPath ) )
		{
			EditorUtility.DisplayDialog( "Gothic Classic Mount", "Keine gemountete Welt gefunden.", "OK" );
			return;
		}

		try
		{
			AnalyzeWorldMeshOverlap( mount, worldPath );
			EditorUtility.DisplayDialog( "Gothic Classic Mount", "Mesh-Overlap-Analyse ausgefuehrt. Details stehen in der Console.", "OK" );
		}
		catch ( Exception exception )
		{
			Log.Warning( $"Gothic world mesh overlap analysis failed: {exception}" );
			EditorUtility.DisplayDialog( "Gothic Classic Mount", "Mesh-Overlap-Analyse fehlgeschlagen. Details stehen in der Console.", "OK" );
		}
	}

	private static void DumpModelMaterials( GothicClassicMount mount, string virtualPath )
	{
		var extension = System.IO.Path.GetExtension( virtualPath );
		if ( extension.Equals( ".MRM", StringComparison.OrdinalIgnoreCase ) )
		{
			DumpSubMeshMaterials( mount, ZenKitRuntime.CreateMultiResolutionMesh( mount.RequireVfs(), virtualPath ), virtualPath );
			return;
		}

		if ( extension.Equals( ".MDM", StringComparison.OrdinalIgnoreCase ) )
		{
			dynamic modelMesh = ZenKitRuntime.CreateModelMesh( mount.RequireVfs(), virtualPath );
			DumpModelMeshMaterials( mount, modelMesh, virtualPath );

			return;
		}

		if ( extension.Equals( ".MDL", StringComparison.OrdinalIgnoreCase ) )
		{
			dynamic model = ZenKitRuntime.CreateModel( mount.RequireVfs(), virtualPath );
			DumpModelMeshMaterials( mount, model.Mesh, virtualPath );
			return;
		}

		if ( extension.Equals( ".MSH", StringComparison.OrdinalIgnoreCase ) )
		{
			dynamic mesh = ZenKitRuntime.CreateMesh( mount.RequireVfs(), virtualPath );
			var materials = ((System.Collections.IEnumerable)mesh.Materials).Cast<object>().ToList();
			for ( var i = 0; i < materials.Count; i++ )
			{
				Log.Info( $"Gothic mesh material [{virtualPath}] #{i}: {mount.DescribeMaterial( materials[i], $"mesh_{i}" )}" );
			}
		}
	}

	private static void DumpWorldMaterials( GothicClassicMount mount, string virtualPath )
	{
		dynamic world = ZenKitRuntime.CreateWorld( mount.RequireVfs(), virtualPath );
		DumpWorldStructure( world, virtualPath );

		var mesh = ZenKitRuntime.GetProperty( world, "Mesh" );
		if ( mesh is null )
		{
			Log.Info( $"Gothic world material diagnostic found no mesh for '{virtualPath}'." );
			return;
		}

		dynamic worldMesh = mesh;
		var materials = ((System.Collections.IEnumerable)worldMesh.Materials).Cast<object>().ToList();
		for ( var i = 0; i < materials.Count; i++ )
		{
			Log.Info( $"Gothic world material [{virtualPath}] #{i}: {mount.DescribeMaterial( materials[i], $"world_{i}" )}" );
		}
	}

	private static void DumpWorldStructure( object world, string virtualPath )
	{
		Log.Info( $"Gothic world structure [{virtualPath}]" );
		DumpInterestingMembers( "World", world );

		var mesh = ZenKitRuntime.GetProperty( world, "Mesh" );
		if ( mesh is not null )
		{
			Log.Info( $"World mesh type: {mesh.GetType().FullName}" );
			DumpInterestingMembers( "World.Mesh", mesh );
			LogEnumerableCount( "World.Mesh.Materials", ZenKitRuntime.GetProperty( mesh, "Materials" ) );
			LogEnumerableCount( "World.Mesh.Polygons", ZenKitRuntime.GetProperty( mesh, "Polygons" ) );
		}

		var bspTree = ZenKitRuntime.GetProperty( world, "BspTree" ) ?? ZenKitRuntime.GetProperty( world, "Bsp" );
		if ( bspTree is not null )
		{
			Log.Info( $"World BSP type: {bspTree.GetType().FullName}" );
			DumpInterestingMembers( "World.Bsp", bspTree );
			LogEnumerableCount( "World.Bsp.Sectors", ZenKitRuntime.GetProperty( bspTree, "Sectors" ) );
			LogEnumerableCount( "World.Bsp.Nodes", ZenKitRuntime.GetProperty( bspTree, "Nodes" ) );
			LogEnumerableCount( "World.Bsp.Leaves", ZenKitRuntime.GetProperty( bspTree, "Leaves" ) );
		}

		var rootVob = ZenKitRuntime.GetProperty( world, "RootVob" ) ?? ZenKitRuntime.GetProperty( world, "Root" );
		if ( rootVob is not null )
		{
			Log.Info( $"World root vob type: {rootVob.GetType().FullName}" );
			DumpInterestingMembers( "World.RootVob", rootVob );
			LogEnumerableCount( "World.RootVob.Children", ZenKitRuntime.GetProperty( rootVob, "Children" ) );
		}

		var vobs = ZenKitRuntime.GetProperty( world, "Vobs" );
		if ( vobs is not null )
		{
			LogEnumerableCount( "World.Vobs", vobs );
			LogInterestingEnumerableSample( "World.Vobs", vobs );
		}
	}

	private static void AnalyzeWorldVisuals( GothicClassicMount mount, string worldPath )
	{
		dynamic world = ZenKitRuntime.CreateWorld( mount.RequireVfs(), worldPath );
		var visualUsage = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		var flaggedVobs = new List<string>();
		var totalVobs = 0;
		var rootVob = ZenKitRuntime.GetProperty( world, "RootVob" ) ?? ZenKitRuntime.GetProperty( world, "Root" );
		if ( rootVob is not null )
		{
			TraverseVobs( rootVob, 0 );
		}
		else
		{
			Log.Info( $"World '{worldPath}' exposed no RootVob/Root. Inspecting direct Vob containers." );
			DumpInterestingMembers( "World.VisualAnalysis", world );

			var directVobs = ZenKitRuntime.GetProperty( world, "Vobs" ) as IEnumerable;
			if ( directVobs is null )
				throw new InvalidOperationException( $"World '{worldPath}' exposed neither root vob nor direct vob collection." );

			foreach ( var vob in directVobs )
				TraverseVobs( vob, 0 );
		}

		Log.Info( $"World visual analysis [{worldPath}] TotalVobs={totalVobs} UniqueVisuals={visualUsage.Count}" );

		foreach ( var pair in visualUsage
			.Where( x => x.Value > 1 )
			.OrderByDescending( x => x.Value )
			.ThenBy( x => x.Key, StringComparer.OrdinalIgnoreCase )
			.Take( 40 ) )
		{
			Log.Info( $"World visual duplicate: {pair.Key} Count={pair.Value}" );
		}

		foreach ( var line in flaggedVobs.Take( 80 ) )
		{
			Log.Info( line );
		}

		void TraverseVobs( object vob, int depth )
		{
			if ( vob is null || depth > 64 )
				return;

			totalVobs++;

			var vobName = ReadStringMember( vob, "PresetName" )
				?? ReadStringMember( vob, "VobName" )
				?? ReadStringMember( vob, "Name" )
				?? "<unnamed>";

			var visual = ReadVisualPath( vob );
			if ( !string.IsNullOrWhiteSpace( visual ) )
			{
				visualUsage[visual] = visualUsage.TryGetValue( visual, out var count ) ? count + 1 : 1;
			}

			var interesting = ReadInterestingVobFlags( vob );
			if ( !string.IsNullOrWhiteSpace( visual ) || !string.IsNullOrWhiteSpace( interesting ) )
			{
				flaggedVobs.Add( $"World vob depth={depth} name={vobName} visual={visual ?? "<none>"} {interesting}".TrimEnd() );
			}

			var children = ZenKitRuntime.GetProperty( vob, "Children" ) as IEnumerable;
			if ( children is null )
				return;

			foreach ( var child in children )
				TraverseVobs( child, depth + 1 );
		}
	}

	private static void AnalyzeWorldMeshOverlap( GothicClassicMount mount, string worldPath )
	{
		dynamic world = ZenKitRuntime.CreateWorld( mount.RequireVfs(), worldPath );
		var mesh = ZenKitRuntime.GetProperty( world, "Mesh" );
		if ( mesh is null )
			throw new InvalidOperationException( $"World '{worldPath}' exposed no mesh." );

		var polygons = ((IEnumerable)ZenKitRuntime.GetProperty( mesh, "Polygons" )).Cast<object>().ToList();
		if ( polygons.Count == 0 )
			throw new InvalidOperationException( $"World '{worldPath}' mesh had no polygons." );

		var dsu = new int[polygons.Count];
		for ( var i = 0; i < dsu.Length; i++ )
			dsu[i] = i;

		var positionOwner = new Dictionary<int, int>();
		for ( var polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++ )
		{
			var positionIndices = ((IEnumerable)ZenKitRuntime.GetProperty( polygons[polygonIndex], "PositionIndices" )).Cast<object>();
			foreach ( var rawIndex in positionIndices )
			{
				var positionIndex = Convert.ToInt32( rawIndex );
				if ( positionOwner.TryGetValue( positionIndex, out var existing ) )
				{
					Union( polygonIndex, existing );
				}
				else
				{
					positionOwner[positionIndex] = polygonIndex;
				}
			}
		}

		var components = new Dictionary<int, MeshComponentInfo>();
		for ( var polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++ )
		{
			var root = Find( polygonIndex );
			if ( !components.TryGetValue( root, out var component ) )
			{
				component = new MeshComponentInfo();
				components[root] = component;
			}

			component.PolygonCount++;

			var materialIndex = ZenKitRuntime.GetProperty<int>( polygons[polygonIndex], "MaterialIndex" );
			component.MaterialIndices.Add( materialIndex );

			var positionIndices = ((IEnumerable)ZenKitRuntime.GetProperty( polygons[polygonIndex], "PositionIndices" )).Cast<object>();
			foreach ( var rawIndex in positionIndices )
			{
				dynamic position = mesh.GetPosition( Convert.ToInt32( rawIndex ) );
				component.Points.Add( new Vector3( (float)position.X, (float)position.Y, (float)position.Z ) );
			}
		}

		var materials = ((IEnumerable)ZenKitRuntime.GetProperty( mesh, "Materials" )).Cast<object>().ToList();
		var componentList = components.Values
			.Select( component =>
			{
				component.Bounds = component.Points.Count > 0 ? BBox.FromPoints( component.Points, 0f ) : default;
				return component;
			} )
			.OrderByDescending( component => component.PolygonCount )
			.ToList();

		Log.Info( $"World mesh overlap analysis [{worldPath}] Polygons={polygons.Count} Components={componentList.Count}" );
		var primaryBounds = componentList[0].Bounds;

		for ( var i = 0; i < Math.Min( 20, componentList.Count ); i++ )
		{
			var component = componentList[i];
			var materialSummary = string.Join( ",", component.MaterialIndices
				.OrderBy( x => x )
				.Take( 6 )
				.Select( index => DescribeWorldMaterial( mount, materials, index ) ) );
			var isContained = i > 0 && IsContainedWithin( component.Bounds, primaryBounds, 8f );
			var wouldCull = i > 0 && isContained && component.PolygonCount <= 800;
			Log.Info( $"World mesh component #{i} Polygons={component.PolygonCount} Materials=[{materialSummary}] Contained={isContained} CullCandidate={wouldCull} Bounds={component.Bounds}" );
		}

		for ( var i = 0; i < Math.Min( 40, componentList.Count ); i++ )
		{
			for ( var j = i + 1; j < Math.Min( 40, componentList.Count ); j++ )
			{
				var overlap = BoundsOverlapRatio( componentList[i].Bounds, componentList[j].Bounds );
				if ( overlap < 0.60f )
					continue;

				Log.Info( $"World mesh overlap components #{i}<->#{j} Overlap={overlap:0.000} A={componentList[i].PolygonCount} B={componentList[j].PolygonCount}" );
			}
		}

		int Find( int index )
		{
			while ( dsu[index] != index )
			{
				dsu[index] = dsu[dsu[index]];
				index = dsu[index];
			}

			return index;
		}

		void Union( int a, int b )
		{
			var rootA = Find( a );
			var rootB = Find( b );
			if ( rootA != rootB )
				dsu[rootB] = rootA;
		}
	}

	private static float BoundsOverlapRatio( BBox a, BBox b )
	{
		var min = Vector3.Max( a.Mins, b.Mins );
		var max = Vector3.Min( a.Maxs, b.Maxs );
		var size = max - min;
		if ( size.x <= 0f || size.y <= 0f || size.z <= 0f )
			return 0f;

		var intersection = size.x * size.y * size.z;
		var volumeA = Math.Max( 0.0001f, a.Size.x * a.Size.y * a.Size.z );
		var volumeB = Math.Max( 0.0001f, b.Size.x * b.Size.y * b.Size.z );
		return intersection / Math.Min( volumeA, volumeB );
	}

	private static bool IsContainedWithin( BBox inner, BBox outer, float margin )
	{
		return inner.Mins.x >= outer.Mins.x - margin
			&& inner.Mins.y >= outer.Mins.y - margin
			&& inner.Mins.z >= outer.Mins.z - margin
			&& inner.Maxs.x <= outer.Maxs.x + margin
			&& inner.Maxs.y <= outer.Maxs.y + margin
			&& inner.Maxs.z <= outer.Maxs.z + margin;
	}

	private static string DescribeWorldMaterial( GothicClassicMount mount, List<object> materials, int materialIndex )
	{
		if ( materialIndex < 0 || materialIndex >= materials.Count )
			return materialIndex.ToString();

		var material = materials[materialIndex];
		var name = ZenKitRuntime.GetProperty<string>( material, "Name" ) ?? $"world_{materialIndex}";
		return $"{materialIndex}:{name}";
	}

	private sealed class MeshComponentInfo
	{
		public int PolygonCount { get; set; }
		public List<Vector3> Points { get; } = new();
		public HashSet<int> MaterialIndices { get; } = new();
		public BBox Bounds { get; set; }
	}

	private static string ReadVisualPath( object instance )
	{
		foreach ( var memberName in new[] { "Visual", "VisualName", "Model", "Mesh" } )
		{
			var direct = ReadStringMember( instance, memberName );
			if ( !string.IsNullOrWhiteSpace( direct ) )
				return direct;

			var nested = ZenKitRuntime.GetProperty( instance, memberName ) ?? ZenKitRuntime.GetField( instance, memberName );
			if ( nested is null )
				continue;

			var nestedName = ReadStringMember( nested, "Name" )
				?? ReadStringMember( nested, "Path" )
				?? ReadStringMember( nested, "FileName" );
			if ( !string.IsNullOrWhiteSpace( nestedName ) )
				return nestedName;

			var nestedText = nested.ToString();
			if ( !string.IsNullOrWhiteSpace( nestedText ) && !nestedText.StartsWith( "ZenKit.", StringComparison.OrdinalIgnoreCase ) )
				return nestedText;
		}

		return null;
	}

	private static string ReadInterestingVobFlags( object instance )
	{
		var values = new List<string>();
		var keywords = new[] { "lod", "visual", "show", "far", "dist", "range" };

		foreach ( var property in instance.GetType().GetProperties( BindingFlags.Public | BindingFlags.Instance ) )
		{
			if ( property.GetIndexParameters().Length > 0 )
				continue;

			if ( !keywords.Any( keyword => property.Name.Contains( keyword, StringComparison.OrdinalIgnoreCase ) ) )
				continue;

			try
			{
				var value = property.GetValue( instance );
				if ( value is null || value is IEnumerable and not string )
					continue;

				values.Add( $"{property.Name}={value}" );
			}
			catch
			{
			}
		}

		foreach ( var field in instance.GetType().GetFields( BindingFlags.Public | BindingFlags.Instance ) )
		{
			if ( !keywords.Any( keyword => field.Name.Contains( keyword, StringComparison.OrdinalIgnoreCase ) ) )
				continue;

			try
			{
				var value = field.GetValue( instance );
				if ( value is null || value is IEnumerable and not string )
					continue;

				values.Add( $"{field.Name}={value}" );
			}
			catch
			{
			}
		}

		return values.Count == 0 ? string.Empty : string.Join( ", ", values.Distinct( StringComparer.OrdinalIgnoreCase ) );
	}

	private static string ReadStringMember( object instance, string memberName )
	{
		if ( instance is null )
			return null;

		var property = instance.GetType().GetProperty( memberName, BindingFlags.Public | BindingFlags.Instance );
		if ( property?.PropertyType == typeof( string ) )
			return property.GetValue( instance ) as string;

		var field = instance.GetType().GetField( memberName, BindingFlags.Public | BindingFlags.Instance );
		if ( field?.FieldType == typeof( string ) )
			return field.GetValue( instance ) as string;

		return null;
	}

	private static void DumpInterestingMembers( string label, object instance )
	{
		if ( instance is null )
			return;

		var keywords = new[] { "lod", "visual", "bsp", "sector", "dist", "range", "far" };
		foreach ( var property in instance.GetType().GetProperties( BindingFlags.Public | BindingFlags.Instance ) )
		{
			if ( property.GetIndexParameters().Length > 0 )
				continue;

			if ( !keywords.Any( keyword => property.Name.Contains( keyword, StringComparison.OrdinalIgnoreCase ) ) )
				continue;

			LogMemberValue( $"{label}.{property.Name}", () => property.GetValue( instance ) );
		}

		foreach ( var field in instance.GetType().GetFields( BindingFlags.Public | BindingFlags.Instance ) )
		{
			if ( !keywords.Any( keyword => field.Name.Contains( keyword, StringComparison.OrdinalIgnoreCase ) ) )
				continue;

			LogMemberValue( $"{label}.{field.Name}", () => field.GetValue( instance ) );
		}
	}

	private static void LogEnumerableCount( string label, object value )
	{
		if ( value is not IEnumerable enumerable || value is string )
			return;

		var count = 0;
		foreach ( var _ in enumerable )
			count++;

		Log.Info( $"{label}.Count={count}" );
	}

	private static void LogInterestingEnumerableSample( string label, object value )
	{
		if ( value is not IEnumerable enumerable || value is string )
			return;

		var index = 0;
		foreach ( var item in enumerable )
		{
			if ( item is null )
				continue;

			Log.Info( $"{label}[{index}]={item.GetType().FullName}" );
			DumpInterestingMembers( $"{label}[{index}]", item );
			index++;
			if ( index >= 3 )
				break;
		}
	}

	private static void LogMemberValue( string label, Func<object> readValue )
	{
		try
		{
			var value = readValue();
			if ( value is null )
			{
				Log.Info( $"{label}=<null>" );
				return;
			}

			if ( value is IEnumerable enumerable and not string )
			{
				var count = 0;
				foreach ( var _ in enumerable )
					count++;

				Log.Info( $"{label}=<{value.GetType().Name}> Count={count}" );
				return;
			}

			Log.Info( $"{label}={value}" );
		}
		catch ( Exception exception )
		{
			Log.Info( $"{label}=<error:{exception.GetType().Name}>" );
		}
	}

	private static void RunWorldDiagnostics( GothicClassicMount mount, string preferredWorldPath )
	{
		var worldPaths = mount.MountedWorldPaths;
		if ( worldPaths.Count == 0 )
			return;

		var orderedPaths = worldPaths
			.OrderByDescending( path => string.Equals( path, preferredWorldPath, StringComparison.OrdinalIgnoreCase ) )
			.ThenBy( path => path, StringComparer.OrdinalIgnoreCase )
			.ToArray();

		Exception lastException = null;
		foreach ( var worldPath in orderedPaths )
		{
			try
			{
				DumpWorldMaterials( mount, worldPath );
				GothicGeometryBuilder.BuildWorldModel( mount, worldPath, mount.GetMountedWorldResourcePath( worldPath ) );
				Log.Info( $"Gothic world diagnostic succeeded: {worldPath}" );
				return;
			}
			catch ( InvalidOperationException exception ) when ( exception.Message.Contains( "had no polygons", StringComparison.OrdinalIgnoreCase ) )
			{
				lastException = exception;
				Log.Info( $"Skipping non-world ZEN during diagnostics: {worldPath}" );
			}
		}

		if ( lastException is not null )
			throw lastException;
	}

	private static void DumpSubMeshMaterials( GothicClassicMount mount, object meshObject, string virtualPath )
	{
		dynamic mesh = meshObject;
		var index = 0;
		foreach ( var subMesh in (System.Collections.IEnumerable)mesh.SubMeshes )
		{
			var material = ZenKitRuntime.GetProperty( subMesh, "Material" );
			var materialName = ZenKitRuntime.GetProperty<string>( material, "Name" ) ?? $"submesh_{index}";
			Log.Info( $"Gothic submesh material [{virtualPath}] #{index}: {mount.DescribeMaterial( material, materialName )}" );
			index++;
		}
	}

	private static void DumpModelMeshMaterials( GothicClassicMount mount, dynamic modelMesh, string virtualPath )
	{
		var dumpedAny = false;

		foreach ( var softSkinMesh in (System.Collections.IEnumerable)modelMesh.Meshes )
		{
			var softMesh = ZenKitRuntime.GetProperty( softSkinMesh, "Mesh" );
			if ( softMesh is null )
				continue;

			DumpSubMeshMaterials( mount, softMesh, virtualPath );
			dumpedAny = true;
		}

		if ( dumpedAny )
			return;

		foreach ( System.Collections.DictionaryEntry attachment in (System.Collections.IDictionary)modelMesh.Attachments )
		{
			if ( attachment.Value is null )
				continue;

			DumpSubMeshMaterials( mount, attachment.Value, virtualPath );
			dumpedAny = true;
		}

		if ( !dumpedAny )
			Log.Info( $"Gothic model material diagnostic found no mesh entries for '{virtualPath}'." );
	}
}
