HEADER
{
	Description = "Maritime sky with drifting clouds, twilight, sun and moon";
	Version = 1;
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
	float4 position : POSITION < Semantic( PosXyz ); >;
};

struct PS_INPUT
{
	float3 ray : TEXCOORD0;
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif
	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
};

VS
{
	PS_INPUT MainVs( VS_INPUT input )
	{
		PS_INPUT output;
		output.ray = input.position.xyz;
		float3 worldPosition = g_vCameraPositionWs + output.ray * (g_flNearPlane + g_flFarPlane);
		output.vPositionPs = Position3WsToPs( worldPosition );
		return output;
	}
}

PS
{
	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, false );
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	BoolAttribute( sky, true );

	#include "naval_sky_lighting.hlsl"

	float4 MainPs( PS_INPUT input ) : SV_Target0
	{
		float3 ray = normalize( input.ray );
		float3 sky = NavalSkyReflection( ray );
		float2 starUv = ray.xy / (1.0 + saturate( ray.z )) * 165.0;
		float2 cell = floor( starUv );
		float2 center = 0.25 + 0.50 * float2( SkyHash( cell ), SkyHash( cell + 47.7 ) );
		float radius = length( frac( starUv ) - center );
		float star = (1.0 - smoothstep( 0.045, 0.073 + fwidth( radius ), radius ))
			* step( 0.983, SkyHash( cell + 18.2 ) ) * (1.0 - g_flDay) * smoothstep( 0.06, 0.26, ray.z );
		float solar = dot( ray, g_vSun );
		float sun = smoothstep( 0.99973, 0.99984, solar );
		float moon = smoothstep( 0.99975, 0.99983, -solar ) + 0.028 * pow( saturate( -solar ), 82 );
		if ( sun + moon + star > 0.002 )
		{
			float clear = 1.0 - smoothstep( -0.015, 0.073, CloudDensity( CloudPlane( ray ) ) );
			float3 direct = lerp( float3( 2.4, 2.15, 1.62 ), float3( 2.0, 0.94, 0.33 ), SkyTwilight() ) * sun;
			sky += clear * (star * 0.72 + (direct + moon * float3( 0.55, 0.70, 0.92 )) * smoothstep( 0, 0.024, ray.z ));
		}
		return float4( sky, 1.0 );
	}
}
