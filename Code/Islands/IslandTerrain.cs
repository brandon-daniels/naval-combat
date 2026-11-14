using System;

namespace NavalCombat;

/// <summary>Local-space island surface. The waterline is Z = 0. Every emitted face is solid.</summary>
internal sealed class IslandTerrain
{
	public const int Sectors = 96;
	private const float Step = MathF.Tau / Sectors;
	private const int CaveInner = 9;
	private const int CaveOuter = CaveInner + 1;
	private const int CaveHalfWidth = 3;
	private static readonly float[] LandRings = {
		0, 0.075f, 0.15f, 0.22f, 0.29f, 0.36f, 0.42f, 0.47f, 0.52f,
		0.57f, 0.64f, 0.69f, 0.74f, 0.79f, 0.84f, 0.88f, 0.915f, 0.945f, 0.97f, 1
	};
	private static readonly float[] ShelfDistances = { 32, 120, 300, 550 };
	private static readonly float[] ShelfHeights = { -11, -100, -250, -425 };

	private readonly Vector3[,] points = new Vector3[LandRings.Length + ShelfDistances.Length, Sectors];
	private readonly float[] phases = new float[7];
	private readonly float[] coast = new float[Sectors];
	private readonly float relief;
	private readonly float cliffs;
	private readonly float beachAngle;
	private readonly Vector3 acrossBeach;
	private readonly Vector3 summit;
	private readonly Vector3 secondHill;
	private readonly int caveSector;
	private readonly Vector3 caveAxis;
	private readonly float caveHeight;
	private readonly float caveFloor;

	public float Radius { get; }
	public float OuterRadius { get; }
	public bool HasField { get; }
	public bool HasCave { get; }
	public Vector3 BeachDirection { get; }
	public Vector3 FieldCenter { get; }
	public Vector2 FieldHalfSize { get; }
	public Vector3 CaveEntrance => points[CaveOuter, caveSector];
	/// <summary>Seeded harmonics of the polygonal coast, in the BeachDirection frame.</summary>
	public Vector4 CoastPhases => new( phases[1], phases[2], phases[3], phases[4] );
	public float CovePhase => phases[5];
	/// <summary>Local radial offsets from the coast at each submerged ring vertex.</summary>
	public Vector4 ShelfOffsets => new( ShelfDistances[0], ShelfDistances[1], ShelfDistances[2], ShelfDistances[3] );
	/// <summary>Local ring heights below the authored Z = 0 waterline.</summary>
	public Vector4 ShelfLevels => new( ShelfHeights[0], ShelfHeights[1], ShelfHeights[2], ShelfHeights[3] );

	public IslandTerrain( int seed, float radius, float relief, float cliffStrength, bool hasField, bool hasCave )
	{
		Radius = radius;
		this.relief = relief;
		cliffs = cliffStrength;
		HasField = hasField;
		HasCave = hasCave;
		var random = new Random( seed );
		for ( int i = 0; i < phases.Length; i++ )
			phases[i] = (float)random.NextDouble() * MathF.Tau;
		beachAngle = phases[0];
		BeachDirection = Direction( beachAngle );
		acrossBeach = new Vector3( -BeachDirection.y, BeachDirection.x, 0 );
		// Coordinates of the summits in the beach frame, as fractions of the island radius.
		summit = new Vector3( -0.16f - 0.22f * Unit( phases[1] ), 0.39f * MathF.Sin( phases[2] ), 0 );
		secondHill = new Vector3( 0.09f * MathF.Cos( phases[3] ), -0.38f * MathF.Cos( phases[2] ), 0 );
		FieldHalfSize = new Vector2( Math.Clamp( radius * 0.105f, 118, 235 ), Math.Clamp( radius * 0.14f, 145, 290 ) );
		FieldCenter = (BeachDirection * 0.45f - acrossBeach * 0.19f) * radius;
		FieldCenter = FieldCenter.WithZ( Math.Clamp( radius * 0.048f, 57, 132 ) );
		caveSector = 13 + random.Next( 0, 4 );
		caveAxis = Direction( beachAngle + caveSector * Step );
		caveHeight = Math.Clamp( radius * 0.10f, 153, 230 );
		caveFloor = Math.Clamp( radius * 0.048f, 71, 135 );

		float farthest = 0;
		for ( int side = 0; side < Sectors; side++ )
		{
			float relative = side * Step;
			var axis = Direction( beachAngle + relative );
			float r = radius * CoastShape( relative );
			coast[side] = r;
			farthest = Math.Max( farthest, r );
			for ( int ring = 0; ring < LandRings.Length; ring++ )
			{
				float f = LandRings[ring];
				// Stagger the inner field of facets. Leave the mouth, shoreline and their shared edges exact.
				if ( ring > 0 && ring < LandRings.Length - 1 && ring != CaveInner && ring != CaveOuter )
				{
					float spacing = Math.Min( f - LandRings[ring - 1], LandRings[ring + 1] - f );
					f += 0.24f * spacing * Smooth( 0.17f, 0.45f, f ) * MathF.Sin( side * 1.93f + ring * 2.37f + phases[2] );
				}
				var xy = axis * (f * r);
				points[ring, side] = xy.WithZ( LandHeight( xy, f, r, relative ) );
			}
			for ( int shelf = 0; shelf < ShelfDistances.Length; shelf++ )
				points[LandRings.Length + shelf, side] = (axis * (r + ShelfDistances[shelf])).WithZ( ShelfHeights[shelf] );
		}
		// A polar mesh has a single common vertex at its center.
		for ( int side = 1; side < Sectors; side++ )
			points[0, side] = points[0, 0];
		OuterRadius = farthest + ShelfDistances[^1];
	}

	private static Vector3 Direction( float angle ) => new( MathF.Cos( angle ), MathF.Sin( angle ), 0 );
	private static float Unit( float phase ) => 0.5f + 0.5f * MathF.Cos( phase );
	private static float Mix( float a, float b, float t ) => a + (b - a) * t;

	private static float Smooth( float low, float high, float value )
	{
		float t = Math.Clamp( (value - low) / (high - low), 0, 1 );
		return t * t * (3 - 2 * t);
	}

	private float CoastShape( float a )
	{
		float shape = 1 + 0.09f * MathF.Sin( 2 * a + phases[1] ) + 0.10f * MathF.Cos( 3 * a + phases[2] )
			+ 0.055f * MathF.Cos( 5 * a + phases[3] ) + 0.028f * MathF.Sin( 8 * a + phases[4] );
		// Shave a crescent out of the coast and smooth its floor across a ship's beam.
		float cove = 1 - Smooth( 0.25f, 0.94f, MathF.Acos( Math.Clamp( MathF.Cos( a ), -1, 1 ) ) );
		return Mix( shape, 0.86f + 0.08f * MathF.Sin( phases[5] ) + 0.055f * (1 - MathF.Cos( 3 * a )), cove );
	}

	private float LandHeight( Vector3 xy, float f, float shore, float angle )
	{
		float x = Vector3.Dot( xy, BeachDirection ) / Radius;
		float y = Vector3.Dot( xy, acrossBeach ) / Radius;
		var p = new Vector3( x, y, 0 );
		float ridge = MathF.Exp( -7.6f * (p - summit).LengthSquared );
		float knoll = MathF.Exp( -12 * (p - secondHill).LengthSquared );
		float folds = 0.035f * MathF.Sin( x * 18 + y * 8 + phases[4] ) + 0.024f * MathF.Sin( y * 22 - x * 3 + phases[5] );
		float inland = relief * (0.12f + 0.72f * ridge + 0.26f * knoll + folds * Smooth( 0.04f, 0.39f, p.Length ));
		float edge = 1 - f;
		float beachArc = MathF.Acos( Math.Clamp( MathF.Cos( angle ), -1, 1 ) );
		float headland = Smooth( 0.60f, 1.46f, beachArc ) * (0.58f + 0.42f * Unit( angle * 3 + phases[6] ));
		float ledge = relief * (0.18f + cliffs * 0.65f) * headland;
		float brow = Smooth( 0.018f, 0.098f - 0.043f * cliffs, edge );
		float z = ledge * brow * (1 - Smooth( 0.18f, 0.58f, edge )) + inland * Smooth( 0, 0.47f, edge );
		float distance = edge * shore;
		float beachGrade = Math.Min( distance, 330 ) * 0.21f + Math.Max( 0, distance - 330 ) * 0.10f;
		float apron = (1 - Smooth( 0.54f, 1.25f, beachArc )) * Smooth( 0.36f, 0.66f, f );
		z = Mix( z, beachGrade, apron );

		if ( HasField )
		{
			float overhang = FieldEdge( xy );
			float blend = Smooth( Radius * 0.083f, Math.Max( Radius * 0.22f, 235 ), overhang );
			// Keep the graded terrace from lifting the waterline on a deep coastal indentation.
			float coastalFade = (1 - Smooth( 0, 160, distance )) * Smooth( 30, 70, overhang );
			z = Mix( z, FieldCenter.z, (1 - blend) * (1 - coastalFade) );
		}
		if ( HasCave )
		{
			// A broad, walkable bench leads from the coast to the cave at the foot of this scarp.
			float offset = MathF.Acos( Math.Clamp( MathF.Cos( angle - caveSector * Step ), -1, 1 ) );
			float bank = (1 - Smooth( 0.21f, 0.50f, offset )) * Smooth( 0.26f, 0.38f, f );
			float shelf = caveFloor * Smooth( 0, 0.30f, edge );
			float roof = caveFloor + caveHeight + 52 + relief * 0.05f * Unit( angle * 15 + phases[1] );
			float scarp = Mix( Math.Max( z, roof ), shelf, Smooth( LandRings[CaveInner], LandRings[CaveOuter], f ) );
			z = Mix( z, scarp, bank );
		}
		return z;
	}

	private float FieldEdge( Vector3 xy )
	{
		var delta = xy.WithZ( 0 ) - FieldCenter.WithZ( 0 );
		return Math.Max( Math.Abs( Vector3.Dot( delta, BeachDirection ) ) - FieldHalfSize.x,
			Math.Abs( Vector3.Dot( delta, acrossBeach ) ) - FieldHalfSize.y );
	}

	/// <summary>Ground occupied by the approach, path into the cave or farm footprint stays clear of props.</summary>
	public bool IsReserved( float x, float y )
	{
		var xy = new Vector3( x, y, 0 );
		float forward = Vector3.Dot( xy, BeachDirection );
		if ( forward > Radius * 0.44f && Math.Abs( Vector3.Dot( xy, acrossBeach ) ) < Math.Max( 235, forward * 0.27f ) )
			return true;
		if ( HasField && FieldEdge( xy ) < 68 ) return true;
		if ( !HasCave ) return false;
		float outwards = Vector3.Dot( xy, caveAxis );
		var side = new Vector3( -caveAxis.y, caveAxis.x, 0 );
		return outwards > Radius * 0.12f && Math.Abs( Vector3.Dot( xy, side ) ) < outwards * 0.26f + 55;
	}

	private (int Left, int Right, float Angle) SectorAt( float angle )
	{
		float relative = (angle - beachAngle) % MathF.Tau;
		if ( relative < 0 ) relative += MathF.Tau;
		int left = Math.Min( (int)(relative / Step), Sectors - 1 );
		return (left, (left + 1) % Sectors, relative - left * Step);
	}

	/// <summary>Intersection of a radial ray and the actual polygonal waterline.</summary>
	public float SeaRadius( float angle )
	{
		var (left, right, offset) = SectorAt( angle );
		return coast[left] * coast[right] * MathF.Sin( Step )
			/ (coast[right] * MathF.Sin( Step - offset ) + coast[left] * MathF.Sin( offset ));
	}

	/// <summary>Uppermost terrain at local XY, interpolated from the same faces used for collision.</summary>
	public Vector3 SurfacePoint( float x, float y )
	{
		var (left, right, _) = SectorAt( MathF.Atan2( y, x ) );
		var p = new Vector3( x, y, 0 );
		for ( int ring = 0; ring < points.GetLength( 0 ) - 1; ring++ )
		{
			var a = points[ring, left];
			var b = OuterVertex( ring, left );
			var c = OuterVertex( ring, right );
			var d = points[ring, right];
			if ( HeightInTriangle( p, a, b, c, out float z ) || (ring > 0 && HeightInTriangle( p, a, c, d, out z )) )
				return p.WithZ( z );
		}
		return p.WithZ( ShelfHeights[^1] );
	}

	private static bool HeightInTriangle( Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float height )
	{
		var u = b - a;
		var v = c - a;
		var w = p - a;
		float area = u.x * v.y - u.y * v.x;
		float s = (w.x * v.y - w.y * v.x) / area;
		float t = (u.x * w.y - u.y * w.x) / area;
		height = a.z + s * u.z + t * v.z;
		return s >= -0.00001f && t >= -0.00001f && s + t <= 1.00001f;
	}

	// The roof lip replaces the upper edge of the opening. The next coastal band begins at its floor.
	private Vector3 OuterVertex( int ring, int side )
	{
		var v = points[ring + 1, side];
		if ( !HasCave || ring != CaveInner || Math.Abs( side - caveSector ) > CaveHalfWidth ) return v;
		return v + Vector3.Up * ArchHeight( side - caveSector + CaveHalfWidth );
	}

	private float ArchHeight( int column )
	{
		// Ends meet the floor, with tall shoulders so a standing sailor can walk through.
		float t = (float)(column - CaveHalfWidth) / CaveHalfWidth;
		return caveHeight * MathF.Sqrt( Math.Max( 0, 1 - t * t ) );
	}

	public void Emit( Action<Vector3, Vector3, Vector3, Color> triangle )
	{
		for ( int ring = 0; ring < points.GetLength( 0 ) - 1; ring++ )
		for ( int side = 0; side < Sectors; side++ )
		{
			int next = (side + 1) % Sectors;
			var a = points[ring, side];
			var b = OuterVertex( ring, side );
			var c = OuterVertex( ring, next );
			var d = points[ring, next];
			EmitGround( triangle, a, b, c );
			if ( ring > 0 ) EmitGround( triangle, a, c, d );
		}
		if ( HasCave ) EmitCave( triangle );
	}

	private float GrassLine( Vector3 p )
	{
		float line = p.z - 30 - 6 * MathF.Sin( p.x * 0.007f + phases[2] ) - 4 * MathF.Sin( p.y * 0.011f + phases[4] );
		if ( line > 0 ) return line;
		float inland = SeaRadius( MathF.Atan2( p.y, p.x ) ) - p.WithZ( 0 ).Length;
		return Math.Max( line, inland * 0.20f - 36 );
	}

	private void EmitGround( Action<Vector3, Vector3, Vector3, Color> triangle, Vector3 a, Vector3 b, Vector3 c )
	{
		var p = (a + b + c) / 3;
		float up = Vector3.Cross( b - a, c - a ).Normal.z;
		float mottling = Unit( p.x * 0.008f + 1.7f * MathF.Sin( p.y * 0.005f + phases[4] ) );
		if ( p.z < -2 )
		{
			triangle( a, b, c, new Color( 0.51f + 0.09f * mottling, 0.67f + 0.06f * mottling, 0.55f ) );
			return;
		}
		if ( up < 0.79f )
		{
			float strata = 0.5f * mottling + 0.5f * Unit( p.z * 0.024f + p.x * 0.002f + phases[3] );
			triangle( a, b, c, new Color( 0.42f + 0.20f * strata, 0.39f + 0.17f * strata, 0.40f + 0.14f * strata ) );
			return;
		}
		float meadow = 0.65f * mottling + 0.35f * Unit( p.y * 0.003f + phases[1] );
		var grass = new Color( 0.33f + 0.092f * meadow, 0.58f + 0.083f * meadow, 0.26f + 0.035f * meadow );
		var sand = new Color( 0.94f, 0.77f + 0.06f * mottling, 0.51f + 0.08f * mottling );
		float da = GrassLine( a ), db = GrassLine( b ), dc = GrassLine( c );
		if ( (da > 0) == (db > 0) && (da > 0) == (dc > 0) )
		{
			triangle( a, b, c, da > 0 ? grass : sand );
			return;
		}
		// Clip exactly where sand meets grass. Rotation puts the single unlike corner at A.
		if ( (da > 0) == (db > 0) )
		{
			(a, b, c) = (c, a, b);
			(da, db, dc) = (dc, da, db);
		}
		else if ( (da > 0) == (dc > 0) )
		{
			(a, b, c) = (b, c, a);
			(da, db, dc) = (db, dc, da);
		}
		var ab = ShoreCrossing( a, b, da, db );
		var ac = ShoreCrossing( a, c, da, dc );
		// Do not cook near-zero-area slivers where the color boundary nearly touches a corner.
		if ( Vector3.Cross( ab - a, ac - a ).LengthSquared < 0.0001f
			|| Vector3.Cross( b - ab, c - ab ).LengthSquared < 0.0001f
			|| Vector3.Cross( c - ab, ac - ab ).LengthSquared < 0.0001f )
		{
			triangle( a, b, c, da + db + dc > 0 ? grass : sand );
			return;
		}
		var pairColor = db > 0 ? grass : sand;
		triangle( a, ab, ac, da > 0 ? grass : sand );
		triangle( ab, b, c, pairColor );
		triangle( ab, c, ac, pairColor );
	}

	// Canonical ordering gives neighboring triangles bit-identical shared edge vertices.
	private static Vector3 ShoreCrossing( Vector3 a, Vector3 b, float da, float db ) => da < db
		? a + (b - a) * (da / (da - db)) : b + (a - b) * (db / (db - da));

	private void EmitCave( Action<Vector3, Vector3, Vector3, Color> triangle )
	{
		const int sections = 5;
		const int columns = CaveHalfWidth * 2 + 1;
		var floor = new Vector3[sections, columns];
		var ceiling = new Vector3[sections, columns];
		var mouth = CaveEntrance;
		var sideAxis = new Vector3( -caveAxis.y, caveAxis.x, 0 );
		float length = Math.Clamp( coast[caveSector] * 0.25f, 205, 575 );
		for ( int i = 0; i < sections; i++ )
		{
			float t = i / (sections - 1f);
			float bend = MathF.Sin( t * MathF.PI ) * length * 0.055f;
			for ( int j = 0; j < columns; j++ )
			{
				int side = caveSector - CaveHalfWidth + j;
				var rim = points[CaveOuter, side];
				var offset = rim - mouth;
				var center = mouth - caveAxis * (length * t) + sideAxis * bend;
				var basePoint = center + offset * (1 - 0.29f * t);
				floor[i, j] = i == 0 ? rim : basePoint.WithZ( Mix( rim.z, mouth.z + 12, t ) );
				ceiling[i, j] = floor[i, j] + Vector3.Up * (ArchHeight( j ) * (1 - 0.22f * t));
			}
		}
		for ( int i = 0; i < sections - 1; i++ )
		for ( int j = 0; j < columns - 1; j++ )
		{
			var floorColor = new Color( 0.49f - i * 0.043f, 0.44f - i * 0.037f, 0.42f - i * 0.027f );
			triangle( floor[i, j], floor[i, j + 1], floor[i + 1, j + 1], floorColor );
			triangle( floor[i, j], floor[i + 1, j + 1], floor[i + 1, j], floorColor );
			float vein = Unit( i * 1.6f + j + phases[6] );
			var stone = new Color( 0.25f + vein * 0.075f, 0.28f + vein * 0.075f, 0.34f + vein * 0.09f );
			triangle( ceiling[i, j], ceiling[i + 1, j], ceiling[i + 1, j + 1], stone );
			triangle( ceiling[i, j], ceiling[i + 1, j + 1], ceiling[i, j + 1], stone );
		}
		int end = sections - 1;
		var back = new Color( 0.23f, 0.29f, 0.32f );
		for ( int j = 0; j < columns - 1; j++ )
		{
			if ( j < columns - 2 ) triangle( floor[end, j], floor[end, j + 1], ceiling[end, j + 1], back );
			if ( j > 0 ) triangle( floor[end, j], ceiling[end, j + 1], ceiling[end, j], back );
		}
	}
}
