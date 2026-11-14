#ifndef NAVAL_SHOALS_HLSL
#define NAVAL_SHOALS_HLSL
// XY centre and XY radius of the authored island in world units. Ring holds its
// outer and inner radii in local radius units, and the two bed elevations.
float4 g_vShelf0 < Attribute( "OceanShelf0" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vShelf1 < Attribute( "OceanShelf1" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vShelf2 < Attribute( "OceanShelf2" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vShelf3 < Attribute( "OceanShelf3" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vRing0 < Attribute( "OceanRing0" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vRing1 < Attribute( "OceanRing1" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vRing2 < Attribute( "OceanRing2" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vRing3 < Attribute( "OceanRing3" ); Default4( 0, 0, 0, 0 ); >;
float3 g_vShelfAxis0 < Attribute( "OceanShelfAxis0" ); Default3( 1, 0, 0 ); >;
float3 g_vShelfAxis1 < Attribute( "OceanShelfAxis1" ); Default3( 1, 0, 0 ); >;
float3 g_vShelfAxis2 < Attribute( "OceanShelfAxis2" ); Default3( 1, 0, 0 ); >;
float3 g_vShelfAxis3 < Attribute( "OceanShelfAxis3" ); Default3( 1, 0, 0 ); >;

// Generated islands share the four nearest-shore slots with authored shelf bands.
// Info = radius, beach heading, cove phase, vertical world scale. Zero radius disables a slot.
float4 g_vIslandInfo0 < Attribute( "OceanIslandInfo0" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandInfo1 < Attribute( "OceanIslandInfo1" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandInfo2 < Attribute( "OceanIslandInfo2" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandInfo3 < Attribute( "OceanIslandInfo3" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandPhases0 < Attribute( "OceanIslandPhases0" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandPhases1 < Attribute( "OceanIslandPhases1" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandPhases2 < Attribute( "OceanIslandPhases2" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandPhases3 < Attribute( "OceanIslandPhases3" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandLocalX0 < Attribute( "OceanIslandLocalX0" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandLocalX1 < Attribute( "OceanIslandLocalX1" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandLocalX2 < Attribute( "OceanIslandLocalX2" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandLocalX3 < Attribute( "OceanIslandLocalX3" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandLocalY0 < Attribute( "OceanIslandLocalY0" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandLocalY1 < Attribute( "OceanIslandLocalY1" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandLocalY2 < Attribute( "OceanIslandLocalY2" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandLocalY3 < Attribute( "OceanIslandLocalY3" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandLocalZ0 < Attribute( "OceanIslandLocalZ0" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandLocalZ1 < Attribute( "OceanIslandLocalZ1" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandLocalZ2 < Attribute( "OceanIslandLocalZ2" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandLocalZ3 < Attribute( "OceanIslandLocalZ3" ); Default4( 0, 0, 0, 0 ); >;
float4 g_vIslandShelfOffsets < Attribute( "OceanIslandShelfOffsets" ); Default4( 32, 120, 300, 550 ); >;
float4 g_vIslandShelfLevels < Attribute( "OceanIslandShelfLevels" ); Default4( -11, -100, -250, -425 ); >;

float IslandCoast( float a, float4 phases, float covePhase )
{
	float shape = 1.0 + 0.09 * sin( 2 * a + phases.x ) + 0.10 * cos( 3 * a + phases.y )
		+ 0.055 * cos( 5 * a + phases.z ) + 0.028 * sin( 8 * a + phases.w );
	float cove = 1.0 - smoothstep( 0.25, 0.94, acos( clamp( cos( a ), -1.0, 1.0 ) ) );
	return lerp( shape, 0.86 + 0.08 * sin( covePhase ) + 0.055 * (1.0 - cos( 3 * a )), cove );
}

float IslandCross( float2 a, float2 b ) { return a.x * b.y - a.y * b.x; }

bool IslandTriangleHeight( float2 p, float3 a, float3 b, float3 c, out float height )
{
	float2 u = b.xy - a.xy;
	float2 v = c.xy - a.xy;
	float2 w = p - a.xy;
	float area = IslandCross( u, v );
	float s = IslandCross( w, v ) / area;
	float t = IslandCross( u, w ) / area;
	height = a.z + s * (b.z - a.z) + t * (c.z - a.z);
	return s >= -0.00001 && t >= -0.00001 && s + t <= 1.00001;
}

float IslandDepth( float2 p, float height, float4 info, float4 phases, float4 rowX, float4 rowY, float4 rowZ )
{
	if ( info.x <= 0.0 ) return 3000;
	float4 world = float4( p, height, 1 );
	float3 local = float3( dot( rowX, world ), dot( rowY, world ), dot( rowZ, world ) );
	if ( max( abs( local.x ), abs( local.y ) ) > info.x * 1.35 + 1200 ) return 3000;
	float cs = cos( info.y ), sn = sin( info.y );
	float2 xy = float2( local.x * cs + local.y * sn, -local.x * sn + local.y * cs );
	const float tau = 6.28318530718;
	const float step = tau / 96.0;
	float angle = frac( atan2( xy.y, xy.x ) / tau ) * tau;
	float a = floor( angle / step ) * step;
	float b = a + step;
	float2 left = float2( cos( a ), sin( a ) );
	float2 right = float2( cos( b ), sin( b ) );
	float ra = info.x * IslandCoast( a, phases, info.z );
	float rb = info.x * IslandCoast( b, phases, info.z );
	float distance = length( xy );
	float offset = angle - a;
	float shore = ra * rb * sin( step ) / max( rb * sin( step - offset ) + ra * sin( offset ), 0.00001 );
	if ( distance < shore )
	{
		// Extend the beach slope under waves just inland; the solid terrain occludes headlands.
		float bed = min( (shore - distance) * 0.21, 120.0 );
		return (local.z - bed) * info.w;
	}
	float innerOffset = 0, innerHeight = 0;
	[unroll] for ( int ring = 0; ring < 4; ring++ )
	{
		float outerOffset = g_vIslandShelfOffsets[ring];
		float outerHeight = g_vIslandShelfLevels[ring];
		float3 va = float3( left * (ra + innerOffset), innerHeight );
		float3 vb = float3( left * (ra + outerOffset), outerHeight );
		float3 vc = float3( right * (rb + outerOffset), outerHeight );
		float3 vd = float3( right * (rb + innerOffset), innerHeight );
		float bed;
		// Match the emitted diagonal, rather than treating the shelf as a circular ring.
		if ( IslandTriangleHeight( xy, va, vb, vc, bed ) )
			return (local.z - bed) * info.w;
		if ( IslandTriangleHeight( xy, va, vc, vd, bed ) )
			return (local.z - bed) * info.w;
		innerOffset = outerOffset;
		innerHeight = outerHeight;
	}
	float outerA = ra + innerOffset, outerB = rb + innerOffset;
	float edge = outerA * outerB * sin( step ) / max( outerB * sin( step - offset ) + outerA * sin( offset ), 0.00001 );
	return (local.z - innerHeight + max( distance - edge, 0 ) * 0.75) * info.w;
}

float ShelfDepth( float2 p, float height, float4 shelf, float4 ring, float3 axis )
{
	float2 offset = p - shelf.xy;
	if ( shelf.z <= 0.0 || max( abs( offset.x ), abs( offset.y ) ) > max( shelf.z, shelf.w ) * (ring.x + 1.3) )
		return 3000;
	float2 local = float2( dot( offset, axis.xy ), dot( offset, float2( -axis.y, axis.x ) ) ) / shelf.zw;
	float angle = atan2( local.y, local.x );
	// IslandSurface builds concentric 24-sided rings; intersect the actual edge for
	// this sector, so a passing wave's wash follows each rocky/sandy indentation.
	float sector = 0.26179939;
	float a = floor( angle / sector ) * sector;
	float b = a + sector;
	float2 aPos = float2( cos( a ), sin( a ) ) * (1.0 + 0.1 * sin( 3.0 * a + axis.z ) + 0.06 * cos( 5.0 * a - axis.z ));
	float2 bPos = float2( cos( b ), sin( b ) ) * (1.0 + 0.1 * sin( 3.0 * b + axis.z ) + 0.06 * cos( 5.0 * b - axis.z ));
	float2 edgeNormal = float2( bPos.y - aPos.y, aPos.x - bPos.x );
	float radius = dot( local, edgeNormal ) / max( dot( aPos, edgeNormal ), 0.00001 );
	float shore = (radius - ring.y) / max( 0.001, ring.x - ring.y );
	float bed = lerp( ring.w, ring.z, min( shore, 1.0 ) );
	// Continue the optical shelf beyond the last underwater terrain ring. This
	// avoids a visible ring where the physical collision mesh ends in deep water.
	bed -= max( radius - ring.x, 0 ) * min( shelf.z, shelf.w ) * 0.75;
	return height - bed;
}

float3 NavalShoreColor( float3 sea, float2 p, float height, float day, float storm, float incidence, float footprint, float exterior )
{
	if ( exterior < 0.5 ) return sea;
	float d = min( min( ShelfDepth( p, height, g_vShelf0, g_vRing0, g_vShelfAxis0 ), ShelfDepth( p, height, g_vShelf1, g_vRing1, g_vShelfAxis1 ) ),
		min( ShelfDepth( p, height, g_vShelf2, g_vRing2, g_vShelfAxis2 ), ShelfDepth( p, height, g_vShelf3, g_vRing3, g_vShelfAxis3 ) ) );
	d = min( d, min( min( IslandDepth( p, height, g_vIslandInfo0, g_vIslandPhases0, g_vIslandLocalX0, g_vIslandLocalY0, g_vIslandLocalZ0 ),
		IslandDepth( p, height, g_vIslandInfo1, g_vIslandPhases1, g_vIslandLocalX1, g_vIslandLocalY1, g_vIslandLocalZ1 ) ),
		min( IslandDepth( p, height, g_vIslandInfo2, g_vIslandPhases2, g_vIslandLocalX2, g_vIslandLocalY2, g_vIslandLocalZ2 ),
		IslandDepth( p, height, g_vIslandInfo3, g_vIslandPhases3, g_vIslandLocalX3, g_vIslandLocalY3, g_vIslandLocalZ3 ) ) ) );
	if ( d > 700 ) return sea;
	float depth = max( d, 0 );
	float clarity = exp2( -depth / lerp( 156.0, 81.0, storm ) );
	float overhead = sqrt( saturate( incidence ) );
	float3 green = lerp( float3( 0.012, 0.40, 0.33 ), float3( 0.029, 0.205, 0.172 ), storm );
	float sand = exp2( -depth / 49 ) * overhead;
	float patch = SkyNoise( p * 0.0088 ) * 0.30;
	float3 bottom = lerp( green, float3( 0.37, 0.66, 0.46 ) * (0.75 + patch), sand );
	float3 lit = bottom * lerp( float3( 0.038, 0.084, 0.14 ), float3( 1, 1, 1 ), day );
	sea = lerp( sea, lit, clarity * (0.15 + 0.79 * overhead) );
	float chop = SkyNoise( p * 0.031 - g_flClock * 0.095 );
	float wash = 1.0 - smoothstep( 1.5, 9.0 + 14.0 * chop + min( footprint, 8.0 ), abs( d - (6.0 + 9.0 * chop) ) );
	float foam = wash * smoothstep( 0.28, 0.70, chop + patch ) * (0.44 + 0.3 * storm);
	return lerp( sea, lerp( float3( 0.07, 0.14, 0.20 ), float3( 0.76, 0.91, 0.86 ), day ), foam );
}
#endif
