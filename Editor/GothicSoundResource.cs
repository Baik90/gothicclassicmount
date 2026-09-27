using System;
using Sandbox.Mounting;

namespace GothicClassicMount;

/// <summary>Loads the original WAV bytes on demand, including archived speech.</summary>
internal sealed class GothicSoundResource : ResourceLoader<GothicClassicMount>
{
	public string SourcePath { get; }

	public GothicSoundResource( string sourcePath ) => SourcePath = sourcePath;

	protected override object Load()
	{
		try
		{
			var node = ZenKitRuntime.ResolveNode( Host.RequireVfs(), SourcePath );
			var buffer = ZenKitRuntime.GetProperty( node, "Buffer" );
			var bytes = ZenKitRuntime.GetProperty( buffer, "Bytes" ) as byte[];
			if ( bytes is null || bytes.Length == 0 )
				throw new InvalidOperationException( "Sound file is empty or unavailable." );
			return SoundFile.FromWav( Path, GothicWaveDecoder.ToPcm( bytes ) );
		}
		catch ( Exception exception )
		{
			Log.Warning( $"Failed to load Gothic sound '{SourcePath}': {exception.Message}" );
			throw;
		}
	}
}
