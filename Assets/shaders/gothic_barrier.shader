
HEADER
{
	Description = "";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth();
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#ifndef S_ALPHA_TEST
	#define S_ALPHA_TEST 0
	#endif
	#ifndef S_TRANSLUCENT
	#define S_TRANSLUCENT 1
	#endif
	
	#include "common/shared.hlsl"
	#include "procedural.hlsl"

	#define S_UV2 1
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float3 vPositionOs : TEXCOORD14;
	float3 vNormalOs : TEXCOORD15;
	float4 vTangentUOs_flTangentVSign : TANGENT	< Semantic( TangentU_SignV ); >;
	float4 vColor : COLOR0;
	float4 vTintColor : COLOR1;
	#if ( PROGRAM == VFX_PROGRAM_PS )
		bool vFrontFacing : SV_IsFrontFace;
	#endif
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		
		PixelInput i = ProcessVertex( v );
		i.vPositionOs = v.vPositionOs.xyz;
		i.vColor = v.vColor;
		
		ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( v.nInstanceTransformID );
		i.vTintColor = extraShaderData.vTint;
		
		VS_DecodeObjectSpaceNormalAndTangent( v, i.vNormalOs, i.vTangentUOs_flTangentVSign );
		return FinalizeVertex( i );
		
	}
}

PS
{
	#include "common/pixel.hlsl"
	RenderState( CullMode, F_RENDER_BACKFACES ? NONE : DEFAULT );
		
	SamplerState g_sSampler0 < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( barrier_tex1, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	CreateInputTexture2D( barrier_tex2, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tbarrier_tex1 < Channel( RGBA, Box( barrier_tex1 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	Texture2D g_tbarrier_tex2 < Channel( RGBA, Box( barrier_tex2 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	float g_flFreq < UiGroup( ",0/,0/0" ); Default1( 12.566371 ); Range1( 0, 50 ); >;
	float g_flOpacity < UiGroup( ",0/,0/0" ); Default1( 1.0 ); Range1( 0, 1 ); >;
	float g_flAmp < UiGroup( ",0/,0/0" ); Default1( 0.06999996 ); Range1( 0, 1 ); >;

	float BarrierNoiseHash( float3 p )
	{
		p = frac( p * 0.1031 );
		p += dot( p, p.yzx + 33.33 );
		return frac( (p.x + p.y) * p.z );
	}

	float BarrierValueNoise( float3 p )
	{
		float3 cell = floor( p );
		float3 f = frac( p );
		f = f * f * (3.0 - 2.0 * f);
		float n000 = BarrierNoiseHash( cell + float3( 0, 0, 0 ) );
		float n100 = BarrierNoiseHash( cell + float3( 1, 0, 0 ) );
		float n010 = BarrierNoiseHash( cell + float3( 0, 1, 0 ) );
		float n110 = BarrierNoiseHash( cell + float3( 1, 1, 0 ) );
		float n001 = BarrierNoiseHash( cell + float3( 0, 0, 1 ) );
		float n101 = BarrierNoiseHash( cell + float3( 1, 0, 1 ) );
		float n011 = BarrierNoiseHash( cell + float3( 0, 1, 1 ) );
		float n111 = BarrierNoiseHash( cell + float3( 1, 1, 1 ) );
		float lower = lerp( lerp( n000, n100, f.x ), lerp( n010, n110, f.x ), f.y );
		float upper = lerp( lerp( n001, n101, f.x ), lerp( n011, n111, f.x ), f.y );
		return lerp( lower, upper, f.z );
	}
	
	float4 MainPs( PixelInput i ) : SV_Target0
	{

		
		float l_0 = g_flFreq;
		float l_1 = 0.0f;
		float l_2 = l_0 * l_1;
		float l_3 = g_flTime + l_2;
		float l_4 = cos( l_3 );
		float l_5 = g_flAmp;
		float l_6 = l_4 * l_5;
		float2 l_7 = i.vTextureCoords.xy * float2( 1, 1 );
		float2 l_8 = l_7 * float2( 5, 5 );
		float l_9 = l_8.x;
		float l_10 = l_0 * l_9;
		float l_11 = g_flTime + l_10;
		float l_12 = sin( l_11 );
		float l_13 = l_12 * l_5;
		float2 l_14 = float2( l_6, l_13);
		float2 l_15 = l_14 + l_8;
		float l_16 = g_flTime * 0.059999995;
		float l_17 = l_16.x;
		float2 l_18 = TileAndOffsetUv( i.vTextureCoords.xy, float2( 1, 1 ), float2( l_17, l_17 ) );
		float2 l_19 = l_15 + l_18;
		float4 l_20 = Tex2DS( g_tbarrier_tex1, g_sSampler0, l_19 );
		float l_21 = g_flTime * -0.059999995;
		float l_22 = l_21.x;
		float2 l_23 = TileAndOffsetUv( i.vTextureCoords.xy, float2( -1, -1 ), float2( l_22, l_22 ) );
		float2 l_24 = l_15 + l_23;
		float4 l_25 = Tex2DS( g_tbarrier_tex2, g_sSampler0, l_24 );
		float4 l_26 = saturate( lerp( l_20, l_25, 0.5 ) );
		float l_27 = l_26.z;
		float barrierAngle = i.vTextureCoords.x * 6.28318530718;
		float3 noiseCoords = float3( cos( barrierAngle ), sin( barrierAngle ), i.vTextureCoords.y * 2.0 ) * 2.5;
		noiseCoords += float3( g_flTime * 0.05, g_flTime * 0.035, -g_flTime * 0.025 );
		float noiseOpacity = 0.35 + BarrierValueNoise( noiseCoords ) * 0.65;
		

		return float4( l_26.xyz, l_27 * noiseOpacity * g_flOpacity );
	}
}
