using System;
using System.IO;
using Sandbox.Mounting;

namespace GothicClassicMount;

internal sealed class GothicWorldSceneResource : SceneLoader<GothicClassicMount>
{
	public GothicWorldSceneResource( string virtualPath )
	{
		SourcePath = virtualPath;
	}

	public string SourcePath { get; }

	protected override void BuildScene()
	{
		try
		{
			var model = Model.Load( System.IO.Path.ChangeExtension( Path, ".scene_mesh.vmdl" ) );
			var sceneRoot = new GameObject( System.IO.Path.GetFileNameWithoutExtension( SourcePath ) ?? "GothicWorld" );
			var renderer = sceneRoot.Components.Create<ModelRenderer>();
			renderer.Model = model;
			var collider = sceneRoot.Components.Create<ModelCollider>();
			collider.Model = model;
			collider.Static = true;
			GothicWorldSky.Populate( Host, SourcePath, sceneRoot );
			GothicWorldObjects.Populate( Host, SourcePath, sceneRoot );
		}
		catch ( Exception exception )
		{
			Log.Warning( $"Failed to build Gothic world scene '{SourcePath}': {exception}" );
			throw;
		}
	}
}
