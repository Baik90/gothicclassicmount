using System;
using System.Collections;
using System.IO;

namespace GothicClassicMount;

internal static class GothicGeometryBuilder
{
	private const float GothicScale = 0.36f;
	private static readonly object WorldDiagnosticsLock = new();
	private static readonly HashSet<string> LoggedWorldDiagnostics = new( StringComparer.OrdinalIgnoreCase );

	public static Model BuildWorldModel( GothicClassicMount host, string virtualPath, string resourcePath, bool includeTrees = true )
	{
		var descriptor = host.GetDescriptor( virtualPath );
		dynamic world = host.CreateWorld( virtualPath );
		var mesh = world.Mesh;
		if ( mesh is null )
			throw new InvalidOperationException( $"World '{virtualPath}' did not expose a mesh." );

		LogWorldDiagnosticsOnce( world, mesh, virtualPath );
		var polygonCount = ((IEnumerable)mesh.Polygons).Cast<object>().Count();
		var worldPolygons = GetWorldLeafPolygons( world, polygonCount, virtualPath );
		List<Sandbox.Mesh> renderMeshes = BuildRenderMeshesFromMesh( host, mesh, descriptor, worldPolygons );
		if ( includeTrees ) AddWorldTrees( host, world, renderMeshes, virtualPath );
		if ( renderMeshes.Count == 0 )
			throw new InvalidOperationException( $"Mesh '{resourcePath}' had no polygons." );
		var builder = Model.Builder.WithName( resourcePath );
		builder.AddMeshes( renderMeshes.ToArray() );
		AddWorldCollision( builder, mesh, worldPolygons, virtualPath );
		return builder.Create();
	}

	private static void AddWorldCollision( ModelBuilder builder, object mesh, HashSet<int> worldPolygons, string worldPath )
	{
		var materials = ((IEnumerable)ZenKitRuntime.GetProperty( mesh, "Materials" )).Cast<object>().ToList();
		var positions = ((IEnumerable)ZenKitRuntime.GetProperty( mesh, "Positions" )).Cast<object>()
			.Select( p => (Vector3)ToSandboxPosition( p ) ).ToList();
		var vertices = new List<Vector3>();
		var indices = new List<int>();
		var vertexMap = new Dictionary<int, int>();
		var polygonIndex = 0;
		foreach ( var polygon in (IEnumerable)ZenKitRuntime.GetProperty( mesh, "Polygons" ) )
		{
			var index = polygonIndex++;
			if ( worldPolygons is not null && !worldPolygons.Contains( index ) ) continue;
			if ( ZenKitRuntime.GetProperty<bool>( polygon, "IsPortal" ) ||
				ZenKitRuntime.GetProperty<bool>( polygon, "IsGhostOccluder" ) ) continue;
			var materialIndex = ZenKitRuntime.GetProperty<int>( polygon, "MaterialIndex" );
			if ( materialIndex >= 0 && materialIndex < materials.Count &&
				ZenKitRuntime.GetProperty<bool>( materials[materialIndex], "DisableCollision" ) ) continue;

			// Alpha is a rendering property: invisible barriers can still have collision.
			var corners = ((IEnumerable)ZenKitRuntime.GetProperty( polygon, "PositionIndices" )).Cast<object>()
				.Select( Convert.ToInt32 ).ToArray();
			if ( corners.Length < 3 || corners.Any( i => i < 0 || i >= positions.Count ) ) continue;
			for ( var i = 1; i + 1 < corners.Length; i++ )
			{
				var a = corners[0];
				var b = corners[i];
				var c = corners[i + 1];
				if ( Vector3.Cross( positions[b] - positions[a], positions[c] - positions[a] ).LengthSquared < 0.000001f ) continue;
				// This order gives outward collision normals after the Gothic-to-sbox axis conversion.
				indices.Add( Vertex( a ) );
				indices.Add( Vertex( b ) );
				indices.Add( Vertex( c ) );
			}
		}
		if ( indices.Count > 0 ) builder.AddCollisionMesh( vertices, indices );
		Log.Info( $"Gothic world collision [{worldPath}]: Vertices={vertices.Count} Triangles={indices.Count / 3}" );

		int Vertex( int index )
		{
			if ( vertexMap.TryGetValue( index, out var mapped ) ) return mapped;
			mapped = vertices.Count;
			vertexMap.Add( index, mapped );
			vertices.Add( positions[index] );
			return mapped;
		}
	}

	private static void AddWorldTrees( GothicClassicMount host, object world, List<Sandbox.Mesh> renderMeshes, string worldPath )
	{
		if ( ZenKitRuntime.GetProperty( world, "RootObjects" ) is not IEnumerable roots ) return;
		var modelPaths = host.MountedModelPaths.Where( p => p.EndsWith( ".MRM", StringComparison.OrdinalIgnoreCase ) )
			.GroupBy( p => Path.GetFileNameWithoutExtension( p ), StringComparer.OrdinalIgnoreCase )
			.ToDictionary( g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase );
		var sourceMeshes = new Dictionary<string, object>( StringComparer.OrdinalIgnoreCase );
		var count = 0;
		foreach ( var root in roots ) Visit( root );
		Log.Info( $"Gothic world tree visuals [{worldPath}]: Imported={count}" );

		void Visit( object vob )
		{
			var visual = ReadVisualPath( vob );
			if ( ZenKitRuntime.GetProperty<bool>( vob, "ShowVisual" ) &&
				!string.IsNullOrWhiteSpace( visual ) &&
				Path.GetFileNameWithoutExtension( visual ).Contains( "TREE", StringComparison.OrdinalIgnoreCase ) &&
				modelPaths.TryGetValue( Path.GetFileNameWithoutExtension( visual ), out var modelPath ) )
			{
				if ( !sourceMeshes.TryGetValue( modelPath, out var sourceMesh ) )
				{
					sourceMesh = ZenKitRuntime.CreateMultiResolutionMesh( host.RequireVfs(), modelPath );
					sourceMeshes.Add( modelPath, sourceMesh );
				}
				var rotation = ZenKitRuntime.GetProperty( vob, "Rotation" );
				Vector3 translation = ToSandboxPosition( ZenKitRuntime.GetProperty( vob, "Position" ) );
				float M( string field ) => ZenKitRuntime.GetFieldValue<float>( rotation, field );
				var row1 = new Vector3( M( "M11" ), M( "M12" ), M( "M13" ) );
				var row2 = new Vector3( M( "M21" ), M( "M22" ), M( "M23" ) );
				var row3 = new Vector3( M( "M31" ), M( "M32" ), M( "M33" ) );
				Vector3 TransformPoint( Vector3 position )
				{
					var gothic = new Vector3( position.x, position.z, position.y );
					return new Vector3( Vector3.Dot( row1, gothic ), Vector3.Dot( row3, gothic ), Vector3.Dot( row2, gothic ) ) + translation;
				}
				var materialIndex = 0;
				renderMeshes.AddRange( BuildRenderMeshes( host, sourceMesh, host.GetDescriptor( modelPath ), ref materialIndex, TransformPoint ) );
				count++;
			}
			if ( ZenKitRuntime.GetProperty( vob, "Children" ) is IEnumerable children )
				foreach ( var child in children ) Visit( child );
		}
	}

	private static HashSet<int> GetWorldLeafPolygons( object world, int polygonCount, string virtualPath )
	{
		var bsp = ZenKitRuntime.GetProperty( world, "BspTree" );
		if ( ZenKitRuntime.GetProperty( bsp, "LeafPolygonIndices" ) is not IEnumerable leafIndices )
		{
			Log.Warning( $"Gothic world '{virtualPath}' has no BSP leaf references; importing all polygons." );
			return null;
		}

		// These are mesh polygon indices, already resolved from the BSP leaf ranges.
		// Internal BSP nodes also reference coarse LOD geometry; do not include them.
		var selected = new HashSet<int>();
		foreach ( var rawIndex in leafIndices )
		{
			var index = Convert.ToInt64( rawIndex );
			if ( index < 0 || index >= polygonCount )
			{
				Log.Warning( $"Gothic world '{virtualPath}' has an invalid BSP polygon index {index}; importing all polygons." );
				return null;
			}
			selected.Add( (int)index );
		}

		if ( selected.Count == 0 )
		{
			Log.Warning( $"Gothic world '{virtualPath}' has no BSP leaf polygons; importing all polygons." );
			return null;
		}

		Log.Info( $"Gothic world BSP filter [{virtualPath}]: Total={polygonCount} Retained={selected.Count} Skipped={polygonCount - selected.Count}" );
		return selected;
	}

	public static Model BuildModel( GothicClassicMount host, string virtualPath, string resourcePath )
	{
		var descriptor = host.GetDescriptor( virtualPath );
		var extension = Path.GetExtension( virtualPath );
		if ( extension.Equals( ".MSH", StringComparison.OrdinalIgnoreCase ) )
			return BuildModelFromMesh( host, resourcePath, ZenKitRuntime.CreateMesh( host.RequireVfs(), virtualPath ), descriptor );

		if ( extension.Equals( ".MRM", StringComparison.OrdinalIgnoreCase ) )
			return BuildModelFromMultiResolutionMesh( host, resourcePath, ZenKitRuntime.CreateMultiResolutionMesh( host.RequireVfs(), virtualPath ), descriptor );

		if ( extension.Equals( ".MDM", StringComparison.OrdinalIgnoreCase ) )
			return BuildModelFromModelMesh( host, resourcePath, ZenKitRuntime.CreateModelMesh( host.RequireVfs(), virtualPath ), descriptor );

		if ( extension.Equals( ".MDL", StringComparison.OrdinalIgnoreCase ) )
		{
			dynamic model = ZenKitRuntime.CreateModel( host.RequireVfs(), virtualPath );
			return BuildModelFromModelMesh( host, resourcePath, model.Mesh, descriptor );
		}

		throw new NotSupportedException( $"Unsupported Gothic model extension '{extension}'." );
	}

	private static Model BuildModelFromModelMesh( GothicClassicMount host, string name, dynamic modelMesh, GothicClassicMount.GothicAssetDescriptor descriptor )
	{
		var meshes = new List<Sandbox.Mesh>();
		var materialIndex = 0;

		foreach ( var softSkinMesh in (IEnumerable)modelMesh.Meshes )
		{
			var softMesh = ZenKitRuntime.GetProperty( softSkinMesh, "Mesh" );
			if ( softMesh is not null )
			{
				meshes.AddRange( BuildRenderMeshes( host, softMesh, descriptor, ref materialIndex ) );
			}
		}

		if ( meshes.Count == 0 )
		{
			foreach ( DictionaryEntry attachment in (IDictionary)modelMesh.Attachments )
			{
				if ( attachment.Value is not null )
				{
					meshes.AddRange( BuildRenderMeshes( host, attachment.Value, descriptor, ref materialIndex ) );
				}
			}
		}

		if ( meshes.Count == 0 )
			throw new InvalidOperationException( $"Model '{name}' did not contain any renderable meshes." );

		var builder = Model.Builder.WithName( name );
		builder.AddMeshes( meshes.ToArray() );
		return builder.Create();
	}

	private static Model BuildModelFromMultiResolutionMesh( GothicClassicMount host, string name, object mesh, GothicClassicMount.GothicAssetDescriptor descriptor )
	{
		var materialIndex = 0;
		var meshes = BuildRenderMeshes( host, mesh, descriptor, ref materialIndex );
		if ( meshes.Count == 0 )
			throw new InvalidOperationException( $"Multi-resolution mesh '{name}' had no submeshes." );

		var builder = Model.Builder.WithName( name );
		builder.AddMeshes( meshes.ToArray() );
		AddModelCollision( builder, mesh );
		return builder.Create();
	}

	private static void AddModelCollision( ModelBuilder builder, object mesh )
	{
		var vertices = new List<Vector3>();
		var indices = new List<int>();
		var vertexMap = new Dictionary<Vector3, int>();
		foreach ( var subMesh in (IEnumerable)ZenKitRuntime.GetProperty( mesh, "SubMeshes" ) )
		{
			if ( ZenKitRuntime.GetProperty<bool>( ZenKitRuntime.GetProperty( subMesh, "Material" ), "DisableCollision" ) ) continue;
			foreach ( var triangle in (IEnumerable)ZenKitRuntime.GetProperty( subMesh, "Triangles" ) )
			{
				var a = CreateTriangleVertex( mesh, subMesh, ZenKitRuntime.GetFieldValue<ushort>( triangle, "Wedge0" ) ).position;
				var b = CreateTriangleVertex( mesh, subMesh, ZenKitRuntime.GetFieldValue<ushort>( triangle, "Wedge1" ) ).position;
				var c = CreateTriangleVertex( mesh, subMesh, ZenKitRuntime.GetFieldValue<ushort>( triangle, "Wedge2" ) ).position;
				if ( Vector3.Cross( b - a, c - a ).LengthSquared < 0.000001f ) continue;
				indices.Add( Vertex( a ) );
				indices.Add( Vertex( b ) );
				indices.Add( Vertex( c ) );
			}
		}
		if ( indices.Count > 0 ) builder.AddCollisionMesh( vertices, indices );

		int Vertex( Vector3 position )
		{
			if ( vertexMap.TryGetValue( position, out var index ) ) return index;
			index = vertices.Count;
			vertexMap.Add( position, index );
			vertices.Add( position );
			return index;
		}
	}

	private static Model BuildModelFromMesh( GothicClassicMount host, string name, dynamic mesh, GothicClassicMount.GothicAssetDescriptor descriptor, HashSet<int> worldPolygons = null )
	{
		var meshes = BuildRenderMeshesFromMesh( host, mesh, descriptor, worldPolygons );
		if ( meshes.Count == 0 )
			throw new InvalidOperationException( $"Mesh '{name}' had no polygons." );

		var builder = Model.Builder.WithName( name );
		builder.AddMeshes( meshes.ToArray() );
		AddWorldCollision( builder, mesh, worldPolygons, name );
		return builder.Create();
	}

	private static List<Sandbox.Mesh> BuildRenderMeshes( GothicClassicMount host, object meshObject, GothicClassicMount.GothicAssetDescriptor descriptor, ref int materialIndex, Func<Vector3, Vector3> transformPoint = null )
	{
		dynamic mesh = meshObject;
		var result = new List<Sandbox.Mesh>();

		foreach ( var subMesh in (IEnumerable)mesh.SubMeshes )
		{
			var triangles = (IEnumerable)ZenKitRuntime.GetProperty( subMesh, "Triangles" );
			var vertices = new List<GothicVertex>();
			var indices = new List<int>();

			foreach ( var triangle in triangles )
			{
				AddTriangle( mesh, subMesh,
					(ushort)ZenKitRuntime.GetFieldValue<ushort>( triangle, "Wedge0" ),
					(ushort)ZenKitRuntime.GetFieldValue<ushort>( triangle, "Wedge1" ),
					(ushort)ZenKitRuntime.GetFieldValue<ushort>( triangle, "Wedge2" ),
					vertices, indices );
			}

			if ( vertices.Count == 0 )
				continue;

			if ( transformPoint is not null )
			{
				for ( var i = 0; i < vertices.Count; i++ )
				{
					var vertex = vertices[i];
					vertex.position = transformPoint( vertex.position );
					vertices[i] = vertex;
				}
			}

			SmoothNormals( vertices, indices );

			result.Add( CreateSandboxMesh( host.LoadMaterial( ResolveMaterialDescriptor( descriptor, materialIndex ) ), vertices, indices ) );
			materialIndex++;
		}

		return result;
	}

	private static List<Sandbox.Mesh> BuildRenderMeshesFromMesh( GothicClassicMount host, dynamic mesh, GothicClassicMount.GothicAssetDescriptor descriptor, HashSet<int> worldPolygons )
	{
		var grouped = new Dictionary<int, (List<GothicVertex> Vertices, List<int> Indices)>();
		var materials = ((IEnumerable)mesh.Materials).Cast<object>().ToList();
		var helperMaterials = new HashSet<int>();
		if ( descriptor?.IsWorld == true )
		{
			for ( var i = 0; i < materials.Count; i++ )
			{
				var materialName = ZenKitRuntime.GetProperty<string>( materials[i], "Name" )?.Trim() ?? "";
				if ( materialName.Equals( "GHOSTOCCLUDER", StringComparison.OrdinalIgnoreCase ) ||
					materialName.Equals( "Z_PORTALMAT", StringComparison.OrdinalIgnoreCase ) ||
					materialName.StartsWith( "P:", StringComparison.OrdinalIgnoreCase ) )
				{
					helperMaterials.Add( i );
				}
			}
		}
		var skippedHelpers = 0;
		var polygonIndex = 0;
		foreach ( var polygon in (IEnumerable)mesh.Polygons )
		{
			var currentIndex = polygonIndex++;
			if ( worldPolygons is not null && !worldPolygons.Contains( currentIndex ) )
				continue;

			// Explicit visibility helper materials are separate from alpha-textured surfaces.
			var materialIndex = ZenKitRuntime.GetProperty<int>( polygon, "MaterialIndex" );
			if ( helperMaterials.Contains( materialIndex ) )
			{
				skippedHelpers++;
				continue;
			}

			var positionIndices = ((IEnumerable)ZenKitRuntime.GetProperty( polygon, "PositionIndices" )).Cast<object>().ToList();
			var featureIndices = ((IEnumerable)ZenKitRuntime.GetProperty( polygon, "FeatureIndices" )).Cast<object>().ToList();
			if ( positionIndices.Count < 3 || featureIndices.Count < 3 )
			{
				continue;
			}

			if ( !grouped.TryGetValue( materialIndex, out var buffers ) )
			{
				buffers = (new List<GothicVertex>(), new List<int>());
				grouped[materialIndex] = buffers;
			}

			for ( var i = 1; i < positionIndices.Count - 1; i++ )
			{
				AddPolygonTriangle( mesh, polygon, 0, i, i + 1, buffers.Vertices, buffers.Indices );
			}

		}

		if ( skippedHelpers > 0 )
			Log.Info( $"Gothic world visibility helpers skipped [{descriptor.VirtualPath}]: {skippedHelpers}" );

		var meshes = new List<Sandbox.Mesh>();
		foreach ( var pair in grouped.OrderBy( x => x.Key ) )
		{
			if ( pair.Value.Vertices.Count == 0 )
				continue;

			SmoothNormals( pair.Value.Vertices, pair.Value.Indices );

			meshes.Add( CreateSandboxMesh( host.LoadMaterial( ResolveMaterialDescriptor( descriptor, pair.Key ) ), pair.Value.Vertices, pair.Value.Indices ) );
		}

		return meshes;
	}

	private static List<WorldMeshComponent> BuildWorldMeshComponents( dynamic mesh )
	{
		var polygons = ((IEnumerable)mesh.Polygons).Cast<object>().ToList();
		if ( polygons.Count == 0 )
			return [];

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

		var components = new Dictionary<int, WorldMeshComponent>();
		for ( var polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++ )
		{
			var root = Find( polygonIndex );
			if ( !components.TryGetValue( root, out var component ) )
			{
				component = new WorldMeshComponent();
				components[root] = component;
			}

			component.PolygonIndices.Add( polygonIndex );
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

		return components.Values
			.Where( x => x.Points.Count > 0 )
			.Select( x =>
			{
				x.Bounds = BBox.FromPoints( x.Points, 0f );
				return x;
			} )
			.OrderByDescending( x => x.PolygonCount )
			.ToList();

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

	private static void LogWorldDiagnosticsOnce( object world, dynamic mesh, string virtualPath )
	{
		lock ( WorldDiagnosticsLock )
		{
			if ( !LoggedWorldDiagnostics.Add( virtualPath ) )
				return;
		}

		try
		{
			List<WorldMeshComponent> components = BuildWorldMeshComponents( mesh );
			var polygonCount = ((IEnumerable)ZenKitRuntime.GetProperty( mesh, "Polygons" )).Cast<object>().Count();
			Log.Info( $"Gothic world import diagnostics [{virtualPath}] MeshPolygons={polygonCount} Components={components.Count}" );

			if ( components.Count > 1 )
			{
				var primary = components[0];
				for ( var componentIndex = 1; componentIndex < components.Count && componentIndex <= 12; componentIndex++ )
				{
					var component = components[componentIndex];
					var contained = IsContainedWithin( component.Bounds, primary.Bounds, 8f );
					var overlap = BoundsOverlapRatio( component.Bounds, primary.Bounds );
					var polygonRatio = component.PolygonCount / (float)Math.Max( primary.PolygonCount, 1 );
					if ( !contained && overlap < 0.60f )
						continue;

					var orderedMaterialIndices = component.MaterialIndices.ToList();
					orderedMaterialIndices.Sort();
					var materialSummary = string.Join( ",", orderedMaterialIndices.Take( 6 ) );
					Log.Info( $"Gothic world component candidate [{virtualPath}] Polygons={component.PolygonCount} PolygonRatio={polygonRatio:0.000} Overlap={overlap:0.000} Contained={contained} Materials=[{materialSummary}] Bounds={component.Bounds}" );
				}
			}

			LogWorldVobDiagnostics( world, virtualPath );
		}
		catch ( Exception exception )
		{
			Log.Warning( $"Gothic world import diagnostics failed for '{virtualPath}': {exception.Message}" );
		}
	}

	private static void LogWorldVobDiagnostics( object world, string virtualPath )
	{
		var visualUsage = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		var flaggedVobs = new List<string>();
		var totalVobs = 0;

		var rootVob = ZenKitRuntime.GetProperty( world, "RootVob" ) ?? ZenKitRuntime.GetProperty( world, "Root" );
		if ( rootVob is not null )
		{
			TraverseVobs( rootVob, 0 );
		}
		else if ( (ZenKitRuntime.GetProperty( world, "RootObjects" ) ?? ZenKitRuntime.GetProperty( world, "Vobs" )) is IEnumerable directVobs )
		{
			foreach ( var vob in directVobs )
				TraverseVobs( vob, 0 );
		}

		Log.Info( $"Gothic world vob diagnostics [{virtualPath}] TotalVobs={totalVobs} UniqueVisuals={visualUsage.Count}" );
		foreach ( var pair in visualUsage
			.Where( x => x.Value > 1 || LooksLikeLodVisual( x.Key ) )
			.OrderByDescending( x => x.Value )
			.ThenBy( x => x.Key, StringComparer.OrdinalIgnoreCase )
			.Take( 20 ) )
		{
			Log.Info( $"Gothic world visual usage [{virtualPath}] Visual={pair.Key} Count={pair.Value}" );
		}

		foreach ( var line in flaggedVobs.Take( 30 ) )
			Log.Info( line );

		void TraverseVobs( object vob, int depth )
		{
			if ( vob is null || depth > 64 )
				return;

			totalVobs++;

			var visual = ReadVisualPath( vob );
			if ( !string.IsNullOrWhiteSpace( visual ) )
				visualUsage[visual] = visualUsage.TryGetValue( visual, out var count ) ? count + 1 : 1;

			if ( !string.IsNullOrWhiteSpace( visual ) && LooksLikeLodVisual( visual ) )
			{
				var vobName = ReadStringMember( vob, "PresetName" )
					?? ReadStringMember( vob, "VobName" )
					?? ReadStringMember( vob, "Name" )
					?? "<unnamed>";
				flaggedVobs.Add( $"Gothic world LOD-like vob [{virtualPath}] Depth={depth} Name={vobName} Visual={visual}" );
			}

			if ( ZenKitRuntime.GetProperty( vob, "Children" ) is not IEnumerable children )
				return;

			foreach ( var child in children )
				TraverseVobs( child, depth + 1 );
		}
	}

	private static bool LooksLikeLodVisual( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
			return false;

		return value.Contains( "LOD", StringComparison.OrdinalIgnoreCase )
			|| value.Contains( "LOW", StringComparison.OrdinalIgnoreCase )
			|| value.Contains( "FAR", StringComparison.OrdinalIgnoreCase );
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
		}

		return null;
	}

	private static string ReadStringMember( object instance, string memberName )
	{
		var value = ZenKitRuntime.GetProperty( instance, memberName ) ?? ZenKitRuntime.GetField( instance, memberName );
		return value as string;
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

	private static GothicClassicMount.GothicMaterialDescriptor ResolveMaterialDescriptor( GothicClassicMount.GothicAssetDescriptor descriptor, int index )
	{
		if ( descriptor?.Materials is null || index < 0 || index >= descriptor.Materials.Count )
		{
			return new GothicClassicMount.GothicMaterialDescriptor
			{
				MaterialName = $"fallback_{index}",
				TexturePath = null,
				CacheKey = $"fallback_{index}",
				DebugSource = "descriptor_missing"
			};
		}

		return descriptor.Materials[index];
	}

	private static void AddTriangle( dynamic mesh, dynamic subMesh, ushort wedge0, ushort wedge1, ushort wedge2, List<GothicVertex> vertices, List<int> indices )
	{
		var v0 = CreateTriangleVertex( mesh, subMesh, wedge0 );
		var v1 = CreateTriangleVertex( mesh, subMesh, wedge1 );
		var v2 = CreateTriangleVertex( mesh, subMesh, wedge2 );
		indices.Add( vertices.Count );
		vertices.Add( v0 );
		indices.Add( vertices.Count );
		vertices.Add( v1 );
		indices.Add( vertices.Count );
		vertices.Add( v2 );
	}

	private static void AddPolygonTriangle( dynamic mesh, dynamic polygon, int index0, int index1, int index2, List<GothicVertex> vertices, List<int> indices )
	{
		var v0 = CreatePolygonVertex( mesh, polygon, index0 );
		var v1 = CreatePolygonVertex( mesh, polygon, index1 );
		var v2 = CreatePolygonVertex( mesh, polygon, index2 );
		indices.Add( vertices.Count );
		vertices.Add( v0 );
		indices.Add( vertices.Count );
		vertices.Add( v1 );
		indices.Add( vertices.Count );
		vertices.Add( v2 );
	}

	private static Sandbox.Mesh CreateSandboxMesh( Material material, List<GothicVertex> vertices, List<int> indices )
	{
		var mesh = new Sandbox.Mesh( material );
		mesh.CreateVertexBuffer( vertices.Count, vertices );
		mesh.CreateIndexBuffer( indices.Count, indices );
		mesh.SetIndexRange( 0, indices.Count );
		mesh.Bounds = BBox.FromPoints( vertices.Select( x => x.position ), 0f );
		return mesh;
	}

	private static Vector3 ToSandboxPosition( dynamic vector )
	{
		var transformed = new Vector3( (float)vector.X, (float)vector.Z, (float)vector.Y );
		return transformed * GothicScale;
	}

	private static Vector3 ToSandboxRotation( dynamic vector )
	{
		return new Vector3( (float)vector.X, (float)vector.Y, (float)vector.Z );
	}

	private static GothicVertex CreateTriangleVertex( dynamic mesh, dynamic subMesh, ushort wedgeIndex )
	{
		dynamic wedge = subMesh.GetWedge( wedgeIndex );
		dynamic position = mesh.GetPosition( (ushort)ZenKitRuntime.GetFieldValue<ushort>( wedge, "Index" ) );
		dynamic texture = ZenKitRuntime.GetField( wedge, "Texture" );

		return new GothicVertex
		{
			position = ToSandboxPosition( position ),
			normal = Vector3.Zero,
			texcoord = new Vector2( (float)texture.X, (float)texture.Y )
		};
	}

	private static GothicVertex CreatePolygonVertex( dynamic mesh, dynamic polygon, int localIndex )
	{
		var positionIndices = ((IEnumerable)ZenKitRuntime.GetProperty( polygon, "PositionIndices" )).Cast<object>().ToList();
		var featureIndices = ((IEnumerable)ZenKitRuntime.GetProperty( polygon, "FeatureIndices" )).Cast<object>().ToList();
		dynamic position = mesh.GetPosition( Convert.ToInt32( positionIndices[localIndex] ) );
		dynamic feature = mesh.GetFeature( Convert.ToInt32( featureIndices[localIndex] ) );
		dynamic texture = ZenKitRuntime.GetField( feature, "Texture" );

		return new GothicVertex
		{
			position = ToSandboxPosition( position ),
			normal = Vector3.Zero,
			texcoord = new Vector2( (float)texture.X, (float)texture.Y )
		};
	}

	private static void SmoothNormals( List<GothicVertex> vertices, List<int> indices )
	{
		var accum = new Dictionary<(Vector3 Position, Vector2 Texcoord), Vector3>();

		for ( var i = 0; i + 2 < indices.Count; i += 3 )
		{
			var i0 = indices[i];
			var i1 = indices[i + 1];
			var i2 = indices[i + 2];

			var v0 = vertices[i0];
			var v1 = vertices[i1];
			var v2 = vertices[i2];
			var faceNormal = Vector3.Cross( v1.position - v0.position, v2.position - v0.position ).Normal;

			Accumulate( v0, faceNormal );
			Accumulate( v1, faceNormal );
			Accumulate( v2, faceNormal );
		}

		for ( var i = 0; i < vertices.Count; i++ )
		{
			var vertex = vertices[i];
			if ( accum.TryGetValue( MakeNormalKey( vertex ), out var normal ) && normal.Length > 0.0001f )
			{
				vertex.normal = normal.Normal;
				vertices[i] = vertex;
			}
		}

		void Accumulate( GothicVertex vertex, Vector3 normal )
		{
			var key = MakeNormalKey( vertex );
			accum[key] = accum.TryGetValue( key, out var current ) ? current + normal : normal;
		}
	}

	private static (Vector3 Position, Vector2 Texcoord) MakeNormalKey( GothicVertex vertex )
	{
		return (Quantize( vertex.position ), Quantize( vertex.texcoord ));
	}

	private static Vector3 Quantize( Vector3 value )
	{
		return new Vector3(
			MathF.Round( value.x, 4 ),
			MathF.Round( value.y, 4 ),
			MathF.Round( value.z, 4 ) );
	}

	private static Vector2 Quantize( Vector2 value )
	{
		return new Vector2(
			MathF.Round( value.x, 4 ),
			MathF.Round( value.y, 4 ) );
	}

	private sealed class WorldMeshComponent
	{
		public int PolygonCount { get; set; }
		public List<int> PolygonIndices { get; } = new();
		public HashSet<int> MaterialIndices { get; } = new();
		public List<Vector3> Points { get; } = new();
		public BBox Bounds { get; set; }
	}

}
