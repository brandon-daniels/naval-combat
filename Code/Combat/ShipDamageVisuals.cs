using System;
using System.Collections.Generic;

namespace NavalCombat;

/// <summary>Persistent hull openings, broken edges, and local physics debris from host-authored impacts.</summary>
public sealed class ShipDamageVisuals : Component
{
	[Property] public int SplintersPerImpact { get; set; } = 14;
	[Property] public float DebrisLifetime { get; set; } = 7;
	[Property] public int DebrisLimit { get; set; } = 56;
	private GameObject effects;
	private readonly List<HullBreach> shown = new();
	private readonly List<GameObject> drops = new();
	private Model shardModel;
	private ModelRenderer hullRenderer;
	private readonly List<ShipDebris> debris = new();
	private readonly List<float> leakStrength = new();
	private readonly Dictionary<int, int> observedImpacts = new();
	private bool initialized;
	private float observationStartedAt;
	private int latestImpactRevision;
	private Mesh waterMesh;
	private Vertex[] waterVertices;
	private const int Segments = 12;
	private const int DropsPerHole = 8;
	public int VisibleHoleCount => shown.Count;
	public int ActiveDebrisCount => debris.Count( x => x.IsValid() );
	public int ImpactBursts { get; private set; }
	public int ActiveLeakCount => leakStrength.Count( x => x > 0.05f );

	protected override void OnUpdate()
	{
		var health = GetComponent<ShipHealth>();
		if ( !health.IsValid() || Scene.IsEditor ) return;
		if ( !initialized ) observationStartedAt = Time.Now;
		bool changed = shown.Count != health.HoleCount;
		for ( int i = 0; !changed && i < shown.Count; i++ )
			changed = !shown[i].Equals( health.Breaches[i] );
		debris.RemoveAll( x => !x.IsValid() );
		if ( changed || (health.HoleCount > 0 && !effects.IsValid()) )
		{
			if ( health.HoleCount == 0 ) ClearDebris();
			int newestRevision = latestImpactRevision;
			foreach ( var breach in health.Breaches )
			{
				bool newImpact = !observedImpacts.TryGetValue( breach.Sequence, out int previous ) || breach.ImpactRevision > previous;
				// Stable identity survives repairs and list reordering. Widening has a new
				// impact revision; rebuilding a patch or joining a damaged ship does not.
				if ( initialized && newImpact && breach.ImpactRevision > latestImpactRevision
					&& breach.CreatedAt >= observationStartedAt && Time.Now - breach.CreatedAt is >= 0 and < 0.75f ) Burst( breach );
				observedImpacts[breach.Sequence] = breach.ImpactRevision;
				newestRevision = Math.Max( newestRevision, breach.ImpactRevision );
			}
			latestImpactRevision = newestRevision;
			foreach ( int sequence in observedImpacts.Keys.Where( sequence => !health.Breaches.Any( breach => breach.Sequence == sequence ) ).ToArray() )
				observedImpacts.Remove( sequence );
			Rebuild( health );
		}
		initialized = true;
		UpdateHullMaterial();
		if ( waterMesh is null || shown.Count == 0 ) return;

		int n = 0;
		for ( int i = 0; i < shown.Count; i++ )
		{
			var breach = shown[i];
			float desired = health.GetBreachSubmersion( breach );
			leakStrength[i] += (desired - leakStrength[i]) * Math.Clamp( Time.Delta * 8, 0, 1 );
			float flow = leakStrength[i];
			float size = Math.Clamp( breach.Radius / 18, 0.5f, 2.5f );
			var inward = LeakNormal( breach );
			var side = Vector3.Cross( inward, MathF.Abs( inward.z ) > 0.9f ? Vector3.Left : Vector3.Up ).Normal;
			var across = Vector3.Cross( inward, side ).Normal;
			for ( int j = 0; j < Segments; j++ )
			{
				float a = j / (float)Segments, b = (j + 1) / (float)Segments;
				var p = JetPoint( breach, a * flow );
				var q = JetPoint( breach, b * flow );
				float wa = size * flow * (5 - a * 3) * (1 + 0.16f * MathF.Sin( Time.Now * 18 - a * 25 + breach.Sequence ));
				float wb = size * flow * (5 - b * 3) * (1 + 0.16f * MathF.Sin( Time.Now * 18 - b * 25 + breach.Sequence ));
				Quad( ref n, p, q, side, wa, wb );
				Quad( ref n, p, q, across, wa, wb );
			}
			for ( int j = 0; j < DropsPerHole; j++ )
			{
				float t = (Time.Now * 1.7f + j / (float)DropsPerHole + breach.Sequence * 0.13f) % 1;
				var drop = drops[i * DropsPerHole + j];
				drop.Enabled = flow > 0.05f;
				drop.LocalPosition = JetPoint( breach, t * flow ) + side * (MathF.Sin( j * 2.4f ) * t * 7 * flow * size);
				drop.LocalScale = Vector3.One * size * (0.035f + (1 - t) * 0.035f);
			}
		}
		waterMesh.SetVertexBufferData<Vertex>( waterVertices );
	}

	private Vector3 JetPoint( HullBreach breach, float t )
	{
		// Gravity is expressed in ship space so the water still falls down as the hull rocks.
		var gravity = WorldRotation.Inverse * Vector3.Down;
		float size = Math.Clamp( breach.Radius / 18, 0.5f, 2.5f );
		var mouth = LeakPosition( breach );
		var p = mouth + LeakNormal( breach ) * (t * (55 + 20 * size)) + gravity * (t * t * 48);
		// Keep the end of hold jets above the authored floor instead of spraying through it.
		var ship = GetComponent<ArcadeShip>();
		var health = GetComponent<ShipHealth>();
		float floor = health.IsValid() && health.FloodFraction > 0 ? health.FloodSurfaceLocalHeight
			: ship.IsValid() ? ship.ShipModelVerticalOffset + ship.ShipModelScale * 0.96f : -42;
		if ( mouth.z > floor ) p.z = Math.Max( floor, p.z );
		return p;
	}

	private static bool HasMappedRepairPoint( HullBreach breach ) => breach.RepairNormal.LengthSquared > 0.5f && breach.RepairPosition.Distance( breach.Interior ) > 4;
	// Four units brings the rim and mouth in front of the hold's raised timber ribs.
	private static Vector3 LeakPosition( HullBreach breach ) => HasMappedRepairPoint( breach ) ? breach.RepairPosition + breach.RepairNormal.Normal * 4 : breach.Interior;
	private static Vector3 LeakNormal( HullBreach breach ) => HasMappedRepairPoint( breach ) ? breach.RepairNormal.Normal : breach.InteriorNormal.Normal;

	private void Rebuild( ShipHealth health )
	{
		var previousFlow = new Dictionary<int, float>();
		for ( int i = 0; i < shown.Count && i < leakStrength.Count; i++ ) previousFlow[shown[i].Sequence] = leakStrength[i];
		ClearMarks();
		if ( health.HoleCount == 0 ) return;
		shardModel ??= ShipDebris.BuildSplinter();
		effects = LocalObject( GameObject, "Hull breaches and incoming water" );
		shown.AddRange( health.Breaches );
		foreach ( var breach in health.Breaches )
		{
			leakStrength.Add( previousFlow.GetValueOrDefault( breach.Sequence ) );
			for ( int j = 0; j < DropsPerHole; j++ )
			{
				var drop = LocalObject( effects, "Leak foam" );
				var renderer = drop.AddComponent<ModelRenderer>();
				renderer.Model = Model.Load( "models/dev/sphere.vmdl" );
				renderer.Tint = new Color( 0.8f, 0.97f, 1 );
				drops.Add( drop );
			}
		}
		waterVertices = new Vertex[shown.Count * Segments * 8];
		var indices = new int[shown.Count * Segments * 24];
		int k = 0;
		for ( int q = 0; q < waterVertices.Length; q += 4 )
		{
			indices[k++] = q; indices[k++] = q + 1; indices[k++] = q + 2;
			indices[k++] = q; indices[k++] = q + 2; indices[k++] = q + 3;
			indices[k++] = q + 2; indices[k++] = q + 1; indices[k++] = q;
			indices[k++] = q + 3; indices[k++] = q + 2; indices[k++] = q;
		}
		waterMesh = new Mesh( Material.Load( "materials/default.vmat" ) );
		waterMesh.CreateVertexBuffer<Vertex>( waterVertices.Length, waterVertices );
		waterMesh.CreateIndexBuffer( indices.Length, indices );
		waterMesh.Bounds = new BBox( new Vector3( -1200, -600, -300 ), new Vector3( 1200, 600, 1000 ) );
		var jet = LocalObject( effects, "Gushing water" ).AddComponent<ModelRenderer>();
		jet.Model = Model.Builder.AddMesh( waterMesh ).Create();
		jet.Tint = new Color( 0.2f, 0.72f, 0.95f );
	}

	private void Mark( Vector3 position, Vector3 normal, float radius, int seed, string name = "Splintered cannon hole" )
	{
		var mark = LocalObject( effects, name );
		mark.LocalPosition = position;
		mark.LocalRotation = Rotation.LookAt( normal );
		mark.LocalScale = Vector3.One * Math.Max( 0.1f, radius / 18 );
		var rim = LocalObject( mark, "Broken wood edge" ).AddComponent<ModelRenderer>();
		rim.Model = BuildRim( position, normal, mark.LocalScale.x );
		rim.Tint = new Color( 0.66f, 0.46f, 0.25f );
		// Raised broken fibers give the cut thickness and a silhouette from oblique views.
		for ( int i = 0; i < 9; i++ )
		{
			float a = i * MathF.Tau / 9;
			var edge = new Vector3( 2, MathF.Cos( a ) * 20, MathF.Sin( a ) * 20 );
			if ( CoveredByAnotherHole( position + mark.LocalRotation * (edge * mark.LocalScale.x), position ) ) continue;
			var tooth = LocalObject( mark, "Torn plank fiber" );
			tooth.LocalPosition = edge;
			tooth.LocalRotation = Rotation.LookAt( new Vector3( 0.4f, -MathF.Cos( a ), -MathF.Sin( a ) ) );
			tooth.LocalScale = new Vector3( 9 + 4 * MathF.Sin( seed + i * 3.7f ), 2, 2 );
			var renderer = tooth.AddComponent<ModelRenderer>();
			renderer.Model = shardModel;
			renderer.Tint = new Color( 0.70f, 0.49f, 0.27f );
		}
	}

	private void UpdateHullMaterial()
	{
		var ship = GetComponent<ArcadeShip>();
		hullRenderer = ship.IsValid() ? ship.GetComponent<ShipStructure>()?.HullRenderer : null;
		if ( !hullRenderer.IsValid() ) return;
		for ( int i = 0; i < ShipHealth.MaximumBreaches; i++ )
		{
			Vector4 mouth = Vector4.Zero, axis = Vector4.Zero;
			if ( i < shown.Count )
			{
				var breach = shown[i];
				var position = WorldTransform.PointToWorld( breach.Position );
				var interior = WorldTransform.PointToWorld( breach.Interior );
				var normal = (position - interior).Normal;
				mouth = new Vector4( position.x, position.y, position.z, Math.Max( 1, breach.Radius ) );
				axis = new Vector4( normal.x, normal.y, normal.z, Math.Clamp( position.Distance( interior ), 8, 110 ) );
			}
			hullRenderer.Attributes.Set( $"BreachMouth{i}", mouth );
			hullRenderer.Attributes.Set( $"BreachAxis{i}", axis );
		}
	}

	private void Burst( HullBreach breach )
	{
		ImpactBursts++;
		var ship = GetComponent<ArcadeShip>();
		var normal = WorldRotation * breach.Normal;
		var inward = WorldRotation * breach.InteriorNormal;
		var side = Vector3.Cross( normal, MathF.Abs( normal.z ) > 0.9f ? Vector3.Left : Vector3.Up ).Normal;
		var up = Vector3.Cross( side, normal ).Normal;
		var incoming = WorldRotation * breach.ImpactVelocity;
		float speed = Math.Clamp( incoming.Length * 0.23f, 180, 550 );
		var direction = incoming.Length > 1 ? incoming.Normal : -normal;
		int count = Math.Clamp( SplintersPerImpact, 0, 24 );
		for ( int i = 0; i < count; i++ )
		{
			while ( debris.Count >= Math.Clamp( DebrisLimit, 1, 96 ) )
			{
				if ( debris[0].IsValid() ) debris[0].GameObject.Destroy();
				debris.RemoveAt( 0 );
			}
			// Stable variation shared across peers; subsequent contacts are intentionally local.
			float a = i * 2.39996f + breach.Sequence * 0.71f;
			float variation = 0.5f + 0.5f * MathF.Sin( a * 4.13f );
			bool inside = i % 3 != 0;
			var outward = inside ? inward : normal;
			var origin = WorldTransform.PointToWorld( inside ? breach.Interior : breach.Position );
			var position = origin + outward * 9 + (side * MathF.Cos( a ) + up * MathF.Sin( a )) * breach.Radius * 0.4f;
			var inherited = ship.IsValid() && ship.Body.IsValid() ? ship.Body.GetVelocityAtPoint( origin ) : Vector3.Zero;
			var velocity = inherited + outward * speed * (0.5f + variation * 0.5f)
				+ (inside ? direction * speed * 0.3f : Vector3.Zero)
				+ side * MathF.Cos( a ) * speed * 0.6f + up * MathF.Sin( a ) * speed * 0.45f;
			var size = new Vector3( 12 + variation * 25, 2 + variation * 5, 2 + variation * 2 );
			var fragment = ShipDebris.Spawn( GameObject, ship?.Ocean, position, Rotation.LookAt( side + outward * variation ),
				size, velocity, new Vector3( MathF.Sin( a ), MathF.Cos( a ), 0.4f ) * (8 + variation * 14),
				Math.Clamp( DebrisLifetime, 1, 12 ), new Color( 0.42f + variation * 0.22f, 0.27f + variation * 0.19f, 0.12f + variation * 0.14f ) );
			if ( fragment.IsValid() ) debris.Add( fragment );
		}
	}

	private bool CoveredByAnotherHole( Vector3 point, Vector3 ownMouth )
	{
		foreach ( var breach in shown )
		{
			foreach ( var mouth in new[] { breach.Position, breach.Interior } )
			{
				if ( mouth.Distance( ownMouth ) < 1 ) continue;
				var delta = point - mouth;
				float depth = Vector3.Dot( delta, breach.Normal );
				if ( MathF.Abs( depth ) < 10 && (delta - breach.Normal * depth).Length < Math.Max( 1, breach.Radius ) ) return true;
			}
		}
		return false;
	}

	private Model BuildRim( Vector3 position, Vector3 normal, float scale )
	{
		var vertices = new List<Vertex>();
		var indices = new List<int>();
		var rotation = Rotation.LookAt( normal );
		for ( int i = 0; i < 16; i++ )
		{
			Vector3 Edge( int index, float radius )
			{
				float angle = index * MathF.PI / 8;
				float r = radius * (1 + 0.14f * MathF.Sin( (index % 16) * 7.31f ));
				return new Vector3( 1 + MathF.Sin( index * 3.1f ), MathF.Cos( angle ) * r, MathF.Sin( angle ) * r );
			}
			var a = Edge( i, 16 );
			var b = Edge( i, 21 );
			var c = Edge( i + 1, 21 );
			var d = Edge( i + 1, 16 );
			if ( CoveredByAnotherHole( position + rotation * ((a + b + c + d) * (scale / 4)), position ) ) continue;
			int v = vertices.Count;
			var faceNormal = Vector3.Cross( b - a, c - a ).Normal;
			foreach ( var p in new[] { a, b, c, d } )
				vertices.Add( new Vertex( p, faceNormal, Vector3.Up, Vector4.Zero ) );
			indices.AddRange( new[] { v, v + 1, v + 2, v, v + 2, v + 3, v + 2, v + 1, v, v + 3, v + 2, v } );
		}
		if ( vertices.Count == 0 ) return null;
		var mesh = new Mesh( Material.Load( "materials/default.vmat" ) );
		mesh.CreateVertexBuffer<Vertex>( vertices.Count, vertices.ToArray() );
		mesh.CreateIndexBuffer( indices.Count, indices.ToArray() );
		mesh.Bounds = new BBox( new Vector3( -2, -30, -30 ), new Vector3( 3, 30, 30 ) );
		return Model.Builder.AddMesh( mesh ).Create();
	}

	private static GameObject LocalObject( GameObject parent, string name )
	{
		var go = new GameObject( parent, true, name );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		return go;
	}

	private void Quad( ref int n, Vector3 a, Vector3 b, Vector3 axis, float wa, float wb )
	{
		waterVertices[n++] = new Vertex( a - axis * wa, Vector3.Up, Vector3.Left, Vector4.Zero );
		waterVertices[n++] = new Vertex( b - axis * wb, Vector3.Up, Vector3.Left, Vector4.Zero );
		waterVertices[n++] = new Vertex( b + axis * wb, Vector3.Up, Vector3.Left, Vector4.Zero );
		waterVertices[n++] = new Vertex( a + axis * wa, Vector3.Up, Vector3.Left, Vector4.Zero );
	}

	private void ClearMarks()
	{
		if ( effects.IsValid() ) effects.Destroy();
		effects = null;
		waterMesh = null;
		waterVertices = null;
		shown.Clear();
		drops.Clear();
		leakStrength.Clear();
	}

	private void ClearDebris()
	{
		foreach ( var fragment in debris ) if ( fragment.IsValid() ) fragment.GameObject.Destroy();
		debris.Clear();
	}

	private void Clear()
	{
		ClearMarks();
		ClearDebris();
		UpdateHullMaterial();
		initialized = false;
		observedImpacts.Clear();
		latestImpactRevision = 0;
	}

	protected override void OnDisabled() => Clear();
	protected override void OnDestroy() => Clear();
}
