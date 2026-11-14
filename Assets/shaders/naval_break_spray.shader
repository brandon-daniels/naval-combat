HEADER
{
	Description = "Feathered spray from intersecting wave crests";
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
	float4 sprayColor : COLOR0 < Semantic( Color ); >;
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
		PixelInput o = ProcessVertex( i );
		o.vVertexColor = i.sprayColor;
		return FinalizeVertex( o );
	}
}

PS
{
	#include "common/pixel.hlsl"
	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
	BoolAttribute( translucent, true );
	float g_flLight < Attribute( "SprayLight" ); Default( 1 ); >;

	float SprayHash( float2 p )
	{
		float3 a = frac( p.xyx * float3( 0.1031, 0.1030, 0.0973 ) );
		a += dot( a, a.zxy + 33.33 );
		return frac( (a.x + a.y) * a.z );
	}
	float SprayNoise( float2 p )
	{
		float2 c = floor( p ), f = frac( p );
		f = f * f * (3 - 2 * f);
		return lerp( lerp( SprayHash( c ), SprayHash( c + float2( 1, 0 ) ), f.x ),
			lerp( SprayHash( c + float2( 0, 1 ) ), SprayHash( c + 1 ), f.x ), f.y );
	}
	float SprayCloud( float2 p )
	{
		float2 warp = float2( SprayNoise( p * 0.53 ), SprayNoise( p * 0.64 + 12.7 ) );
		float2 q = p + (warp - 0.5) * 1.7;
		return SprayNoise( q ) * 0.54 + SprayNoise( q * 2.17 + 13.0 ) * 0.31 + SprayNoise( q * 4.41 ) * 0.15;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float density;
		float froth;
		if ( uv.x < 1.5 )
		{
			float seed = i.vVertexColor.r * 53.7;
			float2 strands = float2( uv.x * 13 + uv.y * 1.55, uv.y * 2.8 + seed );
			float body = SprayCloud( strands );
			float cells = SprayNoise( strands * 2.85 + body * 2.6 );
			float rim = 1 - smoothstep( 0.035, 0.15, abs( cells - 0.48 ) );
			float lip = 0.12 + 0.90 * smoothstep( 0.28, 0.73, body + 0.14 * (cells - 0.5) );
			float veil = 1 - smoothstep( lip - 0.19, lip + 0.045, uv.y );
			float border = smoothstep( 0.0, 0.11, min( uv.x, 1 - uv.x ) );
			float holes = smoothstep( 0.31, 0.57, body + 0.12 * (cells - 0.5) );
			froth = saturate( 0.24 + 0.39 * holes + rim * 0.37 );
			density = border * veil * (0.08 + 0.54 * holes + 0.18 * rim) * smoothstep( 0.19, 0.40, body );
		}
		else if ( uv.x < 3.5 )
		{
			float x = (uv.x - 2.5) * 2.0;
			density = (1.0 - smoothstep( 0.07, 1.0, abs( x ) )) * 0.63;
			froth = 0.68;
		}
		else if ( uv.x < 5.5 )
		{
			float2 drop = float2( uv.x - 4.5, uv.y - 0.5 ) * 2.0;
			density = saturate( 1.0 - dot( drop, drop ) ) * 0.56;
			froth = 0.82;
		}
		else if ( uv.x < 7.5 )
		{
			float2 p = (float2( uv.x - 6, uv.y ) - 0.5) * 2;
			float seed = i.vVertexColor.r * 67.3;
			float phase = i.vVertexColor.g;
			float n = SprayCloud( p * 2.4 + float2( seed - phase * 0.59, seed * 0.31 + phase ) );
			float r = length( p * float2( 0.94, 1 ) ) + 0.77 * (0.51 - n);
			float haze = 1 - smoothstep( 0.20, 0.98, r );
			density = haze * smoothstep( 0.28, 0.72, n ) * 0.42;
			froth = saturate( 0.40 + n * 0.50 );
		}
		else
		{
			float2 p = (float2( uv.x - 8, uv.y ) - 0.5) * 2;
			float2 q = p * float2( 5.6, 2.5 ) + float2( i.vVertexColor.r * 48, i.vVertexColor.g * 0.85 );
			float turbulence = SprayCloud( q );
			float r = length( p + float2( 0, sin( p.x * 3.2 ) * 0.16 ) ) + (0.52 - turbulence) * 0.85;
			float edge = 1 - smoothstep( 0.46, 1, r );
			float bubbles = SprayNoise( q * 3.2 + turbulence * 2.4 );
			float lace = 1 - smoothstep( 0.015, 0.14, abs( bubbles - 0.51 ) );
			float aeration = smoothstep( 0.30, 0.71, turbulence ) * (0.37 + 0.63 * lace);
			density = edge * aeration * 0.87;
			froth = saturate( 0.53 + 0.47 * lace );
		}
		float3 day = lerp( float3( 0.20, 0.42, 0.46 ), float3( 0.72, 0.85, 0.85 ), froth );
		float3 night = lerp( float3( 0.027, 0.049, 0.070 ), float3( 0.10, 0.16, 0.22 ), froth );
		return float4( lerp( night, day, g_flLight ), density * i.vVertexColor.a );
	}
}
