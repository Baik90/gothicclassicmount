
HEADER
{
	Description = "Gothic world and model surfaces with texture alpha cutout";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Rendering" );
}

MODES
{
    Forward();
    Depth( S_MODE_DEPTH );
}

COMMON
{
	#include "common/shared.hlsl"
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
	// Advertise cutouts to the renderer as well as clipping in the pixel shader.
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	#include "common/pixel.hlsl"
	
	SamplerState g_sSampler0 < Filter( Anisotropic ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( Color, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tColor < Channel( RGBA, Box( Color ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i );

		float4 color = Tex2DS( g_tColor, g_sSampler0, i.vTextureCoords.xy );
        clip( color.a - 0.5 );
        m.Albedo = color.rgb;
		m.Normal = normalize( i.vNormalWs );
		m.TextureCoords = i.vTextureCoords.xy;
		m.Roughness = 1;
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.TintMask = 1;
		m.Opacity = color.a;
		m.Emission = 0;
		m.Transmission = 0;

		return ShadingModelStandard::Shade( i, m );
	}
}

