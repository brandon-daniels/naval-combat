using System;

namespace NavalCombat;

/// <summary>World-space gust ribbons driven by the same wind as the sails.</summary>
public sealed class WindStreaks : Component
{
	[Property] public SailingWind Wind { get; set; }
	[Property] public ArcadeOcean Ocean { get; set; }
	[Property] public GameObject FollowTarget { get; set; }
	[Property] public float DriftSpeed { get; set; } = 420;
	[Property] public float StreakLength { get; set; } = 130;
	private const int Count = 48;
	private const int Segments = 8;
	private readonly Vector3[] positions = new Vector3[Count];
	private readonly float[] ages = new float[Count];
	private Vertex[] vertices;
	private Mesh mesh;
	private ModelRenderer renderer;
	private const float Lifetime = 4;

	protected override void OnStart()
	{
		vertices = new Vertex[Count * Segments * 8];
		var indices = new int[Count * Segments * 24];
		int n = 0;
		for ( int q = 0; q < vertices.Length; q += 4 )
		{
			indices[n++] = q; indices[n++] = q + 1; indices[n++] = q + 2;
			indices[n++] = q; indices[n++] = q + 2; indices[n++] = q + 3;
			indices[n++] = q + 2; indices[n++] = q + 1; indices[n++] = q;
			indices[n++] = q + 3; indices[n++] = q + 2; indices[n++] = q;
		}
		mesh = new Mesh( Material.Load( "materials/ocean/naval_wind.vmat" ) );
		mesh.CreateVertexBuffer<Vertex>( vertices.Length, vertices );
		mesh.CreateIndexBuffer( indices.Length, indices );
		mesh.Bounds = new BBox( new Vector3( -4000, -4000, -1000 ), new Vector3( 4000, 4000, 1500 ) );
		renderer = GameObject.AddComponent<ModelRenderer>();
		renderer.Model = Model.Builder.AddMesh( mesh ).Create();
		renderer.Tint = new Color( 0.8f, 0.95f, 1 );
		for ( int i = 0; i < Count; i++ )
		{
			Respawn( i );
			ages[i] = i / (float)Count * Lifetime;
		}
	}

	private void Respawn( int i )
	{
		var center = FollowTarget.IsValid() ? FollowTarget.WorldPosition : Vector3.Zero;
		// Deterministic scattered gusts avoid allocations and do not move with the ship.
		float phase = i * 2.39996f + Time.Now * 0.37f;
		float radius = 450 + (i * 137 % 1200);
		positions[i] = center.WithZ( Ocean.IsValid() ? Ocean.SeaLevel : 0 )
			+ new Vector3( MathF.Cos( phase ) * radius, MathF.Sin( phase ) * radius, 120 + i * 79 % 380 );
		ages[i] = 0;
	}

	protected override void OnUpdate()
	{
		if ( mesh is null || !renderer.IsValid() ) return;
		float strength = Wind.IsValid() && Wind.Enabled ? Wind.CurrentStrength : 0;
		renderer.Enabled = strength > 0.01f;
		if ( strength <= 0.01f ) return;
		var direction = Wind.Direction;
		var side = Vector3.Cross( direction, Vector3.Up ).Normal;
		WorldPosition = FollowTarget.IsValid() ? FollowTarget.WorldPosition.WithZ( 0 ) : Vector3.Zero;
		int n = 0;
		for ( int i = 0; i < Count; i++ )
		{
			ages[i] += Time.Delta;
			if ( ages[i] >= Lifetime || (positions[i] - WorldPosition).WithZ( 0 ).Length > 2600 ) Respawn( i );
			positions[i] += direction * Math.Max( 0, DriftSpeed ) * strength * Time.Delta;
			float fade = Math.Clamp( Math.Min( ages[i], Lifetime - ages[i] ) * 2, 0, 1 ) * Math.Min( strength, 1 );
			for ( int j = 0; j < Segments; j++ )
			{
				float a = j / (float)Segments, b = (j + 1) / (float)Segments;
				var p = Point( i, a, direction, side );
				var q = Point( i, b, direction, side );
				float wa = MathF.Sin( a * MathF.PI ) * 2.5f * fade;
				float wb = MathF.Sin( b * MathF.PI ) * 2.5f * fade;
				Quad( ref n, p, q, Vector3.Up, wa, wb );
				Quad( ref n, p, q, side, wa, wb );
			}
		}
		mesh.SetVertexBufferData<Vertex>( vertices );
	}

	private Vector3 Point( int i, float t, Vector3 direction, Vector3 side ) => positions[i] - WorldPosition
		- direction * ((1 - t) * Math.Max( 1, StreakLength ))
		+ side * (MathF.Sin( t * 4 + i ) * 8) + Vector3.Up * (MathF.Sin( t * 5 + i ) * 5);

	private void Quad( ref int n, Vector3 a, Vector3 b, Vector3 axis, float wa, float wb )
	{
		vertices[n++] = new Vertex( a - axis * wa, Vector3.Up, Vector3.Left, Vector4.Zero );
		vertices[n++] = new Vertex( b - axis * wb, Vector3.Up, Vector3.Left, Vector4.Zero );
		vertices[n++] = new Vertex( b + axis * wb, Vector3.Up, Vector3.Left, Vector4.Zero );
		vertices[n++] = new Vertex( a + axis * wa, Vector3.Up, Vector3.Left, Vector4.Zero );
	}
}

