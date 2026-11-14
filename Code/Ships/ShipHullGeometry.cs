using System;

namespace NavalCombat;

internal static class ShipHullGeometry
{
	private static readonly Vector2[] Stations = { new( -9, 1.95f ), new( -7.5f, 2.75f ),
		new( -5, 3.05f ), new( 0, 3.2f ), new( 4, 2.8f ), new( 7, 1.7f ), new( 8.7f, 0.12f ) };
	internal static float InnerWidth( float y, float z )
	{
		float width = Stations[0].y;
		for ( int i = 1; i < Stations.Length; i++ )
		{
			var a = Stations[i - 1]; var b = Stations[i];
			width = a.y + (b.y - a.y) * Math.Clamp( (y - a.x) / (b.x - a.x), 0, 1 );
			if ( y <= b.x ) break;
		}
		float factor = z < 0.95f ? 0.55f + 0.25f * Math.Clamp( z / 0.95f, 0, 1 )
			: 0.8f + 0.2f * Math.Clamp( (z - 0.95f) / 1.3f, 0, 1 );
		return width * factor - 0.16f;
	}
}
