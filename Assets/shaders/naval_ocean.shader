HEADER
{
	Description = "Cel shaded ocean with animated crest ribbons";
	Version = 1;
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth();
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
	float g_flSeaLevel < Attribute( "OceanSeaLevel" ); Default( 0 ); >;
	float g_flOceanTime < Attribute( "OceanTime" ); Default( 0 ); >;
	float g_flWaveHeight < Attribute( "OceanWaveHeight" ); Default( 55 ); >;
	float g_flFoamStrength < Attribute( "OceanFoamStrength" ); Default( 1 ); >;

	float4 g_vHoldBounds0 < Attribute( "HoldBounds0" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHoldAxis0 < Attribute( "HoldAxis0" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHoldBounds1 < Attribute( "HoldBounds1" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHoldAxis1 < Attribute( "HoldAxis1" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHoldBounds2 < Attribute( "HoldBounds2" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHoldAxis2 < Attribute( "HoldAxis2" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHoldBounds3 < Attribute( "HoldBounds3" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHoldAxis3 < Attribute( "HoldAxis3" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHoldBounds4 < Attribute( "HoldBounds4" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHoldAxis4 < Attribute( "HoldAxis4" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHoldBounds5 < Attribute( "HoldBounds5" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHoldAxis5 < Attribute( "HoldAxis5" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHoldBounds6 < Attribute( "HoldBounds6" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHoldAxis6 < Attribute( "HoldAxis6" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHoldBounds7 < Attribute( "HoldBounds7" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHoldAxis7 < Attribute( "HoldAxis7" ); Default4( 0, 0, 0, 0 ); >;

	void ClipHold( float2 p, float height, float4 bounds, float4 axis )
	{
		float2 delta = p - bounds.xy;
		float along = abs( dot( delta, axis.xy ) );
		float across = abs( dot( delta, float2( -axis.y, axis.x ) ) );
		if ( bounds.z > 0 && along < bounds.z && across < bounds.w && height > axis.z && height < axis.w ) discard;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		// Absolute world coordinates keep color and foam fixed to the waves as the grid follows the ship.
		float2 p = i.vTextureCoords.xy * 512.0;
		float t = g_flOceanTime;
		// Keep these phases in step with ArcadeOcean.SampleWave.
		float a = dot( p, float2( 0.0026, 0.0012 ) ) + t * 0.85;
		float b = dot( p, float2( -0.0018, 0.0042 ) ) + t * 1.15;
		float c = dot( p, float2( 0.007, -0.003 ) ) + t * 1.8;
		float swell = sin( a ) - 0.12 * cos( 2.0 * a ) + 0.45 * sin( b ) + 0.08 * sin( c );
		ClipHold( p, g_flSeaLevel + g_flWaveHeight * swell, g_vHoldBounds0, g_vHoldAxis0 );
		ClipHold( p, g_flSeaLevel + g_flWaveHeight * swell, g_vHoldBounds1, g_vHoldAxis1 );
		ClipHold( p, g_flSeaLevel + g_flWaveHeight * swell, g_vHoldBounds2, g_vHoldAxis2 );
		ClipHold( p, g_flSeaLevel + g_flWaveHeight * swell, g_vHoldBounds3, g_vHoldAxis3 );
		ClipHold( p, g_flSeaLevel + g_flWaveHeight * swell, g_vHoldBounds4, g_vHoldAxis4 );
		ClipHold( p, g_flSeaLevel + g_flWaveHeight * swell, g_vHoldBounds5, g_vHoldAxis5 );
		ClipHold( p, g_flSeaLevel + g_flWaveHeight * swell, g_vHoldBounds6, g_vHoldAxis6 );
		ClipHold( p, g_flSeaLevel + g_flWaveHeight * swell, g_vHoldBounds7, g_vHoldAxis7 );
		float2 slope = g_flWaveHeight * (
			float2( 0.0026, 0.0012 ) * (cos( a ) + 0.24 * sin( 2.0 * a ))
			+ float2( -0.0018, 0.0042 ) * 0.45 * cos( b )
			+ float2( 0.007, -0.003 ) * 0.08 * cos( c ) );
		float3 normal = normalize( float3( -slope, 1.0 ) );
		// Broad blue color fields keep the sea graphic and readable.
		float tone = dot( normal, normalize( float3( -0.4, -0.25, 0.8 ) ) ) + swell * 0.10;
		float edge = max( fwidth( tone ), 0.012 );
		float3 color = float3( 0.012, 0.13, 0.48 );
		color = lerp( color, float3( 0.018, 0.24, 0.67 ), smoothstep( 0.80 - edge, 0.80 + edge, tone ) );
		color = lerp( color, float3( 0.025, 0.35, 0.75 ), smoothstep( 1.03 - edge, 1.03 + edge, tone ) );

		// Warped cellular outlines form the stylized, drifting white sea pattern.
		float2 q = p / 370.0 + float2( t * 0.022, -t * 0.016 );
		q += float2( sin( q.y * 3.1 + t * 0.18 ), cos( q.x * 2.8 - t * 0.15 ) ) * 0.24;
		float2 cell = floor( q );
		float2 f = frac( q );
		float nearest = 10.0;
		float second = 10.0;
		for ( int y = -1; y <= 1; y++ )
		for ( int x = -1; x <= 1; x++ )
		{
			float2 offset = float2( x, y );
			float2 id = cell + offset;
			float2 seed = frac( sin( float2( dot( id, float2( 127.1, 311.7 ) ), dot( id, float2( 269.5, 183.3 ) ) ) ) * 43758.5453 );
			float2 delta = offset + 0.5 + 0.28 * sin( seed * 6.283 + t * 0.12 ) - f;
			float distance = length( delta );
			if ( distance < nearest ) { second = nearest; nearest = distance; }
			else second = min( second, distance );
		}
		float boundary = second - nearest;
		float lineWidth = 0.025 + 0.014 * sin( q.x * 2.0 + q.y * 2.7 + t * 0.25 );
		float aa = max( fwidth( boundary ), 0.004 );
		float lace = 1.0 - smoothstep( lineWidth, lineWidth + aa, boundary );
		// Leave open gaps so the pattern reads as foam patches rather than a wire grid.
		float patches = smoothstep( 0.05, 0.55, sin( p.x * 0.003 + p.y * 0.004 + sin( b ) ) );
		float ridge = abs( cos( a + 0.10 * sin( b + t * 0.13 ) ) );
		float crest = (1.0 - smoothstep( 0.035, 0.075 + fwidth( ridge ), ridge ))
			* smoothstep( 0.8, 0.98, sin( a ) ) * smoothstep( -0.2, 0.4, sin( b * 1.5 ) );
		float distanceToCamera = length( i.vPositionWithOffsetWs );
		float detailFade = 1.0 - smoothstep( 2400.0, 7500.0, distanceToCamera );
		float foam = saturate( (lace * patches * detailFade + crest) * g_flFoamStrength );
		foam *= 1.0 - smoothstep( 6500.0, 13000.0, distanceToCamera );
		color = lerp( color, float3( 0.91, 0.98, 1.0 ), foam );
		color = lerp( color, float3( 0.13, 0.38, 0.66 ), smoothstep( 4500.0, 16000.0, distanceToCamera ) * 0.65 );
		return float4( color, 1.0 );
	}
}

