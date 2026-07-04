using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GothicClassicMount;

internal static class ZenKitRuntime
{
	private static Assembly _assembly;
	private static bool _attemptedLoad;

	public static void TryInitialize()
	{
		if ( _attemptedLoad )
			return;

		_attemptedLoad = true;

		var candidates = new[]
		{
			Path.Combine( AppContext.BaseDirectory, "Libraries", "ZenKit", "ZenKit.dll" ),
			Path.Combine( AppContext.BaseDirectory, "ZenKit.dll" ),
			Path.Combine( Directory.GetCurrentDirectory(), "Editor", "Libraries", "ZenKit", "ZenKit.dll" ),
			Path.Combine( Directory.GetCurrentDirectory(), "addons", "gothicclassicmount", "Editor", "Libraries", "ZenKit", "ZenKit.dll" )
		};

		foreach ( var candidate in candidates.Where( File.Exists ) )
		{
			try
			{
				_assembly = Assembly.LoadFrom( candidate );
				return;
			}
			catch ( Exception e )
			{
				Log.Warning( $"Failed to load ZenKit from '{candidate}': {e.Message}" );
			}
		}
	}

	public static void EnsureAvailable()
	{
		if ( _assembly is null )
			throw new InvalidOperationException( "ZenKit runtime is unavailable. Expected Editor/Libraries/ZenKit/ZenKit.dll to be loadable at runtime." );
	}

	public static object CreateVfs()
	{
		return Activator.CreateInstance( GetType( "ZenKit.Vfs" ) );
	}

	public static void MountDisk( object vfs, string archivePath )
	{
		var enumType = GetType( "ZenKit.VfsOverwriteBehavior" );
		var older = Enum.Parse( enumType, "Older" );
		vfs.GetType().GetMethod( "MountDisk", new[] { typeof( string ), enumType } )!.Invoke( vfs, new[] { archivePath, older } );
	}

	public static object GetRootNode( object vfs ) => GetProperty( vfs, "Root" );
	public static object ResolveNode( object vfs, string path ) => vfs.GetType().GetMethod( "Resolve", [typeof( string )] )!.Invoke( vfs, [NormalizePath( path )] );
	public static IEnumerable<object> GetChildren( object node ) => ((IEnumerable)GetProperty( node, "Children" )).Cast<object>();
	public static string GetName( object node ) => GetProperty<string>( node, "Name" );
	public static bool IsDir( object node ) => (bool)node.GetType().GetMethod( "IsDir" )!.Invoke( node, null );
	public static bool IsFile( object node ) => (bool)node.GetType().GetMethod( "IsFile" )!.Invoke( node, null );

	public static object CreateTexture( object vfs, string virtualPath ) => CreateFromVfsBuffer( "ZenKit.Texture", vfs, virtualPath );
	public static object CreateWorld( object vfs, string virtualPath ) => CreateFromVfsBuffer( "ZenKit.World", vfs, virtualPath );
	public static object CreateMesh( object vfs, string virtualPath ) => CreateFromVfsBuffer( "ZenKit.Mesh", vfs, virtualPath );
	public static object CreateMultiResolutionMesh( object vfs, string virtualPath ) => CreateFromVfsBuffer( "ZenKit.MultiResolutionMesh", vfs, virtualPath );
	public static object CreateModelMesh( object vfs, string virtualPath ) => CreateFromVfsBuffer( "ZenKit.ModelMesh", vfs, virtualPath );
	public static object CreateModel( object vfs, string virtualPath ) => CreateFromVfsBuffer( "ZenKit.Model", vfs, virtualPath );

	public static object InvokeMethod( object instance, string name, params object[] args )
	{
		if ( instance is null )
			return null;

		return instance.GetType().GetMethod( name )?.Invoke( instance, args );
	}

	public static object GetProperty( object instance, string name )
	{
		if ( instance is null )
			return null;

		return instance.GetType().GetProperty( name )?.GetValue( instance );
	}

	public static T GetProperty<T>( object instance, string name )
	{
		var value = GetProperty( instance, name );
		if ( value is null )
			return default;
		return (T)value;
	}

	public static object GetField( object instance, string name )
	{
		if ( instance is null )
			return null;

		return instance.GetType().GetField( name )?.GetValue( instance );
	}

	public static T GetFieldValue<T>( object instance, string name )
	{
		var value = GetField( instance, name );
		if ( value is null )
			return default;
		return (T)value;
	}

	private static Type GetType( string fullName )
	{
		EnsureAvailable();
		return _assembly!.GetType( fullName, true )!;
	}

	private static object CreateFromVfsBuffer( string typeName, object vfs, string virtualPath )
	{
		var type = GetType( typeName );
		var normalized = NormalizePath( virtualPath );
		var node = ResolveNode( vfs, normalized );
		if ( node is null )
			throw new InvalidOperationException( $"ZenKit VFS could not resolve '{normalized}' for type '{typeName}'." );

		if ( !IsFile( node ) )
			throw new InvalidOperationException( $"ZenKit VFS path '{normalized}' is not a file node." );

		var buffer = GetProperty( node, "Buffer" );
		if ( buffer is null )
			throw new InvalidOperationException( $"ZenKit VFS file node '{normalized}' did not expose a readable buffer." );

		try
		{
			return Activator.CreateInstance( type, buffer )!;
		}
		catch ( TargetInvocationException exception ) when ( exception.InnerException is not null )
		{
			throw new InvalidOperationException( $"ZenKit failed to construct '{typeName}' from '{normalized}': {exception.InnerException.Message}", exception.InnerException );
		}
	}

	private static string NormalizePath( string path )
	{
		return path.Replace( '\\', '/' ).TrimStart( '/' );
	}
}
