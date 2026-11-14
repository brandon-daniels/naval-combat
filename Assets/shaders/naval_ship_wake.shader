HEADER
{
	Description = "Waterline creases and bubble trails left by moving hulls";
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
	float4 wakeColor : COLOR0 < Semantic( Color ); >;
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
		o.vVertexColor = i.wakeColor;
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
	float g_flWakeTime < Attribute( "WakeTime" ); Default( 0 ); >;
	float g_flLight < Attribute( "WakeLight" ); Default( 1 ); >;

	float Hash( float2 p )
	{
		float3 v = frac( p.xyx * float3( 0.1031, 0.1030, 0.0973 ) );
		v += dot( v, v.yxz + 19.19 );
		return frac( (v.x + v.y) * v.z );
	}
	float Noise( float2 p )
	{
		float2 cell = floor( p ), f = frac( p );
		f = f * f * (3.0 - 2.0 * f);
		return lerp( lerp( Hash( cell ), Hash( cell + float2(1,0) ), f.x ),
			lerp( Hash( cell + float2(0,1) ), Hash( cell + 1 ), f.x ), f.y );
	}
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float2 world = (i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz).xy;
		float tm = g_flWakeTime;
		float2 heading = i.vVertexColor.rg * 2 - 1;
		heading *= rsqrt( max( dot( heading, heading ), 0.01 ) );
		float2 p = world * 0.032;
		float2 warp = float2( Noise( p * 0.39 + tm * 0.10 ), Noise( p * 0.36 - tm * 0.08 + 12 ) );
		float2 q = p + warp * 2.9 + tm * 0.19 * heading;
		float clump = Noise( q ) * 0.60 + Noise( q * 2.27 + warp * 1.6 ) * 0.40;
		float2 grains = q * 4.45 + 2.4 * warp;
		float flurry = Noise( grains ) * 0.46 + Noise( grains + heading * 0.86 ) * 0.31
			+ Noise( grains + heading * 1.84 ) * 0.23;
		float fine = Noise( grains * 2.8 + 7.6 );
		float flecks = smoothstep( 0.39, 0.77, flurry + (fine - 0.5) * 0.38 );
		float across = abs( uv.x );
		float coverage;
		float froth;
		if ( uv.y > 1.5 )
		{
			float roll = 1 - smoothstep( 0.02, 0.86, abs( uv.x + 0.14 + (clump - 0.5) * 0.84 ) );
			float breakage = smoothstep( 0.25, 0.68, clump + (flecks - 0.5) * 0.24 );
			froth = saturate( 0.22 + 0.44 * flurry + 0.35 * fine );
			coverage = roll * breakage * (0.18 + 0.82 * flecks) * (1 - smoothstep( 0.70, 0.99, across ));
		}
		else
		{
			float age = saturate( uv.y );
			float shoulder = 0.37 + 0.33 * smoothstep( 0.0, 0.55, age ) + (warp.x - 0.5) * 0.21;
			float flare = 1 - smoothstep( 0.01, 0.34, abs( across - shoulder ) );
			float seam = 1 - smoothstep( 0.015, 0.15, abs( across - shoulder * 0.62 + (clump - 0.5) * 0.25 ) );
			float wash = (1 - smoothstep( 0.02, 0.38, age )) * (1 - smoothstep( 0.25, 0.99, across ));
			float plume = smoothstep( 0.29, 0.67, clump + flare * 0.12 + wash * 0.085 - age * 0.12 );
			float edge = 1 - smoothstep( 0.65 + 0.15 * warp.y, 0.99, across );
			froth = saturate( 0.22 + 0.32 * flurry + 0.35 * flecks + 0.16 * wash );
			coverage = edge * plume * (0.08 + 0.29 * flare + 0.14 * seam + 0.40 * wash) * (0.22 + 0.92 * flecks);
		}
		float3 day = lerp( float3( 0.12, 0.36, 0.38 ), float3( 0.85, 0.92, 0.87 ), froth );
		float3 night = lerp( float3( 0.028, 0.054, 0.078 ), float3( 0.18, 0.24, 0.27 ), froth );
		return float4( lerp( night, day, g_flLight ), coverage * i.vVertexColor.a );
	}
}
