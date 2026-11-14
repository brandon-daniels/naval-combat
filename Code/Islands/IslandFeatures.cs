using System;
using System.Collections.Generic;

namespace NavalCombat;

/// <summary>Builds local-space scenery in the island's shared render and collision meshes.</summary>
internal sealed class IslandFeatures
{
	private static readonly Color Granite = new( 0.37f, 0.43f, 0.47f );
	private static readonly Color Slate = new( 0.49f, 0.51f, 0.55f );
	private static readonly Color Bark = new( 0.43f, 0.26f, 0.14f );
	private static readonly Color Timber = new( 0.59f, 0.38f, 0.19f );
	private static readonly Color Board = new( 0.72f, 0.51f, 0.29f );
	private static readonly Color Rope = new( 0.90f, 0.73f, 0.42f );
	private static readonly Color PalmGreen = new( 0.14f, 0.48f, 0.27f );
	private static readonly Color LeafGreen = new( 0.37f, 0.66f, 0.25f );
	private static readonly Color Meadow = new( 0.54f, 0.73f, 0.28f );
	private static readonly Color Coral = new( 0.96f, 0.42f, 0.33f );
	private static readonly Color Saffron = new( 1.0f, 0.79f, 0.27f );
	private static readonly Color Soil = new( 0.43f, 0.26f, 0.16f );
	private static readonly Color Furrow = new( 0.57f, 0.35f, 0.19f );

	private readonly IslandTerrain terrain;
	private readonly IslandGeometry mesh;
	private readonly Random random;
	private readonly List<(Vector3 Point, float Radius)> obstacles = new();

	private IslandFeatures( IslandTerrain terrain, IslandGeometry mesh, int seed )
	{
		this.terrain = terrain;
		this.mesh = mesh;
		random = new Random( seed ^ 0x496c616e );
	}

	internal static void Build( IslandTerrain terrain, IslandGeometry mesh, int seed, bool hasDock, bool hasField )
	{
		var features = new IslandFeatures( terrain, mesh, seed );
		features.Scatter();
		if ( hasField ) features.Field();
		if ( hasDock ) features.Dock();
	}

	private float Range( float min, float max ) => min + (max - min) * (float)random.NextDouble();
	private static Vector3 Direction( float radians ) => new( MathF.Cos( radians ), MathF.Sin( radians ), 0 );
	private static Vector3 Across( Vector3 axis ) => new( -axis.y, axis.x, 0 );
	private Vector3 Ground( Vector3 xy ) => terrain.SurfacePoint( xy.x, xy.y );

	private void Scatter()
	{
		// Keep island silhouettes legible from a ship while preserving space between obstacles on foot.
		int groves = Math.Clamp( (int)(terrain.Radius / 190), 4, 14 );
		for ( int i = 0; i < groves; i++ )
		{
			if ( !FindSite( 80, out var center ) ) continue;
			Palm( center, Range( 150, 255 ) );
			obstacles.Add( (center, 70) );
			var neighbor = Ground( center + Direction( Range( 0, MathF.Tau ) ) * Range( 85, 150 ) );
			if ( Usable( neighbor, 42 ) )
			{
				Palm( neighbor, Range( 110, 185 ) );
				obstacles.Add( (neighbor, 48) );
			}
		}

		for ( int i = 0; i < groves * 2; i++ )
		{
			float size = Range( 35, 84 );
			if ( !FindSite( size, out var center ) ) continue;
			Boulder( center, size, Range( 0.65f, 1.45f ) * size );
			obstacles.Add( (center, size) );
			var neighbor = Ground( center + Direction( Range( 0, MathF.Tau ) ) * size * 1.2f );
			if ( Usable( neighbor, 14 ) ) Boulder( neighbor, size * 0.36f, size * 0.43f );
		}

		for ( int i = 0; i < groves * 7; i++ )
		{
			if ( !FindSite( 18, out var center ) ) continue;
			float heading = Range( 0, MathF.Tau );
			for ( int blade = 0; blade < 5; blade++ )
			{
				var axis = Direction( heading + blade * 2.4f );
				var foot = Ground( center + axis * Range( 3, 12 ) ) - Vector3.Up * 1.5f;
				Leaf( foot, axis, Range( 12, 24 ), Range( 16, 38 ), Range( 3, 6 ), Meadow );
			}
			if ( i % 4 == 0 ) Flower( center, i % 8 == 0 ? Coral : Saffron );
		}
	}

	private bool FindSite( float clearance, out Vector3 point )
	{
		for ( int attempt = 0; attempt < 14; attempt++ )
		{
			float angle = Range( 0, MathF.Tau );
			float distance = MathF.Sqrt( Range( 0, 1 ) ) * 0.94f * terrain.SeaRadius( angle );
			point = Ground( Direction( angle ) * distance );
			if ( Usable( point, clearance ) ) return true;
		}
		point = default;
		return false;
	}

	private bool Usable( Vector3 point, float radius )
	{
		if ( point.z < 36 || terrain.IsReserved( point.x, point.y ) ) return false;
		foreach ( var obstacle in obstacles )
		{
			if ( (point - obstacle.Point).WithZ( 0 ).Length < radius + obstacle.Radius ) return false;
		}
		// Check the footprint, including its downhill side and the boundary of the landing/field.
		for ( int i = 0; i < 4; i++ )
		{
			var edge = point + Direction( i * MathF.PI / 2 ) * radius;
			if ( terrain.IsReserved( edge.x, edge.y ) || MathF.Abs( Ground( edge ).z - point.z ) > radius * 0.55f )
				return false;
		}
		return true;
	}

	private void Boulder( Vector3 basePoint, float size, float height )
	{
		const int sides = 7;
		var low = new Vector3[sides];
		var shoulder = new Vector3[sides];
		float bearing = Range( 0, MathF.Tau );
		var summit = basePoint + Direction( bearing ) * size * 0.27f + Vector3.Up * height;
		var color = random.Next( 2 ) == 0 ? Granite : Slate;
		for ( int i = 0; i < sides; i++ )
		{
			var radial = Direction( bearing + MathF.Tau * (i + Range( -0.16f, 0.16f )) / sides );
			low[i] = Ground( basePoint + radial * size * Range( 0.82f, 1.04f ) ) - Vector3.Up * 7;
			shoulder[i] = basePoint + radial * size * Range( 0.55f, 0.84f ) + Vector3.Up * height * Range( 0.46f, 0.75f );
		}
		for ( int i = 0; i < sides; i++ )
		{
			int j = (i + 1) % sides;
			Quad( low[i], low[j], shoulder[j], shoulder[i], color );
			mesh.Triangle( shoulder[i], shoulder[j], summit, color );
		}
	}

	private void Palm( Vector3 foot, float height )
	{
		float bearing = Range( 0, MathF.Tau );
		var lean = Direction( bearing ) * height * Range( 0.14f, 0.32f );
		var root = foot - Vector3.Up * 9;
		var bend = foot + Vector3.Up * height * 0.48f + lean * 0.72f;
		var crown = foot + Vector3.Up * height + lean;
		Trunk( root, bend, height * 0.060f, height * 0.046f, Bark );
		Trunk( bend, crown, height * 0.046f, height * 0.036f, Timber );
		for ( int i = 0; i < 8; i++ )
		{
			var axis = Direction( bearing + MathF.Tau * i / 8 + Range( -0.1f, 0.1f ) );
			float length = height * Range( 0.48f, 0.67f );
			Frond( crown, axis, length, i % 3 == 0 ? LeafGreen : PalmGreen );
		}
	}

	private void Frond( Vector3 crown, Vector3 axis, float length, Color color )
	{
		var across = Across( axis );
		var previous = crown;
		// A raised midrib and overlapping tapered leaflets give the crown a serrated silhouette.
		for ( int i = 0; i < 4; i++ )
		{
			float t = (i + 1) / 4f;
			var next = crown + axis * length * t + Vector3.Up * length * (0.93f * t - 1.28f * t * t);
			float halfWidth = length * (0.035f + MathF.Sin( (i + 0.5f) / 4 * MathF.PI ) * 0.14f);
			var edge = previous * 0.32f + next * 0.68f - Vector3.Up * halfWidth * 0.36f;
			var inner = across * (halfWidth * 0.34f);
			var leftTip = edge + across * halfWidth + axis * length * 0.16f;
			var rightTip = edge - across * halfWidth + axis * length * 0.16f;
			LeafFace( previous, next, edge + inner, color );
			LeafFace( previous, edge - inner, next, color );
			LeafFace( previous, edge + inner, leftTip, color );
			LeafFace( previous, rightTip, edge - inner, color );
			previous = next;
		}
	}

	private void Leaf( Vector3 root, Vector3 axis, float length, float height, float width, Color color )
	{
		var middle = root + axis * (length * 0.4f) + Vector3.Up * (height * 0.65f);
		var tip = root + axis * length + Vector3.Up * height;
		var side = Across( axis ) * width;
		var ridge = middle + Vector3.Up * width;
		LeafFace( root, ridge, middle + side, color );
		LeafFace( ridge, tip, middle + side, color );
		LeafFace( root, middle - side, ridge, color );
		LeafFace( ridge, middle - side, tip, color );
	}

	private void Flower( Vector3 root, Color color )
	{
		var center = root + Vector3.Up * Range( 26, 40 );
		Trunk( root, center, 1.2f, 0.9f, PalmGreen, false, 3 );
		for ( int i = 0; i < 5; i++ )
		{
			var axis = Direction( i * MathF.Tau / 5 );
			var tip = center + axis * 8 + Vector3.Up * 3;
			var side = Across( axis ) * 3.8f;
			LeafFace( center, tip - side, tip + side, color );
		}
	}

	private void LeafFace( Vector3 a, Vector3 b, Vector3 c, Color color )
	{
		mesh.Triangle( a, b, c, color, false );
		mesh.Triangle( c, b, a, color, false );
	}

	private void Field()
	{
		var center = terrain.FieldCenter;
		var axis = terrain.BeachDirection;
		var across = Across( axis );
		var half = terrain.FieldHalfSize;
		// Inset the raised bed into the flat part of the graded terrace.
		float length = half.x * 0.84f;
		float width = half.y * 0.84f;
		var a = Ground( center - axis * length - across * width ) + Vector3.Up * 2;
		var b = Ground( center + axis * length - across * width ) + Vector3.Up * 2;
		var c = Ground( center + axis * length + across * width ) + Vector3.Up * 2;
		var d = Ground( center - axis * length + across * width ) + Vector3.Up * 2;
		Quad( a, b, c, d, Soil, false );
		int rows = Math.Clamp( (int)(width * 2 / 32), 3, 12 );
		float spacing = width * 2 / rows;
		for ( int row = 0; row < rows; row++ )
		{
			float offset = (row + 0.5f) * spacing - width;
			var start = Ground( center + across * offset - axis * length * 0.92f ) + Vector3.Up * 3;
			var end = Ground( center + across * offset + axis * length * 0.92f ) + Vector3.Up * 3;
			var side = across * spacing * 0.29f;
			var crest = Vector3.Up * 4;
			Quad( start - side, end - side, end + crest, start + crest, Furrow, false );
			Quad( start + crest, end + crest, end + side, start + side, Furrow, false );
			if ( row > 1 ) continue;
			int sprouts = Math.Clamp( (int)(length / 20), 3, 12 );
			for ( int i = 0; i < sprouts; i++ )
			{
				var sprout = Vector3.Lerp( start, end, (i + 0.5f) / sprouts ) + crest;
				float heading = Range( 0, MathF.Tau );
				for ( int leaf = 0; leaf < 3; leaf++ )
					Leaf( sprout, Direction( heading + leaf * MathF.Tau / 3 ), 8, 13, 3, LeafGreen );
			}
		}
	}

	private void Dock()
	{
		var beach = terrain.BeachDirection;
		float beachAngle = MathF.Atan2( beach.y, beach.x );
		// On a small cove move farther around the beach so the ramp never sits on its centerline.
		float offset = Math.Clamp( 345f / terrain.SeaRadius( beachAngle ), 0.29f, 0.44f );
		float angle = beachAngle + offset;
		var axis = Direction( angle );
		var across = Across( axis );
		var shore = axis * terrain.SeaRadius( angle );
		const float halfWidth = 46;
		const float deckHeight = 88;
		const float inner = -110;
		const float rampEnd = 112;
		const float outer = 300;
		var start = Ground( shore + axis * inner );
		var knee = (shore + axis * rampEnd).WithZ( deckHeight );
		var head = (shore + axis * outer).WithZ( deckHeight );
		Boardwalk( start, knee, across, halfWidth );
		Boardwalk( knee, head, across, halfWidth );
		// The hammerhead grows away from the landing lane. Ships can lie parallel to its outer face.
		var headCenter = head + axis * 41;
		Boardwalk( headCenter - across * halfWidth, headCenter + across * 191, -axis, 41 );
		foreach ( float distance in new[] { 6f, rampEnd, outer - 12 } )
		{
			float t = Math.Clamp( (distance - inner) / (rampEnd - inner), 0, 1 );
			float deck = start.z + (deckHeight - start.z) * t;
			for ( int side = -1; side <= 1; side += 2 )
				Piling( shore + axis * distance + across * (halfWidth + 3) * side, deck, false );
		}
		for ( int end = 0; end < 2; end++ )
		{
			var endCenter = headCenter + across * (end == 0 ? -halfWidth + 9 : 182);
			Piling( endCenter + axis * 30, deckHeight, true );
			Piling( endCenter - axis * 30, deckHeight, true );
		}
	}

	private void Boardwalk( Vector3 from, Vector3 to, Vector3 across, float halfWidth )
	{
		int count = Math.Max( 1, (int)MathF.Ceiling( (to - from).Length / 19 ) );
		var side = across * halfWidth;
		var drop = Vector3.Down * 9;
		var left = from + side;
		var right = from - side;
		for ( int i = 0; i < count; i++ )
		{
			var next = Vector3.Lerp( from, to, (i + 1f) / count );
			var nextLeft = next + side;
			var nextRight = next - side;
			Quad( right, nextRight, nextLeft, left, i % 3 == 0 ? Timber : Board );
			Quad( right + drop, nextRight + drop, nextRight, right, Timber );
			Quad( nextLeft + drop, left + drop, left, nextLeft, Timber );
			right = nextRight;
			left = nextLeft;
		}
		Quad( from + side + drop, from - side + drop, from - side, from + side, Bark );
		Quad( to - side + drop, to + side + drop, to + side, to - side, Bark );
	}

	private void Piling( Vector3 xy, float deckHeight, bool mooring )
	{
		var top = xy.WithZ( deckHeight + (mooring ? 23 : 6) );
		Trunk( Ground( xy ) - Vector3.Up * 15, top, 8, 7, Bark );
		if ( mooring ) Trunk( top - Vector3.Up * 14, top - Vector3.Up * 7, 8.5f, 8.5f, Rope, false );
	}

	private void Trunk( Vector3 from, Vector3 to, float bottomRadius, float topRadius, Color color, bool solid = true, int sides = 7 )
	{
		for ( int i = 0; i < sides; i++ )
		{
			var a = Direction( i * MathF.Tau / sides );
			var b = Direction( (i + 1) * MathF.Tau / sides );
			var topA = to + a * topRadius;
			var topB = to + b * topRadius;
			Quad( from + a * bottomRadius, from + b * bottomRadius, topB, topA, color, solid );
			mesh.Triangle( to, topA, topB, color, solid );
		}
	}

	private void Quad( Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, bool solid = true )
	{
		mesh.Triangle( a, b, c, color, solid );
		mesh.Triangle( a, c, d, color, solid );
	}
}
