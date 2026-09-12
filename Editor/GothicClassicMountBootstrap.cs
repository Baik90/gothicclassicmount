using System;
using Sandbox.Mounting;

namespace GothicClassicMount;

internal static class GothicClassicMountBootstrap
{
	private static bool _loggedReadyState;
	private static bool _validatedStartupAssets;

	[EditorEvent.Hotload]
	public static void OnHotload()
	{
		EnsureMounted();
	}

	[EditorEvent.Frame]
	public static void OnFrame()
	{
		EnsureMounted();
	}

	private static void EnsureMounted()
	{
		var mount = Directory.Get( "gothicclassic" );
		if ( mount is null )
			return;

		if ( !mount.IsMounted )
		{
			Directory.Mount( "gothicclassic" );
			_loggedReadyState = false;
			_validatedStartupAssets = false;
			GothicEditorAssetSync.Invalidate();
			return;
		}

		if ( !_loggedReadyState )
		{
			Log.Info( "Gothic Classic mount is active in editor." );
			_loggedReadyState = true;
		}

		if ( !_validatedStartupAssets && mount is GothicClassicMount gothicMount )
		{
			GothicEditorAssetSync.EnsureSynced( gothicMount );
			ValidateStartupAssets( gothicMount );
			_validatedStartupAssets = true;
		}
	}

	private static void ValidateStartupAssets( GothicClassicMount mount )
	{
		var firstModelPath = mount.MountedModelPaths.FirstOrDefault();
		if ( !string.IsNullOrWhiteSpace( firstModelPath ) )
		{
			try
			{
				GothicGeometryBuilder.BuildModel( mount, firstModelPath, mount.GetMountedModelResourcePath( firstModelPath ) );
				Log.Info( $"Gothic startup model validation succeeded: {firstModelPath}" );
			}
			catch ( Exception exception )
			{
				Log.Warning( $"Gothic startup model validation failed for '{firstModelPath}': {exception}" );
			}

			try
			{
				var editorModelPath = mount.GetEditorModelAssetPath( firstModelPath );
				var runtimeModelPath = mount.GetMountedModelResourcePath( firstModelPath );
				var runtimeModelUri = mount.GetMountedResourceUri( runtimeModelPath );
				var loadedEditorModel = Model.Load( editorModelPath );
				var loadedRuntimeModel = Model.Load( runtimeModelUri );
				Log.Info( $"Gothic startup model path validation succeeded: EditorPath={editorModelPath}; RuntimeUri={runtimeModelUri}; LoadedEditor={(loadedEditorModel is null ? "<null>" : loadedEditorModel.Name)}; LoadedRuntime={(loadedRuntimeModel is null ? "<null>" : loadedRuntimeModel.Name)}" );
			}
			catch ( Exception exception )
			{
				Log.Warning( $"Gothic startup model path validation failed for '{firstModelPath}': {exception}" );
			}
		}

		var firstWorldPath = mount.MountedWorldPaths.FirstOrDefault();
		if ( !string.IsNullOrWhiteSpace( firstWorldPath ) )
		{
			try
			{
				GothicGeometryBuilder.BuildWorldModel( mount, firstWorldPath, mount.GetMountedWorldResourcePath( firstWorldPath ) );
				Log.Info( $"Gothic startup world validation succeeded: {firstWorldPath}" );
			}
			catch ( Exception exception )
			{
				Log.Warning( $"Gothic startup world validation failed for '{firstWorldPath}': {exception}" );
			}

			try
			{
				var runtimeWorldSceneUri = mount.GetMountedResourceUri( mount.GetMountedWorldSceneResourcePath( firstWorldPath ) );
				var loadedRuntimeScene = SceneFile.Load( runtimeWorldSceneUri );
				Log.Info( $"Gothic startup world scene validation succeeded: RuntimeSceneUri={runtimeWorldSceneUri}; LoadedRuntimeScene={(loadedRuntimeScene is null ? "<null>" : loadedRuntimeScene.ResourcePath)}" );
			}
			catch ( Exception exception )
			{
				Log.Warning( $"Gothic startup world scene validation failed for '{firstWorldPath}': {exception}" );
			}
		}

		var firstTexturePath = mount.MountedTexturePaths.FirstOrDefault();
		if ( !string.IsNullOrWhiteSpace( firstTexturePath ) )
		{
			try
			{
				var mountedTexturePath = mount.GetEditorTextureAssetPath( firstTexturePath );
				var loadedTexture = Texture.Load( mountedTexturePath, false );
				Log.Info( $"Gothic startup texture path validation succeeded: {mountedTexturePath}; Loaded={(loadedTexture is null ? "<null>" : loadedTexture.GetType().FullName)}" );
			}
			catch ( Exception exception )
			{
				Log.Warning( $"Gothic startup texture path validation failed for '{firstTexturePath}': {exception}" );
			}
		}
	}
}
