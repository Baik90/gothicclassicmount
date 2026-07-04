using System;
using Sandbox.Mounting;

namespace GothicClassicMount;

internal sealed class GothicModelResource : ResourceLoader<GothicClassicMount>
{
	public GothicModelResource( string virtualPath )
	{
		SourcePath = virtualPath;
	}

	public string SourcePath { get; }

	protected override object Load()
	{
		try
		{
			return GothicGeometryBuilder.BuildModel( Host, SourcePath, Path );
		}
		catch ( Exception exception )
		{
			Log.Warning( $"Failed to load Gothic model resource '{SourcePath}': {exception}" );
			throw;
		}
	}
}
