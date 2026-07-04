using System;
using Sandbox.Mounting;

namespace GothicClassicMount;

internal sealed class GothicTextureResource : ResourceLoader<GothicClassicMount>
{
	public GothicTextureResource( string virtualPath )
	{
		SourcePath = virtualPath;
	}

	public string SourcePath { get; }

	protected override object Load()
	{
		try
		{
			return Host.LoadTexture( SourcePath, Path );
		}
		catch ( Exception exception )
		{
			Log.Warning( $"Failed to load Gothic texture resource '{SourcePath}': {exception}" );
			return Texture.White;
		}
	}
}
