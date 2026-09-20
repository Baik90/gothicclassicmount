using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Sandbox.Mounting;

namespace GothicClassicMount;

internal sealed class GothicPrefabResource( string sourcePath ) : ResourceLoader<GothicClassicMount>
{
	private PrefabFile _prefab;

	protected override object Load()
	{
		var builder = new PrefabBuilder().WithName( Path );
		using ( builder.Scope() )
		{
			var root = new GameObject( System.IO.Path.GetFileNameWithoutExtension( sourcePath ) );
			// Stable source IDs keep saved instance overrides valid after remounting.
			root.Deserialize( new JsonObject
			{
				["__guid"] = StableId( "root" ).ToString(),
				["Name"] = System.IO.Path.GetFileNameWithoutExtension( sourcePath ),
				["Enabled"] = true,
				["Components"] = new JsonArray( new JsonObject
				{
					["__type"] = "Sandbox.ModelRenderer",
					["__guid"] = StableId( "renderer" ).ToString(),
					["__enabled"] = true,
					["Model"] = Host.GetMountedResourceUri( Host.GetMountedModelResourcePath( sourcePath ) )
				}, new JsonObject
				{
					["__type"] = "Sandbox.ModelCollider",
					["__guid"] = StableId( "collider" ).ToString(),
					["__enabled"] = true,
					["Static"] = true,
					["Model"] = Host.GetMountedResourceUri( Host.GetMountedModelResourcePath( sourcePath ) )
				} )
			} );
		}
		_prefab = builder.Create();
		return _prefab;
	}

	private Guid StableId( string member ) => new( SHA256.HashData( Encoding.UTF8.GetBytes( $"gothicclassic:{sourcePath.ToLowerInvariant()}:{member}" ) ).AsSpan( 0, 16 ) );

	protected override void Shutdown()
	{
		if ( _prefab is not null ) PrefabBuilder.Destroy( _prefab );
		_prefab = null;
	}
}
