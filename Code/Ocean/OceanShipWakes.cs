using System;

namespace NavalCombat;

/// <summary>Local waterline foam and a fading trail following each moving hull's recorded path.</summary>
internal sealed class OceanShipWakes
{
	private const int ShipLimit = 8;
	private const int History = 52;
	private const int BowRows = 16;
	private const int Columns = 7;
	private const float TrailSeconds = 11;
	private const float Interval = 0.22f;
	private const int RowsPerShip = History + 1 + BowRows * 2;
	private readonly Dictionary<ArcadeShip, Track> tracks = new();
	private readonly List<ArcadeShip> removed = new();
	private readonly Vertex[] vertices = new Vertex[ShipLimit * RowsPerShip * Columns];
	private GameObject visual;
	private Mesh mesh;
	private ModelRenderer renderer;
	private float nextScan;
	private float lastUpdate = -1;

	private struct Mark
	{
		public Vector3 Center, Side;
		public float Width, Born, Power;
		public int Run;
	}

	private sealed class Track
	{
		public readonly Mark[] Marks = new Mark[History];
		public int Next, Count, Run;
		public Vector3 Position;
		public float Speed, LastMark = -100, LastMoving = -100;
	}

	public void Update( ArcadeOcean ocean, float time, float daylight, float storm )
	{
		if ( ocean.Scene.IsEditor ) return;
		if ( time < lastUpdate ) Clear();
		float dt = Math.Clamp( time - lastUpdate, 0, 0.25f );
		lastUpdate = time;
		var center = ocean.WorldPosition;
		if ( time >= nextScan )
		{
			nextScan = time + 0.35f;
			var ships = ocean.Scene.GetAllComponents<ArcadeShip>().Where( x => x.Active && x.Ocean == ocean && !x.IsSinking
				&& (x.WorldPosition - center).WithZ( 0 ).LengthSquared < 8500f * 8500f )
				.OrderBy( x => (x.WorldPosition - center).LengthSquared ).Take( ShipLimit ).ToArray();
			removed.Clear();
			foreach ( var ship in tracks.Keys ) if ( !ships.Contains( ship ) ) removed.Add( ship );
			foreach ( var ship in removed ) tracks.Remove( ship );
			foreach ( var ship in ships )
				if ( !tracks.ContainsKey( ship ) ) tracks.Add( ship, new Track { Position = ship.WorldPosition } );
		}

		Array.Clear( vertices );
		bool visible = false;
		int offset = 0;
		foreach ( var (ship, track) in tracks )
		{
			if ( ship.IsValid() && ship.Active && !ship.IsSinking )
				visible |= DrawShip( ocean, ship, track, time, dt, offset );
			offset += RowsPerShip * Columns;
		}
		if ( !visible )
		{
			if ( renderer.IsValid() ) renderer.Enabled = false;
			return;
		}
		if ( mesh is null || !renderer.IsValid() ) Build( ocean );
		visual.WorldPosition = center;
		renderer.Enabled = true;
		mesh.SetVertexBufferData<Vertex>( vertices );
		if ( renderer.SceneObject.IsValid() )
		{
			renderer.SceneObject.Attributes.Set( "WakeTime", time );
			renderer.SceneObject.Attributes.Set( "WakeLight", daylight * (1 - 0.35f * storm) );
		}
	}

	private bool DrawShip( ArcadeOcean ocean, ArcadeShip ship, Track track, float time, float dt, int start )
	{
		var position = ship.WorldPosition;
		var displacement = (position - track.Position).WithZ( 0 );
		track.Position = position;
		var heading = ship.WorldRotation.Forward.WithZ( 0 ).Normal;
		var side = new Vector3( -heading.y, heading.x, 0 );
		float scale = Math.Max( 1, ship.ShipModelScale );
		if ( displacement.LengthSquared > 500f * 500f )
		{
			track.Count = 0;
			track.Speed = 0;
			track.LastMoving = -100;
			return false;
		}
		if ( dt > 0.0001f )
		{
			// Proxy hull transform motion is interpolated locally. This also measures
			// coasting, towing, or motion while the sail is furled.
			float speed = Math.Max( 0, Vector3.Dot( displacement, heading ) / dt );
			track.Speed += (Math.Min( speed, 1600 ) - track.Speed) * (1 - MathF.Exp( -dt * 4 ));
		}
		float power = Math.Clamp( (track.Speed - 20) / 170, 0, 1 );
		bool wet = MathF.Abs( position.z + ship.ShipModelVerticalOffset + scale - ocean.HeightAt( position, time ) ) < 3.5f * scale;
		var stern = position - heading * (8.72f * scale);
		var head = new Mark { Center = stern, Side = side, Width = 2.10f * scale, Power = power, Born = time };
		if ( power > 0.015f && wet )
		{
			if ( time - track.LastMoving > 0.6f ) track.Run++;
			track.LastMoving = time;
			head.Run = track.Run;
			var previous = track.Marks[(track.Next + History - 1) % History];
			if ( time - track.LastMark >= Interval && (track.Count == 0 || previous.Run != track.Run || (head.Center - previous.Center).LengthSquared > 11 * 11) )
			{
				track.Marks[track.Next] = head;
				track.Next = (track.Next + 1) % History;
				track.Count = Math.Min( History, track.Count + 1 );
				track.LastMark = time;
			}
			for ( int sign = -1; sign <= 1; sign += 2 )
			{
				int bowStart = start + (History + 1 + (sign + 1) / 2 * BowRows) * Columns;
				for ( int r = 0; r < BowRows; r++ )
				{
					float u = r / (float)(BowRows - 1);
					float longitudinal = 8.75f - 17.65f * u;
					float breadth = ShipHullGeometry.InnerWidth( longitudinal, 2.25f ) + 0.12f;
					var root = position + heading * (longitudinal * scale) + side * (sign * breadth * scale);
					float taper = Math.Clamp( u * 10, 0, 1 ) * Math.Clamp( (1 - u) * 3.7f, 0, 1 );
					float width = scale * (0.12f + 0.55f * power + 0.48f * u) * taper;
					for ( int c = 0; c < Columns; c++ )
					{
						float v = c / (float)(Columns - 1);
						float edge = MathF.Sin( MathF.PI * v );
						var p = root + side * (sign * width * v);
						float surge = MathF.Sin( MathF.PI * MathF.Sqrt( u ) );
						float lift = 1 + power * surge * edge * edge * (7 + 0.22f * scale);
						float density = power * taper * (1 - 0.38f * u);
						vertices[bowStart + r * Columns + c] = FoamVertex( ocean, p, time, lift, v * 2 - 1, 2 + u, heading, density );
					}
				}
			}
		}
		while ( track.Count > 0 && time - track.Marks[(track.Next - track.Count + History) % History].Born > TrailSeconds ) track.Count--;
		if ( track.Count == 0 ) return wet && power > 0.015f;

		int rowCount = track.Count + (wet && power > 0.015f ? 1 : 0);
		for ( int r = 0; r < rowCount; r++ )
		{
			int index = (track.Next - track.Count + r + History) % History;
			var mark = r == track.Count ? head : track.Marks[index];
			float age = Math.Max( 0, time - mark.Born );
			float fade = Math.Clamp( 1 - age / TrailSeconds, 0, 1 );
			float width = mark.Width + age * (14 + 16 * mark.Power);
			float density = mark.Power * fade * Math.Clamp( r, 0, 1 );
			if ( r > 0 && track.Marks[(index + History - 1) % History].Run != mark.Run ) density = 0;
			if ( r + 1 < rowCount )
			{
				var next = r + 1 == track.Count ? head : track.Marks[(index + 1) % History];
				if ( next.Run != mark.Run ) density = 0;
			}
			for ( int c = 0; c < Columns; c++ )
			{
				float across = c / (float)(Columns - 1) * 2 - 1;
				var p = mark.Center + mark.Side * (across * width);
				float ridge = MathF.Exp( -18f * MathF.Pow( MathF.Abs( across ) - 0.68f, 2 ) );
				float lift = 1 + ridge * mark.Power * fade * 4.5f;
				var direction = new Vector3( mark.Side.y, -mark.Side.x, 0 );
				vertices[start + r * Columns + c] = FoamVertex( ocean, p, time, lift, across, 1 - fade, direction, density );
			}
		}
		// Collapse remaining rows to the final row. Unused faces have zero area.
		for ( int r = rowCount; r < History + 1; r++ )
			Array.Copy( vertices, start + (rowCount - 1) * Columns, vertices, start + r * Columns, Columns );
		return true;
	}

	private static Vertex FoamVertex( ArcadeOcean ocean, Vector3 p, float time, float lift, float across, float mode, Vector3 direction, float density )
	{
		p.z = ocean.HeightAt( p, time ) + lift;
		return new Vertex( p - ocean.WorldPosition, Vector3.Up, Vector3.Forward, new Vector4( across, mode, 0, 0 ) )
		{
			Color = new Color( 0.5f + 0.5f * direction.x, 0.5f + 0.5f * direction.y, 1, density )
		};
	}

	private void Build( ArcadeOcean ocean )
	{
		visual = new GameObject( ocean.GameObject, true, "Ship wakes" );
		visual.NetworkMode = NetworkMode.Never;
		visual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.Hidden;
		mesh = new Mesh( Material.Load( "materials/ocean/naval_ship_wake.vmat" ) );
		mesh.Bounds = new BBox( new Vector3( -17000, -17000, ocean.SeaLevel - 2500 ), new Vector3( 17000, 17000, ocean.SeaLevel + 2500 ) );
		mesh.CreateVertexBuffer<Vertex>( vertices.Length, vertices );
		var indices = new List<int>( ShipLimit * RowsPerShip * (Columns - 1) * 6 );
		for ( int i = 0; i < ShipLimit; i++ )
		{
			int start = i * RowsPerShip * Columns;
			AddStrip( indices, start, History + 1 );
			AddStrip( indices, start + (History + 1) * Columns, BowRows );
			AddStrip( indices, start + (History + 1 + BowRows) * Columns, BowRows );
		}
		mesh.CreateIndexBuffer( indices.Count, indices.ToArray() );
		renderer = visual.AddComponent<ModelRenderer>();
		renderer.Model = Model.Builder.AddMesh( mesh ).Create();
	}

	private static void AddStrip( List<int> indices, int start, int rows )
	{
		for ( int r = 0; r < rows - 1; r++ )
		for ( int c = 0; c < Columns - 1; c++ )
		{
			int a = start + r * Columns + c;
			indices.Add( a ); indices.Add( a + 1 ); indices.Add( a + Columns + 1 );
			indices.Add( a ); indices.Add( a + Columns + 1 ); indices.Add( a + Columns );
		}
	}

	public void Clear()
	{
		if ( visual.IsValid() ) visual.Destroy();
		visual = null;
		renderer = null;
		tracks.Clear();
		mesh = null;
		nextScan = 0;
		lastUpdate = -1;
	}
}
