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
			// Register an empty template, without queuing component callbacks in a
			// temporary scene while the real scene is being deserialized.
			new GameObject( false, System.IO.Path.GetFileNameWithoutExtension( sourcePath ) );
		}
		_prefab = builder.Create();
		// PrefabBuilder.Create randomizes all IDs. Assign the stable source JSON
		// AFTER Create so saved instance patches keep resolving after a remount.
		_prefab.RootObject = new JsonObject
			{
				["__guid"] = JsonValue.Create( StableId( "root" ) ),
				["Name"] = System.IO.Path.GetFileNameWithoutExtension( sourcePath ),
				["Enabled"] = true,
				["Components"] = new JsonArray( new JsonObject
				{
					["__type"] = "Sandbox.ModelRenderer",
					["__guid"] = JsonValue.Create( StableId( "renderer" ) ),
					["__enabled"] = true,
					["Model"] = Host.GetMountedResourceUri( Host.GetMountedModelResourcePath( sourcePath ) )
				}, new JsonObject
				{
					["__type"] = "Sandbox.ModelCollider",
					["__guid"] = JsonValue.Create( StableId( "collider" ) ),
					["__enabled"] = true,
					["Static"] = true,
					["Model"] = Host.GetMountedResourceUri( Host.GetMountedModelResourcePath( sourcePath ) )
				} )
			};
		return _prefab;
	}

	private Guid StableId( string member ) => new( SHA256.HashData( Encoding.UTF8.GetBytes( $"gothicclassic:{sourcePath.ToLowerInvariant()}:{member}" ) ).AsSpan( 0, 16 ) );

	protected override void Shutdown()
	{
		if ( _prefab is not null ) PrefabBuilder.Destroy( _prefab );
		_prefab = null;
	}
}
