using System;
using Sandbox.Mounting;

namespace GothicClassicMount;

internal sealed class GothicWorldResource : ResourceLoader<GothicClassicMount>
{
	public GothicWorldResource( string virtualPath )
	{
		SourcePath = virtualPath;
	}

	public string SourcePath { get; }

	protected override object Load()
	{
		try
		{
			return GothicGeometryBuilder.BuildWorldModel( Host, SourcePath, Path );
		}
		catch ( Exception exception )
		{
			Log.Warning( $"Failed to load Gothic world resource '{SourcePath}': {exception}" );
			throw;
		}
	}
}
