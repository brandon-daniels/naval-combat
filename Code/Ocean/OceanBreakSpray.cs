using System;

namespace NavalCombat;

/// <summary>Short ballistic fans thrown off intersecting breaking fronts, drawn in one local mesh.</summary>
internal sealed class OceanBreakSpray
{
	private const float Step = 0.25f;
	private const float Cell = 440;
	private const int Limit = 14;
	private const int Fingers = 12;
	private const int CardsPerFinger = 8;
	private const int FoamColumns = 6;
	private const int FoamRows = 4;
	private const int VertexLimit = Limit * (Fingers * CardsPerFinger + (FoamColumns - 1) * (FoamRows - 1)) * 4;
	private const float Gravity = 540;
	private readonly Burst[] bursts = new Burst[Limit];
	private Vertex[] vertices = new Vertex[VertexLimit];
	private readonly Vector3[] foamPoints = new Vector3[FoamColumns * FoamRows];
	private readonly List<ArcadeShip> nearbyShips = new();
	private int lastStep = int.MinValue;
	private (OceanWaveField Field, float Height, float Sharpness, float Level, float Storm, float Foam) lastSea;
	private int previousVertices;
	private Color32 particleColor;
	private GameObject visual;
	private ModelRenderer renderer;
	private Mesh mesh;

	private struct Burst
	{
		public Vector3 Position, Travel;
		public float Strength, Born;
		public uint Seed;
	}

	public void Update( ArcadeOcean ocean, OceanWaveField field, Vector3 center, float time, float sharpness, float amplitude,
		float daylight, float storm, float foam )
	{
		if ( vertices is null || vertices.Length != VertexLimit )
		{
			Clear();
			vertices = new Vertex[VertexLimit];
		}
		var sea = (field, amplitude, sharpness, ocean.SeaLevel, storm, foam);
		if ( lastSea != sea )
		{
			lastSea = sea;
			lastStep = int.MinValue;
			Array.Clear( bursts );
			if ( mesh is not null ) UpdateBounds( ocean.SeaLevel, MathF.Abs( amplitude ) * field.MaximumAmplitude );
		}
		if ( MathF.Abs( amplitude ) < 2f || sharpness < 0.1f || foam < 0.07f )
		{
			if ( lastStep != int.MinValue ) Array.Clear( bursts );
			if ( renderer.IsValid() ) renderer.Enabled = false;
			lastStep = int.MinValue;
			return;
		}

		int tick = (int)MathF.Floor( time / Step );
		if ( tick != lastStep )
		{
			nearbyShips.Clear();
			foreach ( var ship in ocean.Scene.GetAllComponents<ArcadeShip>() )
			{
				if ( ship.Active && ship.WalkableModelReady && (ship.WorldPosition - center).WithZ( 0 ).LengthSquared < 3200f * 3200f )
					nearbyShips.Add( ship );
			}
			// Reconstruct recent events when an editor view opens or a peer joins mid-wave.
			int start = lastStep == int.MinValue || tick < lastStep ? tick - 5 : Math.Max( lastStep + 1, tick - 5 );
			if ( tick < lastStep ) Array.Clear( bursts );
			for ( int i = start; i <= tick; i++ ) SampleCrossings( ocean, center, i, time, storm, foam );
			lastStep = tick;
		}

		int used = 0;
		for ( int i = 0; i < bursts.Length; i++ )
		{
			ref var burst = ref bursts[i];
			float age = time - burst.Born;
			if ( burst.Strength <= 0 || age < 0 || age > 1.6f || Occupied( burst.Position ) ) continue;
			DrawBurst( ocean, burst, center, age, time, ref used );
		}
		if ( used == 0 )
		{
			if ( renderer.IsValid() ) renderer.Enabled = false;
			return;
		}
		if ( mesh is null || !renderer.IsValid() ) Build( ocean, MathF.Abs( amplitude ) * field.MaximumAmplitude );
		visual.WorldPosition = center;
		renderer.Enabled = true;
		if ( used < previousVertices ) Array.Clear( vertices, used, previousVertices - used );
		mesh.SetVertexBufferData<Vertex>( vertices.AsSpan( 0, Math.Max( used, previousVertices ) ) );
		previousVertices = used;
		if ( renderer.SceneObject.IsValid() )
			renderer.SceneObject.Attributes.Set( "SprayLight", daylight * (1 - 0.32f * storm) );
	}

	private void SampleCrossings( ArcadeOcean ocean, Vector3 center, int tick, float now, float storm, float foam )
	{
		int cx = (int)MathF.Floor( center.x / Cell );
		int cy = (int)MathF.Floor( center.y / Cell );
		for ( int y = cy - 5; y <= cy + 5; y++ )
		for ( int x = cx - 5; x <= cx + 5; x++ )
		{
			// Each world cell gets a different candidate position and birth time every step.
			uint hash = Hash( (uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)tick * 83492791u ^ (uint)ocean.WaveSeed );
			var point = new Vector3( (x + Unit( hash )) * Cell, (y + Unit( Hash( hash + 1 ) )) * Cell, 0 );
			if ( (point - center).WithZ( 0 ).LengthSquared > 2100f * 2100f ) continue;
			float birth = (tick - Unit( Hash( hash + 2 ) )) * Step;
			if ( now - birth > 1.6f || !ocean.TrySampleBreakingCrossing( point, birth, out float crossing, out var direction ) ) continue;
			if ( crossing < 0.13f - 0.055f * storm || Occupied( point ) ) continue;
			float strength = Math.Clamp( MathF.Sqrt( crossing ) * MathF.Sqrt( foam ), 0.13f, 1f );
			point.z = ocean.HeightAt( point, birth ) + 2;
			int slot = AvailableSlot( point, birth );
			if ( slot < 0 ) continue;
			// Start above the islands and test down to the wave skin; swells over deep
			// water have no collider. A covered crest under land or a deck cannot erupt.
			var ground = ocean.Scene.Trace.Ray( point + Vector3.Up * 2400, point - Vector3.Up * 2 ).Run();
			if ( ground.Hit || ground.StartedSolid ) continue;
			bursts[slot] = new Burst
			{
				Position = point, Travel = new Vector3( direction.x, direction.y, 0 ),
				Born = birth, Strength = strength, Seed = hash
			};
		}
	}

	private int AvailableSlot( Vector3 point, float birth )
	{
		int available = -1;
		for ( int i = 0; i < bursts.Length; i++ )
		{
			ref var other = ref bursts[i];
			if ( other.Strength > 0 && birth - other.Born < 1.2f && (point - other.Position).LengthSquared < 140 * 140 ) return -1;
			if ( available < 0 && (other.Strength <= 0 || birth - other.Born > 1.6f) ) available = i;
		}
		return available;
	}

	private bool Occupied( Vector3 point )
	{
		foreach ( var ship in nearbyShips )
		{
			if ( !ship.IsValid() ) continue;
			var local = ship.WorldRotation.Inverse * (point - ship.WorldPosition).WithZ( 0 );
			float scale = ship.ShipModelScale;
			if ( Math.Abs( local.x ) < 7.5f * scale + 65 && Math.Abs( local.y ) < 2.0f * scale + 65 ) return true;
		}
		return false;
	}

	private void DrawBurst( ArcadeOcean ocean, Burst burst, Vector3 center, float age, float time, ref int used )
	{
		var forward = burst.Travel;
		var side = new Vector3( -forward.y, forward.x, 0 );
		float size = 0.65f + 0.65f * burst.Strength;
		float handed = Unit( burst.Seed ) > 0.5f ? 1 : -1;
		float bend = 0.6f + 0.7f * Unit( Hash( burst.Seed + 18 ) );
		float span = size * (100 + 70 * Unit( Hash( burst.Seed + 271 ) ));
		var origin = burst.Position - center;
		DrawImpactFoam( ocean, burst, center, time, age, span, side, handed, ref used );
		Vector3 lastRoot = default, lastBelly = default, lastLip = default;
		float lastU = 0;
		for ( int i = 0; i < Fingers; i++ )
		{
			uint seed = Hash( burst.Seed + (uint)i * 37 );
			float u = (i + 0.65f * Unit( seed )) / Fingers;
			// A breaking section sweeps along a crooked crest. Launches share a
			// prevailing lateral velocity rather than fanning out from a central point.
			float along = (u - 0.40f) * handed;
			var basePoint = origin + side * (along * span)
				+ forward * (size * (20 * u * u + 11 * MathF.Sin( u * 5.7f + bend )));
			float t = Math.Max( 0, age - 0.14f * u - 0.045f * Unit( Hash( seed + 1 ) ) );
			float lobes = Math.Clamp( 0.4f + 0.5f * MathF.Sin( u * 7.4f + bend ) + 0.25f * Unit( seed ), 0.18f, 1 );
			float v = size * (99 + 44 * lobes + 34 * Unit( Hash( seed + 2 ) ));
			var flow = size * (forward * (138 + 72 * lobes) + side * handed * (47 + 38 * u));
			float flight = 2 * v / Gravity;
			float sheetLife = 0.40f + 0.075f * lobes;
			float web = Math.Clamp( t * 15, 0, 1 ) * Math.Clamp( (sheetLife - t) * 8, 0, 1 );
			particleColor = new Color( Unit( burst.Seed ), lobes, 1, web );

			// Sample the skin under each foot. At the end of the splash the sheet
			// collapses into the local crest, leaving no long tether to a receding wave.
			var foot = basePoint + flow * (0.18f * Math.Min( t, sheetLife ));
			if ( age < 0.67f )
			{
				foot.z = ocean.HeightAt( foot + center, time ) - center.z - 3;
				float reach = Math.Min( t, 0.32f ) * web;
				var translation = flow * reach;
				float ballisticLift = Math.Max( 0, (v - 0.5f * Gravity * t) * t ) * web;
				float lift = Math.Min( 0.54f * translation.Length, ballisticLift * (0.64f + 0.29f * lobes) );
				var lip = foot + translation + side * (handed * (lobes - 0.60f) * 30 * web);
				lip.z = ocean.HeightAt( lip + center, time ) - center.z - 2 + lift;
				var belly = Vector3.Lerp( foot, lip, 0.48f ) - forward * (5 * lobes * web);
				belly.z = ocean.HeightAt( belly + center, time ) - center.z - 2 + lift * 0.58f;
				if ( i > 0 )
				{
					Quad( ref used, lastRoot, foot, belly, lastBelly, lastU, u, 0, 0.53f );
					Quad( ref used, lastBelly, belly, lip, lastLip, lastU, u, 0.53f, 1 );
				}
				lastRoot = foot; lastBelly = belly; lastLip = lip; lastU = u;
			}

			if ( t <= 0 || t >= 1.22f ) continue;
			basePoint.z = ocean.HeightAt( basePoint + center, burst.Born ) - center.z;
			Vector3 Path( float s ) => basePoint + flow * s + Vector3.Up * ((v - 0.5f * Gravity * s) * s);
			var widthAxis = (side + forward * (0.20f * handed)).Normal;
			// Fast spray tears out of small sections of the sheet. Mist expands and
			// drifts while droplets follow independent ballistic trajectories.
			float mistAge = t - 0.09f - 0.12f * Unit( Hash( seed + 16 ) );
			float mistLife = 0.65f + 0.32f * Unit( Hash( seed + 21 ) );
			if ( mistAge > 0 && mistAge < mistLife )
			{
				float phase = mistAge / mistLife;
				float opacity = MathF.Sin( MathF.PI * phase ) * (0.38f + 0.34f * burst.Strength);
				float spread = size * (7 + 18 * phase) * (0.72f + 0.64f * Unit( seed ));
				var puff = basePoint + flow * (0.19f * (1 - MathF.Exp( -4.8f * mistAge )));
				puff += (forward * (75 + 43 * lobes) + side * handed * (55 * Unit( seed ) - 12)) * mistAge;
				puff.z += size * (46 * lobes + 62) * (1 - MathF.Exp( -4 * mistAge )) - 58 * mistAge * mistAge;
				var wide = (side * handed + forward * (0.47f - 1.1f * Unit( seed ))).Normal * (spread * 1.4f);
				var tall = Vector3.Up * (spread * 0.85f) + forward * (spread * 0.60f);
				particleColor = new Color( Unit( Hash( seed + 7 ) ), phase, 1, opacity );
				Quad( ref used, puff - wide - tall, puff + wide - tall, puff + wide + tall, puff - wide + tall, 6, 7, 0, 1 );
			}
			if ( t >= flight + 0.14f ) continue;
			particleColor = new Color( Unit( seed ), lobes, 1, 1 );
			float stripLife = 0.30f + 0.085f * Unit( Hash( seed + 4 ) );
			if ( Unit( Hash( seed + 5 ) ) > 0.60f && t < stripLife )
			{
				float fade = Math.Clamp( t * 12, 0, 1 ) * Math.Clamp( (stripLife - t) * 16, 0, 1 );
				float trail = Math.Min( t, 0.085f ) * fade;
				float width = size * (1.6f + 2.5f * lobes) * fade;
				var tail = Path( t - trail );
				var middle = Path( t - trail * 0.5f ) + side * (handed * trail * 33);
				var tip = Path( t );
				Strip( ref used, tail, middle, widthAxis, width, width * 0.62f, 2 );
				Strip( ref used, middle, tip, widthAxis, width * 0.62f, width * 0.14f, 2 );
			}

			for ( int j = 0; j < 3; j++ )
			{
				uint drop = Hash( seed + (uint)j * 129 + 9 );
				float dt = t - 0.075f - Unit( drop ) * 0.15f;
				if ( dt <= 0 ) continue;
				var kick = size * (side * (Unit( Hash( drop + 1 ) ) - 0.5f) * 60
					+ forward * Unit( Hash( drop + 2 ) ) * 75 + Vector3.Up * (9 + 40 * Unit( Hash( drop + 3 ) )) );
				var p = Path( t ) + kick * dt;
				var tangent = (flow + kick + Vector3.Up * (v - Gravity * t)).Normal;
				float fade = Math.Clamp( (flight + 0.14f - t) * 9, 0, 1 ) * Math.Clamp( dt * 15, 0, 1 );
				float radius = size * (0.45f + 0.9f * Unit( Hash( drop + 4 ) )) * fade;
				Strip( ref used, p - tangent * (radius * 1.4f), p + tangent * radius, widthAxis, radius, radius, 4 );
			}
		}
	}

	private void DrawImpactFoam( ArcadeOcean ocean, Burst burst, Vector3 center, float time, float age,
		float span, Vector3 side, float handed, ref int used )
	{
		// Aerated water remains where the lip lands and spreads after the airborne sheet falls.
		float phase = Math.Clamp( age / 1.55f, 0, 1 );
		float density = Math.Clamp( age * 6, 0, 1 ) * MathF.Pow( 1 - phase, 1.3f ) * burst.Strength;
		if ( density < 0.015f ) return;
		particleColor = new Color( Unit( burst.Seed ), phase, 1, density );
		var origin = burst.Position + burst.Travel * (10 + 48 * phase) + side * (handed * 18 * phase);
		for ( int r = 0; r < FoamRows; r++ )
		for ( int c = 0; c < FoamColumns; c++ )
		{
			float x = c / (float)(FoamColumns - 1) * 2 - 1;
			float y = r / (float)(FoamRows - 1) * 2 - 1;
			var p = origin + side * (handed * x * span * (0.43f + 0.25f * phase))
				+ burst.Travel * (y * (20 + 45 * phase) + x * (14 - x * 11));
			p.z = ocean.HeightAt( p, time ) + 1.8f;
			foamPoints[r * FoamColumns + c] = p - center;
		}
		for ( int r = 0; r < FoamRows - 1; r++ )
		for ( int c = 0; c < FoamColumns - 1; c++ )
		{
			int i = r * FoamColumns + c;
			Quad( ref used, foamPoints[i], foamPoints[i + 1], foamPoints[i + FoamColumns + 1], foamPoints[i + FoamColumns],
				8 + c / (float)(FoamColumns - 1), 8 + (c + 1) / (float)(FoamColumns - 1),
				r / (float)(FoamRows - 1), (r + 1) / (float)(FoamRows - 1) );
		}
	}

	private void Strip( ref int n, Vector3 a, Vector3 b, Vector3 side, float wa, float wb, float mode )
	{
		Quad( ref n, a - side * wa, a + side * wa, b + side * wb, b - side * wb, mode, mode + 1, 0, 1 );
	}

	private void Quad( ref int n, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float left, float right, float bottom, float top )
	{
		vertices[n++] = new Vertex( a, Vector3.Up, Vector3.Forward, new Vector4( left, bottom, 0, 0 ) ) { Color = particleColor };
		vertices[n++] = new Vertex( b, Vector3.Up, Vector3.Forward, new Vector4( right, bottom, 0, 0 ) ) { Color = particleColor };
		vertices[n++] = new Vertex( c, Vector3.Up, Vector3.Forward, new Vector4( right, top, 0, 0 ) ) { Color = particleColor };
		vertices[n++] = new Vertex( d, Vector3.Up, Vector3.Forward, new Vector4( left, top, 0, 0 ) ) { Color = particleColor };
	}

	private void UpdateBounds( float level, float height )
	{
		mesh.Bounds = new BBox( new Vector3( -2700, -2700, level - height - 250 ), new Vector3( 2700, 2700, level + height + 250 ) );
	}

	private void Build( ArcadeOcean ocean, float waveHeightBound )
	{
		visual = new GameObject( ocean.GameObject, true, "Breaking-wave spray" );
		visual.NetworkMode = NetworkMode.Never;
		visual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.Hidden;
		mesh = new Mesh( Material.Load( "materials/ocean/naval_break_spray.vmat" ) );
		UpdateBounds( ocean.SeaLevel, waveHeightBound );
		mesh.CreateVertexBuffer<Vertex>( vertices.Length, vertices );
		var indices = new int[vertices.Length / 4 * 6];
		for ( int q = 0, i = 0; q < vertices.Length; q += 4 )
		{
			indices[i++] = q; indices[i++] = q + 1; indices[i++] = q + 2;
			indices[i++] = q; indices[i++] = q + 2; indices[i++] = q + 3;
		}
		mesh.CreateIndexBuffer( indices.Length, indices );
		renderer = visual.AddComponent<ModelRenderer>();
		renderer.Model = Model.Builder.AddMesh( mesh ).Create();
	}

	private static uint Hash( uint value )
	{
		value ^= value >> 16;
		value *= 0x7feb352du;
		value ^= value >> 15;
		value *= 0x846ca68bu;
		return value ^ (value >> 16);
	}

	private static float Unit( uint value ) => (value >> 8) / 16777216f;

	public void Clear()
	{
		if ( visual.IsValid() ) visual.Destroy();
		visual = null;
		renderer = null;
		nearbyShips.Clear();
		Array.Clear( bursts );
		mesh = null;
		lastStep = int.MinValue;
		previousVertices = 0;
	}
}
