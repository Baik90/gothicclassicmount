using System.Reflection;
using ZenKit;

if ( args.Contains("--characters") || args.Contains("--animations") )
{
	var option=Array.IndexOf(args,"--install");
	var install=option>=0 && option+1<args.Length ? args[option+1] : @"D:\Steam\steamapps\common\Gothic";
	if(args.Contains("--characters")) Characters.Inspect(install); else AnimationInspect.Inspect(install);
	return;
}
if ( args.Contains("--animation-api") )
{
	foreach(var t in typeof(Vfs).Assembly.GetTypes().Where(t=>t.IsPublic && (t.Name.Contains("Animation") || t.Name.Contains("Sample"))))
	{
		Console.WriteLine(t.FullName);
		foreach(var m in t.GetMembers(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)) Console.WriteLine("  "+m);
	}
	return;
}

if ( args.Contains( "--api" ) )
{
	foreach ( var t in typeof(Vfs).Assembly.GetTypes().Where( t => t.IsPublic && (t.Name.Contains("Daedalus") || t.Name.Contains("NpcInstance") || t.Name.Contains("ItemInstance") || t.Name.Contains("ModelHierarchy") || t.Name.Contains("SoftSkin") || t.Name.Contains("ModelNode") || t.Name.Contains("MorphMesh") || t.Name.Contains("ModelScript")) ) )
	{
		Console.WriteLine(t.FullName);
		foreach(var m in t.GetMembers(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)) Console.WriteLine("  " + m);
	}
	return;
}

var dataPath = args.Length > 0 ? args[0] : @"D:\Steam\steamapps\common\Gothic\Data";
var worldPath = args.Length > 1 ? args[1] : "_WORK/DATA/WORLDS/WORLD.ZEN";

var vfs = new Vfs();
foreach ( var vdf in Directory.EnumerateFiles( dataPath, "*.VDF" ) )
{
	vfs.MountDisk( vdf, VfsOverwriteBehavior.Older );
}

var node = vfs.Resolve( worldPath ) ?? throw new InvalidOperationException( $"{worldPath} not resolved" );
var world = new World( node.Buffer );

void DumpTree( object vob )
{
	if ( ReadVisualPath( vob )?.Contains( "OW_FOREST_TREE_V2", StringComparison.OrdinalIgnoreCase ) == true ) { DumpType( "Tree VOB", vob ); DumpType( "Rotation", ReadObjectMember( vob, "Rotation" ) ); return; }
	if ( ReadEnumerableMember( vob, "Children" ) is System.Collections.IEnumerable children )
		foreach ( var child in children ) DumpTree( child );
}
if ( args.Contains( "--trees" ) ) { foreach ( var root in world.RootObjects ) DumpTree( root ); return; }

Console.WriteLine( $"World type: {world.GetType().FullName}" );

if ( world.Mesh is not null )
{
	Console.WriteLine( $"Mesh polygons: {world.Mesh.Polygons.Count}" );
	Console.WriteLine( $"Mesh materials: {world.Mesh.Materials.Count}" );
	Console.WriteLine( SummarizePolygonFlags( world.Mesh ) );
	Console.WriteLine( SummarizeBspPolygonUsage( world.Mesh, world.BspTree ) );
	var materials = world.Mesh.Materials;
	foreach ( var material in materials.Where( m => m.Name == "BLINDALPHA" ) ) DumpType( "Blind alpha", material );
	var leaves = world.BspTree.LeafPolygonIndices.Select( Convert.ToInt32 ).ToHashSet();
	var polygons = world.Mesh.Polygons;
	if ( args.Length >= 8 )
	{
		float Arg( int i ) => float.Parse( args[i], System.Globalization.CultureInfo.InvariantCulture );
		var origin = new System.Numerics.Vector3( Arg( 2 ), Arg( 3 ), Arg( 4 ) );
		var direction = System.Numerics.Vector3.Normalize( new( Arg( 5 ), Arg( 6 ), Arg( 7 ) ) );
		var positions = world.Mesh.Positions;
		var hits = new List<(float Distance, int Index)>();
		for ( var pi = 0; pi < polygons.Count; pi++ )
		{
			var indices = polygons[pi].PositionIndices;
			for ( var ti = 1; ti + 1 < indices.Count; ti++ )
			{
				var a = positions[indices[0]];
				var e1 = positions[indices[ti]] - a;
				var e2 = positions[indices[ti + 1]] - a;
				var h = System.Numerics.Vector3.Cross( direction, e2 );
				var det = System.Numerics.Vector3.Dot( e1, h );
				if ( MathF.Abs( det ) < 0.000001f ) continue;
				var s = origin - a;
				var u = System.Numerics.Vector3.Dot( s, h ) / det;
				var q = System.Numerics.Vector3.Cross( s, e1 );
				var v = System.Numerics.Vector3.Dot( direction, q ) / det;
				var distance = System.Numerics.Vector3.Dot( e2, q ) / det;
				if ( u >= 0 && v >= 0 && u + v <= 1 && distance > 0 ) hits.Add( (distance, pi) );
			}
		}
		foreach ( var hit in hits.OrderBy( h => h.Distance ).Take( 16 ) )
		{
			var p = polygons[hit.Index];
			var m = materials[p.MaterialIndex];
			Console.WriteLine( $"Probe polygon={hit.Index} distance={hit.Distance} leaf={leaves.Contains( hit.Index )} material={m.Name} texture={m.Texture} portal={p.IsPortal} ghost={p.IsGhostOccluder}" );
		}
	}
	foreach ( var group in polygons.Where( p => p.IsOutdoor ).GroupBy( p => p.MaterialIndex ) )
		Console.WriteLine( $"Outdoor material {materials[group.Key].Name} Texture={materials[group.Key].Texture} Count={group.Count()}" );
	foreach ( var group in polygons.Select( (p, i) => (Polygon: p, Index: i) ).GroupBy( p => p.Polygon.MaterialIndex ).OrderByDescending( g => g.Count( p => !leaves.Contains( p.Index ) ) ).Take( 35 ) )
		Console.WriteLine( $"BSP material {materials[group.Key].Name} Texture={materials[group.Key].Texture} Kept={group.Count( p => leaves.Contains( p.Index ) )} Removed={group.Count( p => !leaves.Contains( p.Index ) )}" );
	var helperCount = 0;
	for ( var i = 0; i < polygons.Count; i++ )
	{
		if ( !leaves.Contains( i ) ) continue;
		var materialName = materials[polygons[i].MaterialIndex].Name.Trim();
		if ( materialName.Equals( "GHOSTOCCLUDER", StringComparison.OrdinalIgnoreCase ) ||
			materialName.Equals( "Z_PORTALMAT", StringComparison.OrdinalIgnoreCase ) ||
			materialName.StartsWith( "P:", StringComparison.OrdinalIgnoreCase ) ) helperCount++;
	}
	Console.WriteLine( $"BSP leaf helper materials: Removed={helperCount} Retained={leaves.Count - helperCount}" );
	foreach ( var material in materials.Where( m => !m.Name.StartsWith( "P:" ) ).Where( m => string.IsNullOrWhiteSpace( m.Texture ) || m.Name.Contains( "GHOST", StringComparison.OrdinalIgnoreCase ) ) )
		DumpType( "Untextured material", material );
	foreach ( var group in world.Mesh.Polygons.Where( p => p.IsPortal || p.IsGhostOccluder ).GroupBy( p => p.MaterialIndex ).OrderByDescending( g => g.Count() ).Take( 15 ) )
		Console.WriteLine( $"Helper material #{group.Key}: {materials[group.Key].Name} Portal={group.Count( p => p.IsPortal )} GhostOccluder={group.Count( p => p.IsGhostOccluder )}" );
}

Console.WriteLine( SummarizeWorldVisuals( world ) );

static void DumpType( string label, object instance )
{
	var type = instance.GetType();
	Console.WriteLine( $"{label}: {type.FullName}" );

	foreach ( var property in type.GetProperties( BindingFlags.Public | BindingFlags.Instance ) )
	{
		if ( property.GetIndexParameters().Length > 0 )
			continue;

		object value;
		try
		{
			value = property.GetValue( instance );
		}
		catch ( Exception exception )
		{
			value = $"<error:{exception.GetType().Name}>";
		}

		Console.WriteLine( $"  P {property.PropertyType.FullName} {property.Name} = {DescribeValue( value )}" );
	}

	foreach ( var field in type.GetFields( BindingFlags.Public | BindingFlags.Instance ) )
	{
		object value;
		try
		{
			value = field.GetValue( instance );
		}
		catch ( Exception exception )
		{
			value = $"<error:{exception.GetType().Name}>";
		}

		Console.WriteLine( $"  F {field.FieldType.FullName} {field.Name} = {DescribeValue( value )}" );
	}
}

static void DumpNamedCollectionSample( object instance, string propertyName )
{
	var property = instance.GetType().GetProperty( propertyName, BindingFlags.Public | BindingFlags.Instance );
	if ( property is null )
	{
		Console.WriteLine( $"{propertyName}: <missing>" );
		return;
	}

	var value = property.GetValue( instance );
	Console.WriteLine( $"{propertyName}: {DescribeValue( value )}" );

	if ( value is not System.Collections.IEnumerable enumerable || value is string )
		return;

	foreach ( var item in enumerable )
	{
		if ( item is null )
			continue;

		DumpType( propertyName.TrimEnd( 's' ), item );
		break;
	}
}

static string DescribeValue( object value )
{
	if ( value is null )
		return "<null>";

	if ( value is string text )
		return text;

	if ( value is System.Collections.IEnumerable enumerable )
	{
		var items = new List<string>();
		var count = 0;
		foreach ( var item in enumerable )
		{
			count++;
			if ( items.Count < 5 )
				items.Add( item?.ToString() ?? "<null>" );
		}

		return $"<{value.GetType().FullName}> Count={count} Sample=[{string.Join( ", ", items )}]";
	}

	return value.ToString() ?? value.GetType().FullName;
}

static string SummarizePolygonFlags( IMesh mesh )
{
	var counts = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
	foreach ( var polygon in mesh.Polygons )
	{
		AddIf( "IsLod", polygon.IsLod );
		AddIf( "IsPortal", polygon.IsPortal );
		AddIf( "IsOccluder", polygon.IsOccluder );
		AddIf( "IsSector", polygon.IsSector );
		AddIf( "IsOutdoor", polygon.IsOutdoor );
		AddIf( "IsGhostOccluder", polygon.IsGhostOccluder );
		AddIf( "IsDynamicallyLit", polygon.IsDynamicallyLit );
		AddIf( "ShouldRelight", polygon.ShouldRelight );
	}

	return string.Join( ", ", counts.OrderByDescending( x => x.Value ).Select( x => $"{x.Key}={x.Value}" ) );

	void AddIf( string name, bool value )
	{
		if ( !value )
			return;

		counts[name] = counts.TryGetValue( name, out var count ) ? count + 1 : 1;
	}
}

static string SummarizeBspPolygonUsage( IMesh mesh, IBspTree bsp )
{
	var polygonCount = mesh.PolygonCount;
	var polygons = mesh.Polygons;
	var inTree = new bool[polygonCount];
	var inLeaf = new bool[polygonCount];

	foreach ( var index in bsp.PolygonIndices )
	{
		if ( (uint)index < polygonCount )
			inTree[index] = true;
	}

	foreach ( var index in bsp.LeafPolygonIndices )
	{
		if ( (uint)index < polygonCount )
			inLeaf[index] = true;
	}

	var treeUniqueCount = 0;
	var leafUniqueCount = 0;
	var treeOnly = 0;
	var leafOnly = 0;
	var missingFromTree = 0;
	var missingFromLeaf = 0;
	var missingLeafLod = 0;
	var missingLeafPortal = 0;
	var missingLeafOccluder = 0;
	var missingLeafSector = 0;
	var treeOnlyLod = 0;
	var treeOnlyPortal = 0;
	var treeOnlyOccluder = 0;
	var treeOnlySector = 0;

	for ( var i = 0; i < polygonCount; i++ )
	{
		var tree = inTree[i];
		var leaf = inLeaf[i];
		if ( tree ) treeUniqueCount++;
		if ( leaf ) leafUniqueCount++;
		if ( tree && !leaf )
		{
			treeOnly++;
			var polygon = polygons[i];
			if ( polygon.IsLod ) treeOnlyLod++;
			if ( polygon.IsPortal ) treeOnlyPortal++;
			if ( polygon.IsOccluder ) treeOnlyOccluder++;
			if ( polygon.IsSector ) treeOnlySector++;
		}
		if ( leaf && !tree ) leafOnly++;
		if ( !tree ) missingFromTree++;
		if ( !leaf )
		{
			missingFromLeaf++;
			var polygon = polygons[i];
			if ( polygon.IsLod ) missingLeafLod++;
			if ( polygon.IsPortal ) missingLeafPortal++;
			if ( polygon.IsOccluder ) missingLeafOccluder++;
			if ( polygon.IsSector ) missingLeafSector++;
		}
	}

	return $"BSP polygon usage: TreeUnique={treeUniqueCount} LeafUnique={leafUniqueCount} TreeOnly={treeOnly} LeafOnly={leafOnly} MissingFromTree={missingFromTree} MissingFromLeaf={missingFromLeaf} MissingLeafFlags[Lod={missingLeafLod}, Portal={missingLeafPortal}, Occluder={missingLeafOccluder}, Sector={missingLeafSector}] TreeOnlyFlags[Lod={treeOnlyLod}, Portal={treeOnlyPortal}, Occluder={treeOnlyOccluder}, Sector={treeOnlySector}]";
}

static string SummarizePolygonMaterials( IMesh mesh )
{
	var totals = new Dictionary<int, int>();
	var lodTotals = new Dictionary<int, int>();
	var portalTotals = new Dictionary<int, int>();
	var occluderTotals = new Dictionary<int, int>();

	foreach ( var polygon in mesh.Polygons )
	{
		var index = polygon.MaterialIndex;
		totals[index] = totals.TryGetValue( index, out var total ) ? total + 1 : 1;
		if ( polygon.IsLod )
			lodTotals[index] = lodTotals.TryGetValue( index, out var lod ) ? lod + 1 : 1;
		if ( polygon.IsPortal )
			portalTotals[index] = portalTotals.TryGetValue( index, out var portal ) ? portal + 1 : 1;
		if ( polygon.IsOccluder )
			occluderTotals[index] = occluderTotals.TryGetValue( index, out var occ ) ? occ + 1 : 1;
	}

	var topLod = string.Join( "; ", lodTotals
		.OrderByDescending( x => x.Value )
		.Take( 20 )
		.Select( x => DescribeMaterialBucket( mesh, x.Key, totals, lodTotals, portalTotals, occluderTotals ) ) );

	return $"Top LOD materials: {topLod}";
}

static string DescribeMaterialBucket(
	IMesh mesh,
	int materialIndex,
	Dictionary<int, int> totals,
	Dictionary<int, int> lodTotals,
	Dictionary<int, int> portalTotals,
	Dictionary<int, int> occluderTotals )
{
	var materialName = materialIndex >= 0 && materialIndex < mesh.Materials.Count
		? mesh.Materials[materialIndex].Name
		: "<out_of_range>";
	totals.TryGetValue( materialIndex, out var total );
	lodTotals.TryGetValue( materialIndex, out var lod );
	portalTotals.TryGetValue( materialIndex, out var portal );
	occluderTotals.TryGetValue( materialIndex, out var occluder );
	return $"#{materialIndex} {materialName} Total={total} Lod={lod} Portal={portal} Occluder={occluder}";
}

static string SummarizePolygonGeometry( IMesh mesh )
{
	var lodByVertexCount = new Dictionary<int, int>();
	var nonLodByVertexCount = new Dictionary<int, int>();
	var lodLargeArea = 0;
	var nonLodLargeArea = 0;

	foreach ( var polygon in mesh.Polygons )
	{
		var vertexCount = polygon.PositionIndices.Count;
		var area = EstimatePolygonArea( mesh, polygon );
		var target = polygon.IsLod ? lodByVertexCount : nonLodByVertexCount;
		target[vertexCount] = target.TryGetValue( vertexCount, out var count ) ? count + 1 : 1;

		if ( area >= 250_000f )
		{
			if ( polygon.IsLod ) lodLargeArea++;
			else nonLodLargeArea++;
		}
	}

	var lodCounts = string.Join( ", ", lodByVertexCount.OrderBy( x => x.Key ).Select( x => $"{x.Key}v={x.Value}" ) );
	var nonLodCounts = string.Join( ", ", nonLodByVertexCount.OrderBy( x => x.Key ).Select( x => $"{x.Key}v={x.Value}" ) );
	return $"Polygon geometry: LodVertexCounts[{lodCounts}] NonLodVertexCounts[{nonLodCounts}] LargeArea[Lod={lodLargeArea}, NonLod={nonLodLargeArea}]";
}

static float EstimatePolygonArea( IMesh mesh, IPolygon polygon )
{
	if ( polygon.PositionIndices.Count < 3 )
		return 0f;

	var origin = mesh.Positions[polygon.PositionIndices[0]];
	var area = 0f;
	for ( var i = 1; i < polygon.PositionIndices.Count - 1; i++ )
	{
		var a = mesh.Positions[polygon.PositionIndices[i]] - origin;
		var b = mesh.Positions[polygon.PositionIndices[i + 1]] - origin;
		area += System.Numerics.Vector3.Cross( a, b ).Length() * 0.5f;
	}

	return area;
}

static string SummarizeWorldVisuals( World world )
{
	var visualUsage = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
	var flagged = new List<string>();

	foreach ( var root in world.RootObjects )
		CollectVisuals( root, 0, visualUsage, flagged );

	var topVisuals = string.Join( "; ", visualUsage
		.OrderByDescending( x => x.Value )
		.Take( 30 )
		.Select( x => $"{x.Key}={x.Value}" ) );

	var flaggedSummary = string.Join( "; ", flagged.Take( 30 ) );
	return $"World visuals: Unique={visualUsage.Count} Top=[{topVisuals}] Flagged=[{flaggedSummary}]";
}

static void CollectVisuals( object instance, int depth, Dictionary<string, int> visualUsage, List<string> flagged )
{
	if ( instance is null || depth > 64 )
		return;

	var visual = ReadVisualPath( instance );
	if ( !string.IsNullOrWhiteSpace( visual ) )
	{
		visualUsage[visual] = visualUsage.TryGetValue( visual, out var count ) ? count + 1 : 1;
		if ( LooksLikeLodVisual( visual ) )
		{
			var name = ReadStringMember( instance, "PresetName" )
				?? ReadStringMember( instance, "VobName" )
				?? ReadStringMember( instance, "Name" )
				?? instance.GetType().Name;
			flagged.Add( $"{name}:{visual}" );
		}
	}

	if ( ReadEnumerableMember( instance, "Children" ) is not System.Collections.IEnumerable children )
		return;

	foreach ( var child in children )
		CollectVisuals( child, depth + 1, visualUsage, flagged );
}

static bool LooksLikeLodVisual( string value )
{
	if ( string.IsNullOrWhiteSpace( value ) )
		return false;

	return value.Contains( "LOD", StringComparison.OrdinalIgnoreCase )
		|| value.Contains( "LOW", StringComparison.OrdinalIgnoreCase )
		|| value.Contains( "FAR", StringComparison.OrdinalIgnoreCase );
}

static string ReadVisualPath( object instance )
{
	foreach ( var memberName in new[] { "Visual", "VisualName", "Model", "Mesh" } )
	{
		var direct = ReadStringMember( instance, memberName );
		if ( !string.IsNullOrWhiteSpace( direct ) )
			return direct;

		var nested = ReadObjectMember( instance, memberName );
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

static object ReadObjectMember( object instance, string memberName )
{
	try
	{
		var property = instance.GetType().GetProperty( memberName, BindingFlags.Public | BindingFlags.Instance );
		if ( property is not null )
			return property.GetValue( instance );

		var field = instance.GetType().GetField( memberName, BindingFlags.Public | BindingFlags.Instance );
		return field?.GetValue( instance );
	}
	catch
	{
		return null;
	}
}

static System.Collections.IEnumerable ReadEnumerableMember( object instance, string memberName )
{
	var value = ReadObjectMember( instance, memberName );
	return value as System.Collections.IEnumerable;
}

static string ReadStringMember( object instance, string memberName )
{
	return ReadObjectMember( instance, memberName ) as string;
}

