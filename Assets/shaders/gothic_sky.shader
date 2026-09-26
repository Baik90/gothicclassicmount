HEADER
{
	Description = "Gothic colony sky with original cloud layers and animated magic barrier";
}

FEATURES
{
	#include "vr_common_features.fxc"
}

MODES
{
	Forward();
}

COMMON
{
	#include "system.fxc"
	#include "vr_common.fxc"
}

struct VS_INPUT
{
	float4 vPositionOs : POSITION < Semantic( PosXyz ); >;
};

struct PS_INPUT
{
	float3 vDirection : TEXCOORD0;
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif
	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
};

VS
{
	#define IS_SPRITECARD 1
	#include "system.fxc"

	PS_INPUT MainVs( VS_INPUT i )
	{
		PS_INPUT o;
		o.vDirection = i.vPositionOs.xyz;
		float3 position = g_vCameraPositionWs.xyz + i.vPositionOs.xyz * (g_flNearPlane + g_flFarPlane);
		o.vPositionPs = Position3WsToPs( position );
		// Reversed depth: draw only behind scene geometry, regardless of world size.
		o.vPositionPs.z = 0;
		return o;
	}
}

PS
{
	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, false );
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	BoolAttribute( sky, true );

	SamplerState g_sCloudSampler < Filter( Anisotropic ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( SkyLayer, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 0.3, 0.35, 0.4, 1 ) );
	CreateInputTexture2D( CloudLayer, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 0, 0, 0, 0 ) );
	Texture2D g_tSkyLayer < Channel( RGBA, Box( SkyLayer ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	Texture2D g_tCloudLayer < Channel( RGBA, Box( CloudLayer ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	float g_flCloudSpeed < UiGroup( "Clouds" ); Default( 1 ); Range( 0, 5 ); >;

	float4 MainPs( PS_INPUT i ) : SV_Target0
	{
		float3 direction = normalize( i.vDirection );
		float elevation = saturate( direction.z );
		// Planar projection onto a shallow dome avoids a seam and a pinched pole overhead.
		float2 dome = direction.xy / (0.55 + elevation);
		float time = g_flTime;
		float2 drift = frac( time * g_flCloudSpeed * float2( 0.0015, 0.0007 ) );
		float3 sky = g_tSkyLayer.Sample( g_sCloudSampler, dome * 1.35 + drift ).rgb;
		float2 cloudDrift = frac( time * g_flCloudSpeed * float2( -0.0024, -0.00112 ) );
		float4 clouds = g_tCloudLayer.Sample( g_sCloudSampler, dome * 1.8 + cloudDrift + 0.37 );
		sky = lerp( sky, clouds.rgb, clouds.a * 0.6 );
		float horizon = smoothstep( 0.0, 0.3, elevation );
		sky = lerp( float3( 0.25, 0.30, 0.36 ), sky, horizon );

		return float4( sky, 1 );
	}
}
