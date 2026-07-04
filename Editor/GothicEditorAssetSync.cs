using System;
using System.IO;
using System.Text;

namespace GothicClassicMount;

internal static class GothicEditorAssetSync
{
	private static bool _isSynced;

	public static void EnsureSynced( GothicClassicMount mount )
	{
		if ( _isSynced || Project.Current is null )
			return;

		var assetsRoot = Project.Current.GetAssetsPath();
		if ( string.IsNullOrWhiteSpace( assetsRoot ) || !Directory.Exists( assetsRoot ) )
			return;

		var registeredCount = 0;

		foreach ( var virtualPath in mount.MountedModelPaths )
		{
			if ( EnsureModelProxy( assetsRoot, mount, virtualPath, isWorld: false ) )
				registeredCount++;
		}

		foreach ( var virtualPath in mount.MountedWorldPaths )
		{
			if ( EnsureModelProxy( assetsRoot, mount, virtualPath, isWorld: true ) )
				registeredCount++;
		}

		_isSynced = true;
		Log.Info( $"Synced {registeredCount} Gothic editor proxy assets." );
	}

	public static void Invalidate()
	{
		_isSynced = false;
	}

	private static bool EnsureModelProxy( string assetsRoot, GothicClassicMount mount, string virtualPath, bool isWorld )
	{
		var editorPath = isWorld
			? mount.GetEditorWorldAssetPath( virtualPath )
			: mount.GetEditorModelAssetPath( virtualPath );
		var internalMountPath = isWorld
			? mount.GetMountedWorldResourcePath( virtualPath )
			: mount.GetMountedModelResourcePath( virtualPath );
		var absolutePath = Path.Combine( assetsRoot, editorPath.Replace( '/', Path.DirectorySeparatorChar ) );

		Directory.CreateDirectory( Path.GetDirectoryName( absolutePath )! );

		var fileContents = BuildProxyModelDoc( mount.GetMountedResourceUri( internalMountPath ) );
		var needsWrite = !File.Exists( absolutePath ) || !string.Equals( File.ReadAllText( absolutePath ), fileContents, StringComparison.Ordinal );
		if ( needsWrite )
		{
			File.WriteAllText( absolutePath, fileContents, Encoding.UTF8 );
		}

		AssetSystem.RegisterFile( absolutePath );
		return true;
	}

	private static string BuildProxyModelDoc( string baseModelPath )
	{
		return string.Join( "\n",
			"<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc29:version{3cec427c-1b0e-4d48-a90a-0436f33a6041} -->",
			"{",
			"\trootNode = ",
			"\t{",
			"\t\t_class = \"RootNode\"",
			"\t\tchildren = [  ]",
			"\t\tmodel_archetype = \"\"",
			"\t\tprimary_associated_entity = \"\"",
			"\t\tanim_graph_name = \"\"",
			$"\t\tbase_model_name = \"{baseModelPath}\"",
			"\t}",
			"}",
			string.Empty );
	}
}
