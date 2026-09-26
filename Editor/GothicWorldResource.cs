using System;
using Sandbox.Mounting;

namespace GothicClassicMount;

internal sealed class GothicWorldResource : ResourceLoader<GothicClassicMount>
{
	public GothicWorldResource( string virtualPath, bool includeTrees = true )
	{
		SourcePath = virtualPath;
		IncludeTrees = includeTrees;
	}

	public string SourcePath { get; }
	private bool IncludeTrees { get; }

	protected override object Load()
	{
		try
		{
			return GothicGeometryBuilder.BuildWorldModel( Host, SourcePath, Path, includeTrees: IncludeTrees );
		}
		catch ( Exception exception )
		{
			Log.Warning( $"Failed to load Gothic world resource '{SourcePath}': {exception}" );
			throw;
		}
	}
}
