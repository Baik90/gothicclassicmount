using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sandbox.Mounting;
using IODirectory = System.IO.Directory;
using IOPath = System.IO.Path;
using SMaterial = Sandbox.Material;
using STexture = Sandbox.Texture;

namespace GothicClassicMount;

public sealed class GothicClassicMount : BaseGameMount
{
	public new const long SteamAppId = 65540;
	private const string MountVersion = "gothic_classic";
	private const string InternalMountRoot = "_gothicruntime";
	private const string GothicZenVersion = "108";

	private readonly Dictionary<string, string> _textureLookup = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, GothicAssetDescriptor> _assetDescriptors = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, Sandbox.Texture> _textureCache = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, SMaterial> _materialCache = new( StringComparer.OrdinalIgnoreCase );
	private readonly object _cacheLock = new();
	private readonly List<string> _mountedTexturePaths = [];
	private readonly List<string> _mountedModelPaths = [];
	private readonly List<string> _mountedWorldPaths = [];

	public override string Ident => "gothicclassic";
	public override string Title => "Gothic 1 Classic";

	public string InstallDirectory { get; private set; } = string.Empty;
	public object VirtualFileSystem { get; private set; }
	public object ProcessedWorldFileSystem { get; private set; }
	public IReadOnlyList<string> MountedTexturePaths => _mountedTexturePaths;
	public IReadOnlyList<string> MountedModelPaths => _mountedModelPaths;
	public IReadOnlyList<string> MountedWorldPaths => _mountedWorldPaths;

	protected override void Initialize( InitializeContext context )
	{
		ZenKitRuntime.TryInitialize();

		if ( context.IsAppInstalled( SteamAppId ) )
		{
			var appDirectory = context.GetAppDirectory( SteamAppId );
			if ( IODirectory.Exists( appDirectory ) )
			{
				InstallDirectory = appDirectory;
				IsInstalled = true;
				return;
			}
		}

		var fallbackDirectory = IOPath.GetFullPath( IOPath.Combine( AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Gothic" ) );
		if ( IODirectory.Exists( fallbackDirectory ) )
		{
			InstallDirectory = fallbackDirectory;
			IsInstalled = true;
			return;
		}

		Log.Warning( "Gothic 1 Classic installation was not found." );
	}

	protected override Task Mount( MountContext context )
	{
		if ( !IsInstalled )
			return Task.CompletedTask;

		ZenKitRuntime.EnsureAvailable();

		_textureLookup.Clear();
		_assetDescriptors.Clear();
		_textureCache.Clear();
		_materialCache.Clear();
		_mountedTexturePaths.Clear();
		_mountedModelPaths.Clear();
		_mountedWorldPaths.Clear();
		ProcessedWorldFileSystem = null;

		VirtualFileSystem = BuildVirtualFileSystem();

		var allFiles = EnumerateFiles( ZenKitRuntime.GetRootNode( VirtualFileSystem ) )
			.OrderBy( x => x.VirtualPath, StringComparer.OrdinalIgnoreCase )
			.ToArray();

		foreach ( var file in allFiles.Where( x => IOPath.GetExtension( x.VirtualPath ).Equals( ".TEX", StringComparison.OrdinalIgnoreCase ) ) )
		{
			IndexTexturePath( file.VirtualPath );
			_mountedTexturePaths.Add( file.VirtualPath );
			context.Add( ResourceType.Texture, ToInternalMountedTexturePath( file.VirtualPath ), new GothicTextureResource( file.VirtualPath ) );
		}

		foreach ( var file in allFiles )
		{
			var extension = IOPath.GetExtension( file.VirtualPath );
			if ( string.IsNullOrWhiteSpace( extension ) )
				continue;

			if ( IsWorldExtension( extension ) && IsWorldPath( file.VirtualPath ) && IsRenderableWorld( file.VirtualPath ) )
			{
				_assetDescriptors[file.VirtualPath] = BuildAssetDescriptor( file.VirtualPath, true );
				_mountedWorldPaths.Add( file.VirtualPath );
				context.Add( ResourceType.Scene, ToInternalMountedWorldScenePath( file.VirtualPath ), new GothicWorldSceneResource( file.VirtualPath ) );
				context.Add( ResourceType.Model, ToInternalMountedWorldPath( file.VirtualPath ), new GothicWorldResource( file.VirtualPath ) );
			}
			else if ( IsModelExtension( extension ) )
			{
				_assetDescriptors[file.VirtualPath] = BuildAssetDescriptor( file.VirtualPath, false );
				_mountedModelPaths.Add( file.VirtualPath );
				context.Add( ResourceType.Model, ToInternalMountedModelPath( file.VirtualPath ), new GothicModelResource( file.VirtualPath ) );
				if ( extension.Equals( ".MRM", StringComparison.OrdinalIgnoreCase ) || extension.Equals( ".MSH", StringComparison.OrdinalIgnoreCase ) )
					context.Add( ResourceType.PrefabFile, GetMountedPrefabResourcePath( file.VirtualPath ), new GothicPrefabResource( file.VirtualPath ) );
			}
		}

		Log.Info( $"Mounted Gothic Classic from '{InstallDirectory}' with {allFiles.Length} files." );
		IsMounted = true;
		return Task.CompletedTask;
	}

	public string GetMountedPrefabResourcePath( string virtualPath ) =>
		IOPath.Combine( InternalMountRoot, "prefabs", MountVersion, virtualPath + ".prefab" ).Replace( '\\', '/' ).ToLowerInvariant();

	public Sandbox.Texture LoadTexture( string virtualPath, string resourcePath = null )
	{
		lock ( _cacheLock )
		{
			if ( _textureCache.TryGetValue( virtualPath, out var cachedTexture ) )
				return cachedTexture;
		}

		try
		{
			dynamic texture = ZenKitRuntime.CreateTexture( RequireVfs(), virtualPath );
			var width = Math.Max( 1, (int)texture.Width );
			var height = Math.Max( 1, (int)texture.Height );
			byte[] data = LoadTexturePixels( texture, virtualPath, width, height );
			var textureName = string.IsNullOrWhiteSpace( resourcePath )
				? IOPath.GetFileNameWithoutExtension( virtualPath )
				: resourcePath;

			var sandboxTexture = STexture.Create( width, height, ImageFormat.BGRA8888 )
				.WithName( textureName )
				.WithData( data )
				.Finish();

			lock ( _cacheLock )
			{
				_textureCache[virtualPath] = sandboxTexture;
			}

			return sandboxTexture;
		}
		catch ( Exception exception )
		{
			Log.Warning( $"Failed to load Gothic texture '{virtualPath}': {exception}" );
			return Sandbox.Texture.White;
		}
	}

	public SMaterial LoadMaterial( object gothicMaterial, string fallbackMaterialName )
	{
		var materialInfo = ResolveMaterialInfo( gothicMaterial, fallbackMaterialName );
		return LoadMaterial( new GothicMaterialDescriptor
		{
			MaterialName = materialInfo.MaterialName,
			TexturePath = materialInfo.TexturePath,
			CacheKey = materialInfo.CacheKey,
			DebugSource = materialInfo.DebugSource
		} );
	}

	public SMaterial LoadMaterial( GothicMaterialDescriptor descriptor )
	{
		var materialInfo = descriptor ?? new GothicMaterialDescriptor
		{
			MaterialName = "gothic_material",
			TexturePath = null,
			CacheKey = "gothic_material",
			DebugSource = "null_descriptor"
		};

		var cacheKey = "alpha_v1_" + materialInfo.CacheKey;
		lock ( _cacheLock )
		{
			if ( _materialCache.TryGetValue( cacheKey, out var cachedMaterial ) )
				return cachedMaterial;
		}

		var safeMaterialName = SanitizeResourceName( $"gothic_{materialInfo.MaterialName}_{cacheKey}" );

		if ( materialInfo.TexturePath is null )
		{
			Log.Info( $"Gothic material '{materialInfo.MaterialName}' did not resolve to a texture. Source: {materialInfo.DebugSource}" );
		}

		var material = SMaterial.Create( safeMaterialName, "gothic_surface" );
		material.Attributes?.SetCombo( "D_RENDER_BACKFACES", true );
		material.Set( "Color", materialInfo.TexturePath is null ? Sandbox.Texture.White : LoadTexture( materialInfo.TexturePath ) );

		lock ( _cacheLock )
		{
			_materialCache[cacheKey] = material;
		}

		return material;
	}

	public GothicAssetDescriptor GetDescriptor( string virtualPath )
	{
		if ( _assetDescriptors.TryGetValue( virtualPath, out var descriptor ) )
			return descriptor;

		throw new InvalidOperationException( $"No Gothic asset descriptor was prepared for '{virtualPath}'." );
	}

	internal object CreateWorld( string virtualPath )
	{
		if ( ProcessedWorldFileSystem is not null )
		{
			var processedNode = ZenKitRuntime.ResolveNode( ProcessedWorldFileSystem, virtualPath );
			if ( processedNode is not null )
				return ZenKitRuntime.CreateWorld( ProcessedWorldFileSystem, virtualPath );
		}

		return ZenKitRuntime.CreateWorld( RequireVfs(), virtualPath );
	}

	public string GetMountedModelResourcePath( string virtualPath )
	{
		return ToInternalMountedModelPath( virtualPath );
	}

	public string GetMountedWorldResourcePath( string virtualPath )
	{
		return ToInternalMountedWorldPath( virtualPath );
	}

	public string GetMountedWorldSceneResourcePath( string virtualPath )
	{
		return ToInternalMountedWorldScenePath( virtualPath );
	}

	public string GetMountedTextureResourcePath( string virtualPath )
	{
		return ToInternalMountedTexturePath( virtualPath );
	}

	public string GetEditorModelAssetPath( string virtualPath )
	{
		return ToEditorModelPath( virtualPath );
	}

	public string GetEditorWorldAssetPath( string virtualPath )
	{
		return ToEditorWorldPath( virtualPath );
	}

	public string GetEditorTextureAssetPath( string virtualPath )
	{
		return ToEditorTexturePath( virtualPath );
	}

	public string GetMountedResourceUri( string resourcePath )
	{
		return $"mount://{Ident}/{resourcePath}";
	}

	private object BuildVirtualFileSystem()
	{
		var vfs = ZenKitRuntime.CreateVfs();
		ProcessedWorldFileSystem = BuildProcessedWorldFileSystem();
		var archiveDirectories = new[]
		{
			IOPath.Combine( InstallDirectory, "Data" ),
			IOPath.Combine( InstallDirectory, "Data", "ModVDF" )
		};

		foreach ( var directory in archiveDirectories.Where( IODirectory.Exists ) )
		{
			foreach ( var archivePath in IODirectory.EnumerateFiles( directory, "*.VDF", SearchOption.TopDirectoryOnly ).OrderBy( x => x, StringComparer.OrdinalIgnoreCase ) )
			{
				Log.Info( $"Mounting Gothic archive '{archivePath}'" );
				ZenKitRuntime.MountDisk( vfs, archivePath );
			}
		}

		return vfs;
	}

	private object BuildProcessedWorldFileSystem()
	{
		try
		{
			var sourceWorldRoot = IOPath.Combine( InstallDirectory, "VDFS-Tool", "_WORK", "DATA", "WORLDS" );
			if ( !IODirectory.Exists( sourceWorldRoot ) )
				return null;

			var gothicZenPath = FindGothicZenExecutable();
			if ( string.IsNullOrWhiteSpace( gothicZenPath ) || !File.Exists( gothicZenPath ) )
			{
				Log.Warning( "GothicZEN executable was not found. Falling back to original world ZEN files." );
				return null;
			}

			var cacheRoot = GetProcessedWorldCacheRoot();
			IODirectory.CreateDirectory( cacheRoot );

			var wroteAny = false;
			foreach ( var sourcePath in IODirectory.EnumerateFiles( sourceWorldRoot, "*.ZEN", SearchOption.TopDirectoryOnly ) )
			{
				var fileName = IOPath.GetFileName( sourcePath );
				var virtualPath = $"_WORK/DATA/WORLDS/{fileName}";
				if ( !IsRenderableWorld( virtualPath ) )
					continue;

				var outputPath = IOPath.Combine( cacheRoot, "_WORK", "DATA", "WORLDS", fileName );
				IODirectory.CreateDirectory( IOPath.GetDirectoryName( outputPath )! );
				if ( EnsureProcessedWorldFile( gothicZenPath, sourcePath, outputPath ) )
					wroteAny = true;
			}

			var cachedWorldRoot = IOPath.Combine( cacheRoot, "_WORK" );
			if ( !IODirectory.Exists( cachedWorldRoot ) )
				return null;

			var vfs = ZenKitRuntime.CreateVfs();
			ZenKitRuntime.MountDisk( vfs, cacheRoot );
			Log.Info( $"Mounted processed Gothic world cache '{cacheRoot}' (Updated={wroteAny})" );
			return vfs;
		}
		catch ( Exception exception )
		{
			Log.Warning( $"Failed to build processed Gothic world cache: {exception}" );
			return null;
		}
	}

	private bool EnsureProcessedWorldFile( string gothicZenPath, string sourcePath, string outputPath )
	{
		var sourceInfo = new FileInfo( sourcePath );
		var outputInfo = new FileInfo( outputPath );
		if ( outputInfo.Exists && outputInfo.LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc )
			return false;

		var startInfo = new ProcessStartInfo
		{
			FileName = gothicZenPath,
			Arguments = $"{GothicZenVersion} {GothicZenVersion} \"{sourcePath}\" \"{outputPath}\"",
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = IOPath.GetDirectoryName( gothicZenPath ) ?? InstallDirectory
		};

		using var process = Process.Start( startInfo );
		process?.WaitForExit();
		if ( process is null || process.ExitCode != 0 || !File.Exists( outputPath ) )
			throw new InvalidOperationException( $"GothicZEN failed for '{sourcePath}'." );

		Log.Info( $"Processed Gothic world '{sourcePath}' -> '{outputPath}'" );
		return true;
	}

	private string GetProcessedWorldCacheRoot()
	{
		return IOPath.GetFullPath( IOPath.Combine( Environment.CurrentDirectory, "addons", "gothicclassicmount", ".cache", "gothiczen" ) );
	}

	private string FindGothicZenExecutable()
	{
		var candidates = new[]
		{
			IOPath.Combine( Environment.GetFolderPath( Environment.SpecialFolder.DesktopDirectory ), "GothicZEN", "Binaries", "x64", "GothicZEN.exe" ),
			IOPath.Combine( InstallDirectory, "GothicZEN.exe" ),
			IOPath.Combine( InstallDirectory, "Tools", "GothicZEN.exe" )
		};

		return candidates.FirstOrDefault( File.Exists );
	}

	private IEnumerable<(string VirtualPath, object Node)> EnumerateFiles( object root )
	{
		return EnumerateFiles( root, string.Empty );
	}

	private IEnumerable<(string VirtualPath, object Node)> EnumerateFiles( object node, string currentPath )
	{
		foreach ( var child in ZenKitRuntime.GetChildren( node ) )
		{
			var childName = ZenKitRuntime.GetName( child );
			var childPath = string.IsNullOrEmpty( currentPath ) ? childName : $"{currentPath}/{childName}";
			if ( ZenKitRuntime.IsDir( child ) )
			{
				foreach ( var nested in EnumerateFiles( child, childPath ) )
					yield return nested;
			}
			else if ( ZenKitRuntime.IsFile( child ) )
			{
				yield return (childPath.Replace( '\\', '/' ), child);
			}
		}
	}

	private void IndexTexturePath( string virtualPath )
	{
		var normalized = virtualPath.Replace( '\\', '/' );
		foreach ( var key in EnumerateTextureLookupKeys( normalized ) )
		{
			_textureLookup.TryAdd( key, normalized );
		}
	}

	internal object RequireVfs()
	{
		return VirtualFileSystem ?? throw new InvalidOperationException( "Gothic VFS is not initialized." );
	}

	private MaterialInfo ResolveMaterialInfo( object gothicMaterial, string fallbackMaterialName )
	{
		var materialName = fallbackMaterialName;
		var candidates = new List<string>();
		var debugValues = new List<string>();

		if ( gothicMaterial is not null )
		{
			foreach ( var property in gothicMaterial.GetType().GetProperties() )
			{
				if ( property.GetIndexParameters().Length > 0 )
					continue;

				object value;
				try
				{
					value = property.GetValue( gothicMaterial );
				}
				catch
				{
					continue;
				}

				CollectMaterialValue( property.Name, value, candidates, debugValues, ref materialName );
			}

			foreach ( var field in gothicMaterial.GetType().GetFields() )
			{
				object value;
				try
				{
					value = field.GetValue( gothicMaterial );
				}
				catch
				{
					continue;
				}

				CollectMaterialValue( field.Name, value, candidates, debugValues, ref materialName );
			}
		}

		if ( !string.IsNullOrWhiteSpace( fallbackMaterialName ) )
			candidates.Add( fallbackMaterialName );

		var materialText = gothicMaterial?.ToString();
		if ( !string.IsNullOrWhiteSpace( materialText ) && !materialText.StartsWith( "ZenKit.", StringComparison.OrdinalIgnoreCase ) )
		{
			debugValues.Add( $"ToString={materialText}" );
			candidates.Add( materialText );
		}

		var texturePath = candidates
			.Select( ResolveTexturePath )
			.FirstOrDefault( path => !string.IsNullOrWhiteSpace( path ) );

		var resolvedName = string.IsNullOrWhiteSpace( materialName ) ? "gothic_material" : materialName;
		var resolvedKey = texturePath ?? resolvedName;
		var debugSource = debugValues.Count == 0 ? "<none>" : string.Join( ", ", debugValues.Distinct( StringComparer.OrdinalIgnoreCase ) );

		return new MaterialInfo
		{
			MaterialName = resolvedName,
			TexturePath = texturePath,
			CacheKey = SanitizeResourceName( resolvedKey ),
			DebugSource = debugSource
		};
	}

	private GothicAssetDescriptor BuildAssetDescriptor( string virtualPath, bool isWorld )
	{
		var materials = isWorld
			? BuildWorldMaterialDescriptors( virtualPath )
			: BuildModelMaterialDescriptors( virtualPath );

		return new GothicAssetDescriptor
		{
			VirtualPath = virtualPath,
			IsWorld = isWorld,
			Materials = materials
		};
	}

	private IReadOnlyList<GothicMaterialDescriptor> BuildWorldMaterialDescriptors( string virtualPath )
	{
		dynamic world = CreateWorld( virtualPath );
		var mesh = ZenKitRuntime.GetProperty( world, "Mesh" );
		if ( mesh is null )
			return Array.Empty<GothicMaterialDescriptor>();

		var materials = ZenKitRuntime.GetProperty( mesh, "Materials" ) as IEnumerable;
		return BuildMaterialDescriptors( materials, "world" );
	}

	private IReadOnlyList<GothicMaterialDescriptor> BuildModelMaterialDescriptors( string virtualPath )
	{
		var extension = IOPath.GetExtension( virtualPath );
		if ( extension.Equals( ".MSH", StringComparison.OrdinalIgnoreCase ) )
		{
			dynamic mesh = ZenKitRuntime.CreateMesh( RequireVfs(), virtualPath );
			var materials = ZenKitRuntime.GetProperty( mesh, "Materials" ) as IEnumerable;
			return BuildMaterialDescriptors( materials, "mesh" );
		}

		if ( extension.Equals( ".MRM", StringComparison.OrdinalIgnoreCase ) )
		{
			dynamic mesh = ZenKitRuntime.CreateMultiResolutionMesh( RequireVfs(), virtualPath );
			return BuildSubMeshMaterialDescriptors( mesh, "mrm" );
		}

		if ( extension.Equals( ".MDM", StringComparison.OrdinalIgnoreCase ) )
		{
			dynamic modelMesh = ZenKitRuntime.CreateModelMesh( RequireVfs(), virtualPath );
			return BuildModelMeshMaterialDescriptors( modelMesh );
		}

		if ( extension.Equals( ".MDL", StringComparison.OrdinalIgnoreCase ) )
		{
			dynamic model = ZenKitRuntime.CreateModel( RequireVfs(), virtualPath );
			return BuildModelMeshMaterialDescriptors( model.Mesh );
		}

		return Array.Empty<GothicMaterialDescriptor>();
	}

	private IReadOnlyList<GothicMaterialDescriptor> BuildModelMeshMaterialDescriptors( dynamic modelMesh )
	{
		var descriptors = new List<GothicMaterialDescriptor>();

		foreach ( var softSkinMesh in (IEnumerable)modelMesh.Meshes )
		{
			var softMesh = ZenKitRuntime.GetProperty( softSkinMesh, "Mesh" );
			if ( softMesh is not null )
				descriptors.AddRange( BuildSubMeshMaterialDescriptors( softMesh, "submesh" ) );
		}

		if ( descriptors.Count > 0 )
			return descriptors;

		foreach ( DictionaryEntry attachment in (IDictionary)modelMesh.Attachments )
		{
			if ( attachment.Value is not null )
				descriptors.AddRange( BuildSubMeshMaterialDescriptors( attachment.Value, "attachment" ) );
		}

		return descriptors;
	}

	private IReadOnlyList<GothicMaterialDescriptor> BuildSubMeshMaterialDescriptors( object meshObject, string fallbackPrefix )
	{
		dynamic mesh = meshObject;
		var descriptors = new List<GothicMaterialDescriptor>();
		var index = 0;
		foreach ( var subMesh in (IEnumerable)mesh.SubMeshes )
		{
			var material = ZenKitRuntime.GetProperty( subMesh, "Material" );
			descriptors.Add( CreateMaterialDescriptor( material, $"{fallbackPrefix}_{index}" ) );
			index++;
		}

		return descriptors;
	}

	private IReadOnlyList<GothicMaterialDescriptor> BuildMaterialDescriptors( IEnumerable materials, string fallbackPrefix )
	{
		if ( materials is null )
			return Array.Empty<GothicMaterialDescriptor>();

		var descriptors = new List<GothicMaterialDescriptor>();
		var index = 0;
		foreach ( var material in materials )
		{
			descriptors.Add( CreateMaterialDescriptor( material, $"{fallbackPrefix}_{index}" ) );
			index++;
		}

		return descriptors;
	}

	private GothicMaterialDescriptor CreateMaterialDescriptor( object gothicMaterial, string fallbackMaterialName )
	{
		var info = ResolveMaterialInfo( gothicMaterial, fallbackMaterialName );
		return new GothicMaterialDescriptor
		{
			MaterialName = info.MaterialName,
			TexturePath = info.TexturePath,
			CacheKey = info.CacheKey,
			DebugSource = info.DebugSource
		};
	}

	public string DescribeMaterial( object gothicMaterial, string fallbackMaterialName )
	{
		var info = ResolveMaterialInfo( gothicMaterial, fallbackMaterialName );
		return $"MaterialName={info.MaterialName}; TexturePath={info.TexturePath ?? "<none>"}; Candidates={info.DebugSource}";
	}

	private static void CollectMaterialValue( string memberName, object value, List<string> candidates, List<string> debugValues, ref string materialName )
	{
		if ( value is null )
			return;

		if ( value is string text )
		{
			var trimmed = text.Trim();
			if ( string.IsNullOrWhiteSpace( trimmed ) )
				return;

			debugValues.Add( $"{memberName}={trimmed}" );
			candidates.Add( trimmed );

			if ( memberName.Equals( "Name", StringComparison.OrdinalIgnoreCase ) )
				materialName = trimmed;

			return;
		}

		var nestedName = TryGetMemberString( value, "Name" );
		if ( !string.IsNullOrWhiteSpace( nestedName ) )
		{
			debugValues.Add( $"{memberName}.Name={nestedName}" );
			candidates.Add( nestedName );
			if ( memberName.Equals( "Texture", StringComparison.OrdinalIgnoreCase ) && string.IsNullOrWhiteSpace( materialName ) )
				materialName = nestedName;
		}

		var nestedPath = TryGetMemberString( value, "Path" ) ?? TryGetMemberString( value, "FileName" );
		if ( !string.IsNullOrWhiteSpace( nestedPath ) )
		{
			debugValues.Add( $"{memberName}.Path={nestedPath}" );
			candidates.Add( nestedPath );
		}

		var nestedText = value.ToString();
		if ( !string.IsNullOrWhiteSpace( nestedText ) && !nestedText.StartsWith( value.GetType().Namespace ?? string.Empty, StringComparison.OrdinalIgnoreCase ) )
		{
			debugValues.Add( $"{memberName}.ToString={nestedText}" );
			candidates.Add( nestedText );
		}
	}

	private static string TryGetMemberString( object instance, string memberName )
	{
		if ( instance is null )
			return null;

		var property = instance.GetType().GetProperty( memberName );
		if ( property?.PropertyType == typeof( string ) )
			return property.GetValue( instance ) as string;

		var field = instance.GetType().GetField( memberName );
		if ( field?.FieldType == typeof( string ) )
			return field.GetValue( instance ) as string;

		return null;
	}

	private string ResolveTexturePath( string gothicTextureName )
	{
		if ( string.IsNullOrWhiteSpace( gothicTextureName ) )
			return null;

		var normalized = gothicTextureName.Replace( '\\', '/' );
		foreach ( var candidate in EnumerateTextureLookupKeys( normalized ) )
		{
			if ( _textureLookup.TryGetValue( candidate, out var path ) )
				return path;
		}

		return null;
	}

	private static IEnumerable<string> EnumerateTextureLookupKeys( string texturePath )
	{
		var normalized = texturePath.Replace( '\\', '/' );
		var fileName = IOPath.GetFileName( normalized );
		var baseName = IOPath.GetFileNameWithoutExtension( normalized );
		var compiledBaseName = StripCompiledSuffix( baseName );

		foreach ( var key in new[]
		{
			normalized,
			fileName,
			baseName,
			compiledBaseName,
			baseName + ".TEX",
			baseName + ".TGA",
			compiledBaseName + ".TEX",
			compiledBaseName + ".TGA"
		} )
		{
			if ( !string.IsNullOrWhiteSpace( key ) )
				yield return key;
		}
	}

	private static string StripCompiledSuffix( string textureName )
	{
		if ( string.IsNullOrWhiteSpace( textureName ) )
			return textureName;

		return textureName.EndsWith( "-C", StringComparison.OrdinalIgnoreCase )
			? textureName[..^2]
			: textureName;
	}

	private static void SwizzleRgbaToBgra( byte[] data )
	{
		for ( var i = 0; i + 3 < data.Length; i += 4 )
		{
			(data[i], data[i + 2]) = (data[i + 2], data[i]);
		}
	}

	private static byte[] LoadTexturePixels( dynamic texture, string virtualPath, int width, int height )
	{
		try
		{
			byte[] rgba = (byte[])ZenKitRuntime.InvokeMethod( texture, "GetMipmapRgba", 0 );
			if ( rgba is not null && rgba.Length == width * height * 4 )
			{
				SwizzleRgbaToBgra( rgba );
				return rgba;
			}
		}
		catch ( Exception exception )
		{
			_ = exception;
		}

		var raw = ZenKitRuntime.InvokeMethod( texture, "GetMipmapRaw", 0 ) as byte[];
		if ( raw is null )
			throw new InvalidOperationException( $"Texture '{virtualPath}' did not return pixel data." );

		var formatName = ZenKitRuntime.GetProperty( texture, "Format" )?.ToString() ?? string.Empty;
		return ConvertRawTextureToBgra( raw, formatName, width, height, virtualPath );
	}

	private static byte[] ConvertRawTextureToBgra( byte[] raw, string formatName, int width, int height, string virtualPath )
	{
		var expectedSize = width * height * 4;

		if ( raw.Length == expectedSize )
		{
			if ( formatName.Contains( "B8G8R8A8", StringComparison.OrdinalIgnoreCase ) )
				return raw;

			if ( formatName.Contains( "R8G8B8A8", StringComparison.OrdinalIgnoreCase ) || string.IsNullOrWhiteSpace( formatName ) )
			{
				SwizzleRgbaToBgra( raw );
				return raw;
			}

			if ( formatName.Contains( "A8R8G8B8", StringComparison.OrdinalIgnoreCase ) )
				return ConvertArgbToBgra( raw );

			if ( formatName.Contains( "A8B8G8R8", StringComparison.OrdinalIgnoreCase ) )
				return ConvertAbgrToBgra( raw );
		}

		throw new NotSupportedException( $"Texture '{virtualPath}' uses unsupported format '{formatName}' with {raw.Length} bytes." );
	}

	private static byte[] ConvertArgbToBgra( byte[] raw )
	{
		var converted = new byte[raw.Length];
		for ( var i = 0; i + 3 < raw.Length; i += 4 )
		{
			var a = raw[i];
			var r = raw[i + 1];
			var g = raw[i + 2];
			var b = raw[i + 3];
			converted[i] = b;
			converted[i + 1] = g;
			converted[i + 2] = r;
			converted[i + 3] = a;
		}

		return converted;
	}

	private static byte[] ConvertAbgrToBgra( byte[] raw )
	{
		var converted = new byte[raw.Length];
		for ( var i = 0; i + 3 < raw.Length; i += 4 )
		{
			var a = raw[i];
			var b = raw[i + 1];
			var g = raw[i + 2];
			var r = raw[i + 3];
			converted[i] = b;
			converted[i + 1] = g;
			converted[i + 2] = r;
			converted[i + 3] = a;
		}

		return converted;
	}

	private static string SanitizeResourceName( string value )
	{
		var chars = value
			.ToLowerInvariant()
			.Select( x => char.IsLetterOrDigit( x ) || x == '_' ? x : '_' )
			.ToArray();

		return new string( chars );
	}

	private static bool IsModelExtension( string extension )
	{
		return extension.Equals( ".MRM", StringComparison.OrdinalIgnoreCase )
			|| extension.Equals( ".MDM", StringComparison.OrdinalIgnoreCase )
			|| extension.Equals( ".MDL", StringComparison.OrdinalIgnoreCase )
			|| extension.Equals( ".MSH", StringComparison.OrdinalIgnoreCase );
	}

	private static bool IsWorldExtension( string extension )
	{
		return extension.Equals( ".ZEN", StringComparison.OrdinalIgnoreCase );
	}

	private static bool IsWorldPath( string virtualPath )
	{
		var normalized = virtualPath.Replace( '\\', '/' );
		return normalized.Contains( "/WORLDS/", StringComparison.OrdinalIgnoreCase )
			|| normalized.StartsWith( "WORLDS/", StringComparison.OrdinalIgnoreCase );
	}

	private bool IsRenderableWorld( string virtualPath )
	{
		try
		{
			dynamic world = CreateWorld( virtualPath );
			var mesh = ZenKitRuntime.GetProperty( world, "Mesh" );
			if ( mesh is null )
				return false;

			var polygons = ZenKitRuntime.GetProperty( mesh, "Polygons" ) as IEnumerable;
			if ( polygons is null )
				return false;

			foreach ( var _ in polygons )
				return true;

			Log.Info( $"Skipping Gothic world candidate without polygons: {virtualPath}" );
			return false;
		}
		catch ( Exception exception )
		{
			Log.Info( $"Skipping Gothic world candidate '{virtualPath}': {exception.Message}" );
			return false;
		}
	}

	private static string ToEditorTexturePath( string virtualPath )
	{
		return IOPath.Combine( "textures", MountVersion, IOPath.ChangeExtension( virtualPath, ".vtex" ) )
			.Replace( '\\', '/' )
			.ToLowerInvariant();
	}

	private static string ToEditorModelPath( string virtualPath )
	{
		return IOPath.Combine( "models", MountVersion, IOPath.ChangeExtension( virtualPath, ".vmdl" ) )
			.Replace( '\\', '/' )
			.ToLowerInvariant();
	}

	private static string ToEditorWorldPath( string virtualPath )
	{
		return IOPath.Combine( "worlds", MountVersion, IOPath.ChangeExtension( virtualPath, ".vmdl" ) )
			.Replace( '\\', '/' )
			.ToLowerInvariant();
	}

	private static string ToEditorWorldScenePath( string virtualPath )
	{
		return IOPath.Combine( "worlds", MountVersion, IOPath.ChangeExtension( virtualPath, ".scene" ) )
			.Replace( '\\', '/' )
			.ToLowerInvariant();
	}

	private static string ToInternalMountedTexturePath( string virtualPath )
	{
		return IOPath.Combine( InternalMountRoot, ToEditorTexturePath( virtualPath ) )
			.Replace( '\\', '/' )
			.ToLowerInvariant();
	}

	private static string ToInternalMountedModelPath( string virtualPath )
	{
		return IOPath.Combine( InternalMountRoot, ToEditorModelPath( virtualPath ) )
			.Replace( '\\', '/' )
			.ToLowerInvariant();
	}

	private static string ToInternalMountedWorldPath( string virtualPath )
	{
		return IOPath.Combine( InternalMountRoot, ToEditorWorldPath( virtualPath ) )
			.Replace( '\\', '/' )
			.ToLowerInvariant();
	}

	private static string ToInternalMountedWorldScenePath( string virtualPath )
	{
		return IOPath.Combine( InternalMountRoot, ToEditorWorldScenePath( virtualPath ) )
			.Replace( '\\', '/' )
			.ToLowerInvariant();
	}

	private sealed class MaterialInfo
	{
		public string MaterialName { get; init; }
		public string TexturePath { get; init; }
		public string CacheKey { get; init; }
		public string DebugSource { get; init; }
	}

	public sealed class GothicAssetDescriptor
	{
		public string VirtualPath { get; init; }
		public bool IsWorld { get; init; }
		public IReadOnlyList<GothicMaterialDescriptor> Materials { get; init; }
	}

	public sealed class GothicMaterialDescriptor
	{
		public string MaterialName { get; init; }
		public string TexturePath { get; init; }
		public string CacheKey { get; init; }
		public string DebugSource { get; init; }
	}
}
