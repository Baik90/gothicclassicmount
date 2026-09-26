HEADER
{
	Description = "Gothic water with material tint, translucency and moving surface distortion";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_TRANSLUCENT, 0..1, "Rendering" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
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
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		return FinalizeVertex( i );
	}
}

PS
{
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( PC ) );
	DynamicCombo( D_RENDER_BACKFACES, 0..1, Sys( ALL ) );
	RenderState( CullMode, D_RENDER_BACKFACES ? NONE : BACK );

	#include "common/pixel.hlsl"

	SamplerState g_sSampler0 < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( Color, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tColor < Channel( RGBA, Box( Color ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;

	float4 WaterTint < UiType( Color ); Default4( 0.28, 0.36, 0.40, 0.78 ); >;
	float2 g_vFlowDirection < Default2( 0, 0 ); >;
	float g_flAnimationFps < Default1( 0 ); >;
	float g_bLinearFlow < Default1( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i );

		float2 uv = i.vTextureCoords.xy;
		float animationRate = max( g_flAnimationFps, 1.0 );
		float phase = g_flTime * animationRate * 0.25;
		float2 ripple = float2(
			sin( uv.y * 6.2831853 + phase ),
			cos( uv.x * 6.2831853 - phase * 0.73 ) ) * 0.01;
		float2 flow = g_bLinearFlow > 0.5
			? g_vFlowDirection * g_flTime
			: float2( g_flTime * animationRate * 0.003, -g_flTime * animationRate * 0.002 );
		float4 water = Tex2DS( g_tColor, g_sSampler0, uv + flow + ripple );

		m.Albedo = water.rgb * WaterTint.rgb;
		m.Normal = normalize( i.vNormalWs );
		m.TextureCoords = uv;
		m.Roughness = 0.8;
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.TintMask = 1;
		m.Opacity = saturate( water.a * WaterTint.a );
		m.Emission = 0;
		m.Transmission = 0;

		return ShadingModelStandard::Shade( i, m );
	}
}
