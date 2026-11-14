namespace NavalCombat;

/// <summary>Pass the nearest authored and generated island shelves to the water material.</summary>
internal sealed class OceanShoals
{
	private const int Limit = 4;
	private readonly Component[] nearestShorelines = new Component[Limit];
	private readonly float[] distances = new float[Limit];
	private float lastScan = float.NegativeInfinity;
	private Vector3 lastCenter;

	public void Apply( ArcadeOcean ocean, ModelRenderer renderer )
	{
		float elapsed = Time.Now - lastScan;
		if ( ocean.Scene.IsEditor || elapsed < 0 || elapsed >= 1f
			|| lastCenter.DistanceSquared( ocean.WorldPosition ) >= 800f * 800f )
		{
			lastScan = Time.Now;
			lastCenter = ocean.WorldPosition;
			System.Array.Clear( nearestShorelines );
			System.Array.Fill( distances, float.MaxValue );
			foreach ( var band in ocean.Scene.GetAllComponents<IslandSurface>() )
			{
				if ( Usable( band, ocean.SeaLevel ) ) Consider( band );
			}
			foreach ( var island in ocean.Scene.GetAllComponents<ProceduralIsland>() )
			{
				if ( Usable( island ) ) Consider( island );
			}
		}

		// Discovery is throttled; rerolls and transform edits refresh existing shelves every frame.
		var attributes = renderer.SceneObject.Attributes;
		for ( int i = 0; i < Limit; i++ )
		{
			var shore = nearestShorelines[i];
			attributes.Set( $"OceanShelf{i}", Vector4.Zero );
			attributes.Set( $"OceanIslandInfo{i}", Vector4.Zero );
			if ( shore is IslandSurface band && Usable( band, ocean.SeaLevel ) )
			{
				var origin = band.WorldPosition;
				var scale = band.WorldScale;
				var axis = band.WorldRotation.Forward.WithZ( 0 ).Normal;
				attributes.Set( $"OceanShelf{i}", new Vector4( origin.x, origin.y, band.Radius * scale.x, band.Radius * scale.y ) );
				attributes.Set( $"OceanRing{i}", new Vector4( band.OuterScale, band.InnerScale, origin.z + band.LowerHeight * scale.z,
					origin.z + band.UpperHeight * scale.z ) );
				attributes.Set( $"OceanShelfAxis{i}", new Vector3( axis.x, axis.y, band.Phase ) );
			}
			else if ( shore is ProceduralIsland island && Usable( island ) )
			{
				var terrain = island.Terrain;
				var transform = island.WorldTransform;
				var origin = transform.PointToLocal( Vector3.Zero );
				var dx = transform.PointToLocal( Vector3.Forward ) - origin;
				var dy = transform.PointToLocal( Vector3.Left ) - origin;
				var dz = transform.PointToLocal( Vector3.Up ) - origin;
				attributes.Set( $"OceanIslandLocalX{i}", new Vector4( dx.x, dy.x, dz.x, origin.x ) );
				attributes.Set( $"OceanIslandLocalY{i}", new Vector4( dx.y, dy.y, dz.y, origin.y ) );
				attributes.Set( $"OceanIslandLocalZ{i}", new Vector4( dx.z, dy.z, dz.z, origin.z ) );
				attributes.Set( $"OceanIslandInfo{i}", new Vector4( terrain.Radius, System.MathF.Atan2( terrain.BeachDirection.y, terrain.BeachDirection.x ),
					terrain.CovePhase, island.WorldScale.z ) );
				attributes.Set( $"OceanIslandPhases{i}", terrain.CoastPhases );
				attributes.Set( "OceanIslandShelfOffsets", terrain.ShelfOffsets );
				attributes.Set( "OceanIslandShelfLevels", terrain.ShelfLevels );
			}
		}
	}

	private void Consider( Component shore )
	{
		float distance = shore.WorldPosition.DistanceSquared( lastCenter );
		if ( distance >= distances[Limit - 1] ) return;
		int slot = Limit - 1;
		while ( slot > 0 && distances[slot - 1] > distance )
		{
			nearestShorelines[slot] = nearestShorelines[slot - 1];
			distances[slot] = distances[slot - 1];
			slot--;
		}
		nearestShorelines[slot] = shore;
		distances[slot] = distance;
	}

	private static bool Usable( Component shore ) => shore.IsValid() && shore.Active
		&& shore.WorldScale.x > 0 && shore.WorldScale.y > 0 && shore.WorldScale.z > 0
		&& (shore is not ProceduralIsland island || island.Terrain is not null);

	private static bool Usable( IslandSurface band, float seaLevel ) => Usable( band )
		&& band.Radius > 0 && band.OuterScale > band.InnerScale
		&& band.WorldPosition.z + band.LowerHeight * band.WorldScale.z < seaLevel
		&& band.WorldPosition.z + band.UpperHeight * band.WorldScale.z >= seaLevel;
}
