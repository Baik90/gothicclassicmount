using System.Reflection;

var dataPath = @"E:\Steam\steamapps\common\Gothic\Data";
var worldPath = "_WORK/DATA/WORLDS/WORLD.ZEN";

var zenKitAssembly = Assembly.LoadFrom( Path.GetFullPath( Path.Combine( AppContext.BaseDirectory, "ZenKit.dll" ) ) );
var vfsType = zenKitAssembly.GetType( "ZenKit.Vfs", true )!;
var overwriteType = zenKitAssembly.GetType( "ZenKit.VfsOverwriteBehavior", true )!;
var worldType = zenKitAssembly.GetType( "ZenKit.World", true )!;

var vfs = Activator.CreateInstance( vfsType )!;
var older = Enum.Parse( overwriteType, "Older" );
var mountDisk = vfsType.GetMethod( "MountDisk", [typeof( string ), overwriteType] )!;
foreach ( var vdf in Directory.EnumerateFiles( dataPath, "*.VDF" ) )
{
	mountDisk.Invoke( vfs, [vdf, older] );
}

var resolve = vfsType.GetMethod( "Resolve", [typeof( string )] )!;
var node = resolve.Invoke( vfs, [worldPath] ) ?? throw new InvalidOperationException( $"{worldPath} not resolved" );
var buffer = node.GetType().GetProperty( "Buffer" )!.GetValue( node )!;
var world = Activator.CreateInstance( worldType, [buffer] )!;

Console.WriteLine( $"World type: {world.GetType().FullName}" );

var stringProps = world.GetType().GetProperties()
	.Where( p => p.PropertyType == typeof( string ) )
	.Select( p => $"{p.Name}={p.GetValue( world )}" )
	.ToList();

Console.WriteLine( "String properties:" );
foreach ( var line in stringProps )
	Console.WriteLine( line );

Console.WriteLine( "Properties:" );
foreach ( var prop in world.GetType().GetProperties() )
{
	object value;
	try
	{
		value = prop.GetValue( world );
	}
	catch ( Exception e )
	{
		value = $"<error:{e.GetType().Name}>";
	}

	var display = value switch
	{
		null => "<null>",
		string s => s,
		_ => value.GetType().FullName
	};
	Console.WriteLine( $"{prop.Name}: {prop.PropertyType.FullName} = {display}" );
}

var rootObjectsProp = world.GetType().GetProperty( "RootObjects" );
if ( rootObjectsProp?.GetValue( world ) is System.Collections.IEnumerable rootObjects )
{
	Console.WriteLine( "RootObjects sample:" );
	var count = 0;
	foreach ( var obj in rootObjects )
	{
		if ( obj is null )
			continue;

		var type = obj.GetType();
		var stringValues = type.GetProperties()
			.Where( p => p.PropertyType == typeof( string ) && p.GetIndexParameters().Length == 0 )
			.Select( p =>
			{
				try { return $"{p.Name}={p.GetValue( obj )}"; }
				catch { return $"{p.Name}=<error>"; }
			} )
			.Where( s => !s.EndsWith( "=" ) && !s.EndsWith( "=<error>" ) )
			.ToList();

		Console.WriteLine( $"- {type.FullName}" );
		foreach ( var value in stringValues.Take( 8 ) )
			Console.WriteLine( $"  {value}" );

		count++;
		if ( count >= 12 )
			break;
	}

	Console.WriteLine( $"RootObjects total inspected: {count}" );
}
