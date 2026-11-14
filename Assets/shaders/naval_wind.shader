HEADER
{
	Description = "Translucent sea haze carried by sailing wind";
	Version = 1;
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
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
	PixelInput MainVs( VertexInput i )
	{
		return FinalizeVertex( ProcessVertex( i ) );
	}
}

PS
{
	#include "common/pixel.hlsl"
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
	BoolAttribute( translucent, true );
	float g_flDay < Attribute( "NavalSkyDaylight" ); Default( 1 ); >;
	float g_flCover < Attribute( "NavalSkyCover" ); Default( 0.38 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float edge = saturate( 1.0 - abs( uv.y * 2.0 - 1.0 ) );
		float ends = saturate( uv.x * (1 - uv.x) * 4.0 );
		float strength = lerp( 0.045, 0.17, g_flCover * g_flCover );
		float3 mist = lerp( float3( 0.10, 0.17, 0.24 ), float3( 0.70, 0.83, 0.84 ), g_flDay );
		return float4( mist, strength * edge * ends * ends );
	}
}
