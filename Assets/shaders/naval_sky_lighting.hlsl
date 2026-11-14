#ifndef NAVAL_SKY_LIGHTING_HLSL
#define NAVAL_SKY_LIGHTING_HLSL

float3 g_vSun < Attribute( "NavalSkySun" ); Default3( 0, -0.5, 0.866 ); >;
float g_flDay < Attribute( "NavalSkyDaylight" ); Default( 1 ); >;
float g_flCover < Attribute( "NavalSkyCover" ); Default( 0.38 ); >;
float g_flClock < Attribute( "NavalSkyTime" ); Default( 0 ); >;

float SkyHash( float2 point )
{
	float3 hash = frac( point.xyx * float3( 0.1031, 0.1030, 0.0973 ) );
	hash += dot( hash, hash.yzx + 33.33 );
	return frac( (hash.x + hash.y) * hash.z );
}

float SkyNoise( float2 p )
{
	float2 cell = floor( p );
	float2 f = frac( p );
	f = f * f * (3.0 - 2.0 * f);
	return lerp( lerp( SkyHash( cell ), SkyHash( cell + float2( 1, 0 ) ), f.x ),
		lerp( SkyHash( cell + float2( 0, 1 ) ), SkyHash( cell + 1.0 ), f.x ), f.y );
}

float2 SkyTurn( float2 p )
{
	return float2( 0.8 * p.x - 0.6 * p.y, 0.6 * p.x + 0.8 * p.y );
}

float SkyTwilight()
{
	return (1.0 - smoothstep( 0.025, 0.37, abs( g_vSun.z ) )) * (1.0 - 0.72 * g_flCover * g_flCover);
}

float3 NavalSkyHorizon( float3 ray )
{
	float storm = g_flCover * g_flCover;
	float sunward = saturate( dot( ray.xy, g_vSun.xy ) * 0.5 + 0.5 );
	float3 noon = lerp( float3( 0.39, 0.67, 0.78 ), float3( 0.16, 0.252, 0.29 ), storm );
	float3 horizon = lerp( float3( 0.014, 0.028, 0.059 ), noon, g_flDay );
	float3 dusk = lerp( float3( 0.31, 0.19, 0.27 ), float3( 0.99, 0.39, 0.145 ), sunward * sunward );
	return lerp( horizon, dusk, SkyTwilight() );
}

// Intersect a shallow, curved cloud layer. The denominator stays finite at sea level.
float2 CloudPlane( float3 ray )
{
	float elevation = max( ray.z, 0.0 );
	return ray.xy * (2.5 / (elevation + sqrt( elevation * elevation + 0.048 )))
		+ float2( 5.07, 13.35 ) + g_flClock * float2( -0.0027, 0.00085 );
}

// A slow-changing silhouette holds together as small billows travel through it.
float CloudBody( float2 p )
{
	float broad = SkyNoise( p * 0.72 );
	float cumulus = SkyNoise( SkyTurn( p ) * 1.60 + broad * 0.75 + 19.4 );
	float lobes = SkyNoise( p * 3.75 + cumulus * 1.15 - g_flClock * 0.00075 );
	return broad * 0.46 + cumulus * 0.34 + lobes * 0.20;
}

float CloudDensity( float2 p )
{
	float body = CloudBody( p );
	float fringe = SkyNoise( SkyTurn( p ) * 8.2 + body * 1.65 );
	float density = body + 0.085 * (fringe - 0.5);
	return density - lerp( 0.70, 0.35, g_flCover );
}

float NavalSunVisibility()
{
	float3 light = g_vSun.z >= 0.0 ? g_vSun : -g_vSun;
	float cloud = smoothstep( -0.018, 0.075, CloudDensity( CloudPlane( light ) ) );
	return (1.0 - 0.87 * cloud) * (1.0 - 0.42 * g_flCover * g_flCover);
}

float3 NavalSkyReflection( float3 ray, float roughness )
{
	float day = g_flDay;
	float storm = g_flCover * g_flCover;
	float z = max( ray.z, 0.0 );
	float dusk = SkyTwilight();
	float3 horizon = NavalSkyHorizon( ray );
	float3 zenith = lerp( float3( 0.018, 0.195, 0.51 ), float3( 0.044, 0.087, 0.137 ), storm );
	zenith = lerp( float3( 0.0026, 0.006, 0.02 ), zenith, day );
	float3 sky = lerp( horizon, zenith, pow( z, 0.52 ) );
	float sunDot = saturate( dot( ray, g_vSun ) );
	sky += lerp( float3( 0.34, 0.30, 0.21 ), float3( 0.7, 0.25, 0.075 ), dusk )
		* pow( sunDot, 16 ) * day * (1.0 - storm * 0.79);

	float2 p = CloudPlane( ray );
	float band = roughness * (0.082 + 0.065 / (0.32 + z));
	float body = CloudBody( p );
	float detail = 0.085 * (SkyNoise( SkyTurn( p ) * 8.2 + body * 1.65 ) - 0.5);
	float density = body + detail * (1.0 - roughness) - lerp( 0.70, 0.35, g_flCover );
	float alpha = smoothstep( -0.010 - band, 0.038 + band, density ) * smoothstep( -0.007, 0.085, ray.z );
	if ( alpha < 0.002 ) return sky;
	float3 light = g_vSun.z >= 0.0 ? g_vSun : -g_vSun;
	// Contrast the illuminated side of each bank with its deeper underside, tapering
	// contrast at the horizon where sunlight scatters through intervening atmosphere.
	float cast = CloudBody( p + light.xy * 0.24 ) - lerp( 0.70, 0.35, g_flCover );
	float side = saturate( 0.48 + (density - cast) * 4.4 - density * 1.15 );
	float rim = (1.0 - smoothstep( 0.015 - band, 0.092 + band, density )) * (1.0 - roughness * 0.7);
	float3 base = lerp( float3( 0.215, 0.322, 0.40 ), float3( 0.027, 0.055, 0.078 ), storm );
	float3 lit = lerp( float3( 0.96, 0.90, 0.77 ), float3( 0.35, 0.43, 0.46 ), storm );
	base = lerp( base, float3( 0.18, 0.115, 0.18 ), dusk );
	lit = lerp( lit, float3( 1.18, 0.465, 0.18 ), dusk );
	float3 clouds = lerp( base, lit, saturate( side + rim * (0.22 + 0.42 * sunDot) ) );
	clouds = lerp( float3( 0.010, 0.022, 0.042 ) + side * float3( 0.032, 0.046, 0.068 ), clouds, max( day, 0.36 * dusk ) );
	clouds = lerp( horizon, clouds, smoothstep( 0.0, 0.33, z ) * 0.77 + 0.23 );
	return lerp( sky, clouds, alpha );
}

float3 NavalSkyReflection( float3 ray )
{
	return NavalSkyReflection( ray, 0.0 );
}
#endif
