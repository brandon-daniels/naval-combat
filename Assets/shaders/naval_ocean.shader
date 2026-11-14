HEADER
{
	Description = "Directional ocean swells, generated island shallows and breaking whitewater";
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
	#include "naval_sky_lighting.hlsl"
	#include "naval_shoals.hlsl"
	CreateInputTexture2D( WaveletSlopes, Linear, 8, "", "_slopes", "Water", Default3( 0.5, 0.5, 0.5 ) );
	Texture2D g_tWaveletSlopes < Channel( RGB, Box( WaveletSlopes ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	float g_flSeaLevel < Attribute( "OceanSeaLevel" ); Default( 0 ); >;
	float g_flOceanTime < Attribute( "OceanTime" ); Default( 0 ); >;
	float g_flWaveHeight < Attribute( "OceanWaveHeight" ); Default( 55 ); >;
	float g_flFoamStrength < Attribute( "OceanFoamStrength" ); Default( 1 ); >;
	float g_flDaylight < Attribute( "OceanDaylight" ); Default( 1 ); >;
	float g_flStorminess < Attribute( "OceanStorminess" ); Default( 0 ); >;
	float3 g_vLightDirection < Attribute( "OceanLightDirection" ); Default3( -0.4, -0.25, 0.8 ); >;
	float2 g_vTravelDirection < Attribute( "OceanTravelDirection" ); Default2( 1, 0 ); >;
	float g_flHasBreakers < Attribute( "OceanHasBreakers" ); Default( 0 ); >;

	float4 g_vHullLocalX0 < Attribute( "HullLocalX0" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalY0 < Attribute( "HullLocalY0" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalZ0 < Attribute( "HullLocalZ0" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalX1 < Attribute( "HullLocalX1" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalY1 < Attribute( "HullLocalY1" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalZ1 < Attribute( "HullLocalZ1" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalX2 < Attribute( "HullLocalX2" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalY2 < Attribute( "HullLocalY2" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalZ2 < Attribute( "HullLocalZ2" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalX3 < Attribute( "HullLocalX3" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalY3 < Attribute( "HullLocalY3" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalZ3 < Attribute( "HullLocalZ3" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalX4 < Attribute( "HullLocalX4" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalY4 < Attribute( "HullLocalY4" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalZ4 < Attribute( "HullLocalZ4" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalX5 < Attribute( "HullLocalX5" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalY5 < Attribute( "HullLocalY5" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalZ5 < Attribute( "HullLocalZ5" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalX6 < Attribute( "HullLocalX6" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalY6 < Attribute( "HullLocalY6" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalZ6 < Attribute( "HullLocalZ6" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalX7 < Attribute( "HullLocalX7" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalY7 < Attribute( "HullLocalY7" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vHullLocalZ7 < Attribute( "HullLocalZ7" ); Default4( 0, 0, 0, 0 ); >;

	void ClipHull( float3 world, float4 rowX, float4 rowY, float4 rowZ )
	{
		if ( dot( rowX.xyz, rowX.xyz ) < 0.00000001 ) return;
		float4 point = float4( world, 1 );
		float3 local = float3( dot( rowX, point ), dot( rowY, point ), dot( rowZ, point ) );
		if ( local.y < -9 || local.y > 8.7 || local.z < 0 || local.z > 3.34 ) return;
		// The same stations and sloping strakes as the reference hull generator.
		float width;
		if ( local.y < -7.5 ) width = lerp( 1.95, 2.75, saturate( (local.y + 9) / 1.5 ) );
		else if ( local.y < -5 ) width = lerp( 2.75, 3.05, (local.y + 7.5) / 2.5 );
		else if ( local.y < 0 ) width = lerp( 3.05, 3.2, (local.y + 5) / 5 );
		else if ( local.y < 4 ) width = lerp( 3.2, 2.8, local.y / 4 );
		else if ( local.y < 7 ) width = lerp( 2.8, 1.7, (local.y - 4) / 3 );
		else width = lerp( 1.7, 0.12, (local.y - 7) / 1.7 );
		float flare = local.z < 0.95 ? lerp( 0.55, 0.8, local.z / 0.95 )
			: lerp( 0.8, 1.0, saturate( (local.z - 0.95) / 1.3 ) );
		// End inside the plank thickness, avoiding both interior seams and an exterior moat.
		if ( abs( local.x ) < max( 0, width * flare - 0.08 ) ) discard;
	}

	float WaterHash( float2 cell )
	{
		float3 p = frac( cell.xyx * float3( 0.1031, 0.1030, 0.0973 ) );
		p += dot( p, p.yzx + 33.33 );
		return frac( (p.x + p.y) * p.z );
	}

	float WaterNoise( float2 p )
	{
		float2 cell = floor( p );
		float2 f = frac( p );
		f = f * f * (3.0 - 2.0 * f);
		return lerp( lerp( WaterHash( cell ), WaterHash( cell + float2( 1, 0 ) ), f.x ),
			lerp( WaterHash( cell + float2( 0, 1 ) ), WaterHash( cell + 1.0 ), f.x ), f.y );
	}

	// Two independently advected sections of the periodic spectrum. Its RGB channels
	// store linear signed slopes so distant subpixel ripples cancel as mips average.
	float2 WindRipples( float2 p, float2 travel, float time, float roughness )
	{
		float2 across = float2( -travel.y, travel.x );
		float2 coords = float2( dot( p, travel ), dot( p, across ) );
		float2 windPatch = coords * 0.0022 - time * float2( 0.033, 0.014 );
		float bend = WaterNoise( windPatch );
		float shelter = WaterNoise( windPatch * 0.73 + 13.8 );
		float2 bendUv = float2( bend, shelter ) - 0.5;
		float2 slowUv = coords / 565.0 - time * float2( 0.061, 0.011 ) + 0.14 * bendUv;
		float2 slow = 2.0 * g_tWaveletSlopes.Sample( g_sAniso, slowUv ).rg - 1.0;

		float2 oblique = float2( 0.48, 0.877 );
		float2 tangent = float2( -oblique.y, oblique.x );
		float2 fastUv = float2( dot( coords, oblique ), dot( coords, tangent ) ) / 218.0;
		fastUv += float2( 0.37, 0.69 ) - time * float2( 0.098, -0.023 ) + 0.19 * bendUv.yx;
		float2 fast = 2.0 * g_tWaveletSlopes.Sample( g_sAniso, fastUv ).rg - 1.0;
		float2 slopes = slow * lerp( 0.16, 0.32, bend ) + (fast.x * oblique + fast.y * tangent) * lerp( 0.19, 0.08, bend );
		slopes *= roughness * lerp( 0.38, 1.0, smoothstep( 0.18, 0.81, shelter ) );
		return travel * slopes.x + across * slopes.y;
	}

	// A staggered bed of differently sized bubble pockets. The pixel footprint
	// resolves the narrow raised rims nearby; unresolved bubbles become their average.
	float2 BubbleLace( float2 uv, float resolution )
	{
		float2 cell = floor( uv );
		float2 f = frac( uv );
		float nearest = 3.0;
		[unroll] for ( int y = -1; y <= 1; ++y )
		{
			[unroll] for ( int x = -1; x <= 1; ++x )
			{
				float2 offset = float2( x, y );
				float2 id = cell + offset;
				float variation = WaterHash( id );
				float2 center = 0.23 + 0.54 * float2( variation, WaterHash( id + 41.3 ) );
				float2 delta = offset + center - f;
				nearest = min( nearest, dot( delta, delta ) / (0.63 + variation * 0.62) );
			}
		}
		float radius = sqrt( nearest );
		float rim = 1.0 - smoothstep( 0.035, 0.12, abs( radius - 0.38 ) );
		float froth = smoothstep( 0.28, 0.50, radius );
		return lerp( float2( 0.46, 0.22 ), float2( froth, rim ), resolution );
	}

	float2 FoamPatch( float2 position, float2 travel, float footprint )
	{
		float2 across = float2( -travel.y, travel.x );
		float2 uv = float2( dot( position, travel ), dot( position, across ) );
		float broad = WaterNoise( uv * float2( 0.008, 0.005 ) );
		float sheet = WaterNoise( uv * float2( 0.032, 0.013 ) + broad * 2.4 );
		float2 strandsUv = uv * float2( 0.069, 0.025 ) + float2( broad, sheet ) * 2.1;
		float strands = WaterNoise( strandsUv );
		float2 cells = uv * float2( 0.095, 0.054 ) + float2( broad * 2.3, 1.65 * strands );
		float2 bubble = BubbleLace( cells, 1.0 - smoothstep( 1.8, 12.0, footprint ) );
		float density = 0.52 * sheet + 0.34 * broad + 0.14 * strands;
		density -= 0.13 * smoothstep( 0.41, 0.73, WaterNoise( uv * 0.0047 + 17.2 ) );
		return float2( density, 0.70 * bubble.x + 0.30 * bubble.y );
	}

	float2 BreakingFoam( float2 position, float2 travel, float2 breaking, float time, float footprint )
	{
		float drive = saturate( length( breaking ) );
		if ( drive < 0.055 ) return 0;
		float2 direction = breaking / drive;
		float2 flow = position - travel * (time * 17.0);
		float phase = frac( time * 0.34 + 0.55 * WaterNoise( flow * 0.0025 ) );
		float phaseB = frac( phase + 0.5 );
		float2 excursion = direction * lerp( 38.0, 168.0, drive );
		float2 a = FoamPatch( flow - excursion * phase, travel, footprint );
		float2 b = FoamPatch( flow - excursion * phaseB, travel, footprint );
		float2 pattern = lerp( a, b, abs( phase * 2.0 - 1.0 ) );
		// A green aerated edge arrives ahead of the opaque cap. Thin rear wash exposes
		// the underlying surface through larger gaps and carries fewer bright bubbles.
		float mass = pattern.x + 0.32 * drive;
		float threshold = lerp( 0.70, 0.61, drive );
		float lip = smoothstep( threshold - 0.022, threshold + 0.055, mass );
		float holes = saturate( pattern.y * 1.35 + max( 0, mass - 0.95 ) * 1.2 );
		float activity = smoothstep( 0.055, 0.71, drive ) * (1.0 - exp( -2.7 * g_flFoamStrength ));
		float foam = lip * activity * lerp( 0.16, 1.0, holes );
		float churn = smoothstep( threshold - 0.13, threshold + 0.01, mass ) * activity;
		return float2( foam, churn );
	}

	// The visible sun is a disc. Point the half vector at its nearest edge, then
	// integrate unresolved normal variance into a broader, energy-limited GGX glint.
	float SunGlitter( float3 view, float3 normal, float3 light, float3 reflected, float seaState )
	{
		float along = dot( reflected, light );
		float3 lateral = reflected - light * along;
		float3 onDisc = normalize( light + lateral * min( 1.0, 0.010 / max( length( lateral ), 0.0001 ) ) );
		float3 halfVector = normalize( view + onDisc );
		float nl = saturate( dot( normal, onDisc ) );
		float nv = max( saturate( dot( normal, view ) ), 0.025 );
		float nh = saturate( dot( normal, halfVector ) );
		float vh = saturate( dot( view, halfVector ) );
		float alpha = lerp( 0.013, 0.030, seaState );
		float variance = dot( ddx( normal ), ddx( normal ) ) + dot( ddy( normal ), ddy( normal ) );
		float a2 = min( 0.026, alpha * alpha + 0.14 * variance );
		float divisor = 1.0 + nh * nh * (a2 - 1.0);
		float distribution = a2 / max( 0.000001, 3.14159 * divisor * divisor );
		float fresnel = 0.021 + 0.979 * pow( 1.0 - vh, 5.0 );
		float visibility = 0.5 / max( 0.015, nl * sqrt( nv * nv * (1.0 - a2) + a2 )
			+ nv * sqrt( nl * nl * (1.0 - a2) + a2 ) );
		float energy = 0.48 * distribution * visibility * fresnel * nl;
		return 1.45 * (1.0 - exp( -energy / 1.45 )) * smoothstep( -0.05, 0.09, along );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 world = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
		float2 p = world.xy;
		float t = g_flOceanTime;
		// The mesh carries the same surface as buoyancy, including the wave group's true crest.
		ClipHull( world, g_vHullLocalX0, g_vHullLocalY0, g_vHullLocalZ0 );
		ClipHull( world, g_vHullLocalX1, g_vHullLocalY1, g_vHullLocalZ1 );
		ClipHull( world, g_vHullLocalX2, g_vHullLocalY2, g_vHullLocalZ2 );
		ClipHull( world, g_vHullLocalX3, g_vHullLocalY3, g_vHullLocalZ3 );
		ClipHull( world, g_vHullLocalX4, g_vHullLocalY4, g_vHullLocalZ4 );
		ClipHull( world, g_vHullLocalX5, g_vHullLocalY5, g_vHullLocalZ5 );
		ClipHull( world, g_vHullLocalX6, g_vHullLocalY6, g_vHullLocalZ6 );
		ClipHull( world, g_vHullLocalX7, g_vHullLocalY7, g_vHullLocalZ7 );
		float3 geometricNormal = normalize( i.vNormalWs );
		float3 view = CalculatePositionToCameraDirWs( world );
		float3 light = normalize( g_vLightDirection );
		float2 travel = g_vTravelDirection / max( 0.001, length( g_vTravelDirection ) );
		float2 breaker = i.vTextureCoords.xy * g_flHasBreakers;
		float drive = saturate( length( breaker ) );
		float seaState = smoothstep( 3.0, 76.0, abs( g_flWaveHeight ) );
		float2 ripple = WindRipples( p, travel, t, lerp( 0.12, 1.0, seaState ) );
		float3 normal = normalize( geometricNormal + float3( ripple, 0 ) );

		float day = g_flDaylight;
		float storm = g_flStorminess;
		float swell = (world.z - g_flSeaLevel) / max( 24.0, abs( g_flWaveHeight ) );
		float crest = smoothstep( -0.12, 1.45, swell ) * seaState;
		float slope = saturate( 2.4 * length( geometricNormal.xy ) );
		float face = saturate( 0.46 + 1.65 * dot( normal.xy, light.xy ) + 0.17 * swell );
		float forwardScatter = pow( saturate( 0.5 - 0.5 * dot( light, view ) ), 2.0 );
		float keyElevation = saturate( light.z );
		float incidence = saturate( dot( normal, view ) );

		// Deep troughs scatter chiefly blue light. The light path through a raised
		// shoulder is shorter; green moves up the wave face and turns jade at its lip.
		float3 abyss = lerp( float3( 0.002, 0.031, 0.063 ), float3( 0.002, 0.022, 0.035 ), storm );
		float3 sea = lerp( float3( 0.008, 0.215, 0.254 ), float3( 0.006, 0.109, 0.100 ), storm );
		float3 body = lerp( abyss, sea, saturate( 0.13 + 0.73 * face * face + 0.43 * crest ) );
		float thickness = max( crest * (0.54 + 0.46 * slope), drive );
		float scatter = thickness * (0.18 + 1.75 * forwardScatter + 0.27 * keyElevation);
		scatter *= (0.52 + 0.48 * (1.0 - incidence)) * (1.0 - 0.31 * storm);
		body += float3( 0.018, 0.51, 0.255 ) * scatter;
		body *= lerp( float3( 0.15, 0.19, 0.28 ), float3( 1, 1, 1 ), day );

		float3 reflected = reflect( -view, normal );
		// A finite footprint around the reflected ray softens the horizon crossing and
		// keeps cloud contours from tracing dark outlines across every disturbed facet.
		float reflectedZ = 0.5 * (reflected.z + sqrt( reflected.z * reflected.z + 0.0144 ));
		float3 skyRay = normalize( float3( reflected.xy, reflectedZ ) );
		float reflectionRoughness = lerp( 0.28, 0.84, seaState );
		float3 sky = NavalSkyReflection( skyRay, reflectionRoughness );
		float skyVisible = smoothstep( -0.17, 0.07, reflected.z + 0.04 * crest );
		float fresnel = 0.021 + 0.979 * pow( 1.0 - incidence, 6.0 );
		// Swells occupy part of the reflected hemisphere close to the waterline.
		// Smooth water stays more mirror-like; rougher faces expose their green body.
		float skyFraction = lerp( 0.87, 0.49, seaState ) + 0.12 * smoothstep( 0.10, 0.55, reflectedZ );
		float3 color = lerp( body, sky, fresnel * skyVisible * skyFraction );

		float twilight = 4.0 * day * (1.0 - day) * (1.0 - 0.80 * storm);
		float3 keyColor = lerp( float3( 0.11, 0.19, 0.34 ), float3( 1.16, 0.97, 0.69 ), day );
		keyColor = lerp( keyColor, float3( 1.39, 0.58, 0.25 ), twilight );
		float direct = lerp( 0.33 * (1.0 - 0.75 * storm), NavalSunVisibility(), day );
		color += keyColor * direct * SunGlitter( view, normal, light, reflected, seaState );

		float footprint = max( length( ddx( p ) ), length( ddy( p ) ) );
		color = NavalShoreColor( color, p, world.z, day, storm, incidence, footprint, g_flHasBreakers );
		float2 foam = BreakingFoam( p, travel, breaker, t, footprint );
		float3 subFoam = lerp( float3( 0.023, 0.081, 0.107 ), float3( 0.086, 0.35, 0.30 ), day );
		color = lerp( color, subFoam * (1.0 - 0.26 * storm), 0.52 * foam.y );
		float3 foamLight = lerp( float3( 0.176, 0.23, 0.32 ), float3( 0.99, 1.11, 1.08 ), day );
		float3 foamShade = lerp( float3( 0.038, 0.072, 0.104 ), float3( 0.24, 0.44, 0.45 ), day );
		float topLight = saturate( 0.35 + 0.34 * dot( geometricNormal, light ) + 0.36 * drive );
		float3 froth = lerp( foamShade, foamLight, topLight ) * (1.0 - 0.30 * storm);
		color = lerp( color, froth, foam.x );

		// The same atmospheric colour sits behind both the wave silhouette and the sky.
		float distanceToCamera = length( world - g_vCameraPositionWs );
		float haze = smoothstep( 9000.0, 24000.0, distanceToCamera ) * lerp( 0.43, 0.68, storm );
		color = lerp( color, NavalSkyHorizon( -view ), haze );
		return float4( color, 1.0 );
	}
}

