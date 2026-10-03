using System;

namespace NavalCombat;

/// <summary>Cel shaded swells sharing their height and clock with ship buoyancy and foam.</summary>
public sealed class ArcadeOcean : Component, Component.ExecuteInEditor
{
	[Property] public float SeaLevel { get; set; } = 0;
	[Property] public float WaveHeight { get; set; } = 55;
	[Property] public float FoamStrength { get; set; } = 1;
	[Property] public GameObject FollowTarget { get; set; }

	private const int Cells = 128;
	private const float Spacing = 64;
	private Vertex[] vertices;
	private float[] gridCoordinates;
	private Mesh mesh;
	private ModelRenderer renderer;

	public float HeightAt( Vector3 position, float time )
	{
		SampleWave( position, time, out float height, out _ );
		return height;
	}

	private void SampleWave( Vector3 position, float time, out float height, out Vector3 normal )
	{
		// The material uses these same phases for crest placement and per-pixel lighting bands.
		float a = position.x * 0.0026f + position.y * 0.0012f + time * 0.85f;
		float b = position.x * -0.0018f + position.y * 0.0042f + time * 1.15f;
		float c = position.x * 0.007f - position.y * 0.003f + time * 1.8f;
		height = SeaLevel + WaveHeight * (MathF.Sin( a ) - 0.12f * MathF.Cos( 2 * a )
			+ 0.45f * MathF.Sin( b ) + 0.08f * MathF.Sin( c ));
		float da = MathF.Cos( a ) + 0.24f * MathF.Sin( 2 * a );
		float db = 0.45f * MathF.Cos( b );
		float dc = 0.08f * MathF.Cos( c );
		normal = new Vector3( -WaveHeight * (0.0026f * da - 0.0018f * db + 0.007f * dc),
			-WaveHeight * (0.0012f * da + 0.0042f * db - 0.003f * dc), 1 ).Normal;
	}

	protected override void OnStart()
	{
		BuildSurface();
	}

	protected override void OnRefresh()
	{
		// Hotloading preserves fields: rebuild buffers when grid size or material code changes.
		if ( mesh is not null ) BuildSurface();
	}

	private void BuildSurface()
	{
		if ( renderer.IsValid() ) renderer.Destroy();
		vertices = new Vertex[(Cells + 1) * (Cells + 1)];
		gridCoordinates = new float[Cells + 1];
		for ( int i = 0; i <= Cells; i++ ) gridCoordinates[i] = GridCoordinate( i );
		mesh = new Mesh( Material.Load( "materials/ocean/naval_ocean.vmat" ) );
		UpdateVertices();
		mesh.CreateVertexBuffer<Vertex>( vertices.Length, vertices );
		var indices = new int[Cells * Cells * 6];
		int index = 0;
		for ( int y = 0; y < Cells; y++ )
		{
			for ( int x = 0; x < Cells; x++ )
			{
				int a = y * (Cells + 1) + x;
				indices[index++] = a;
				indices[index++] = a + 1;
				indices[index++] = a + Cells + 2;
				indices[index++] = a;
				indices[index++] = a + Cells + 2;
				indices[index++] = a + Cells + 1;
			}
		}
		mesh.CreateIndexBuffer( indices.Length, indices );
		renderer = GameObject.AddComponent<ModelRenderer>();
		renderer.Flags |= ComponentFlags.NotSaved;
		renderer.Model = Model.Builder.AddMesh( mesh ).Create();
		UpdateMaterial();
	}

	protected override void OnUpdate()
	{
		if ( mesh is null ) return;
		if ( vertices is null || vertices.Length != (Cells + 1) * (Cells + 1)
			|| gridCoordinates is null || gridCoordinates.Length != Cells + 1 ) BuildSurface();
		UpdateVertices();
		mesh.SetVertexBufferData<Vertex>( vertices );
		UpdateMaterial();
	}

	private void UpdateMaterial()
	{
		if ( !renderer.IsValid() || !renderer.SceneObject.IsValid() ) return;
		renderer.SceneObject.Attributes.Set( "OceanTime", Time.Now );
		renderer.SceneObject.Attributes.Set( "OceanWaveHeight", WaveHeight );
		renderer.SceneObject.Attributes.Set( "OceanFoamStrength", Math.Max( 0, FoamStrength ) );
	}

	private static float GridCoordinate( int index )
	{
		float unit = (index - Cells / 2) / (float)(Cells / 2);
		// Dense around the player, stretched at the horizon: no extra far-water draw calls.
		return (index - Cells / 2) * Spacing + MathF.Sign( unit ) * MathF.Pow( MathF.Abs( unit ), 4 ) * 14000;
	}

	private void UpdateVertices()
	{
		var target = FollowTarget.IsValid() ? FollowTarget.WorldPosition : Vector3.Zero;
		var center = new Vector3( MathF.Floor( target.x / Spacing ) * Spacing,
			MathF.Floor( target.y / Spacing ) * Spacing, 0 );
		// Keep the mesh near its object origin so model bounds remain stable as the ship travels.
		WorldPosition = center;
		float time = Time.Now;
		for ( int y = 0; y <= Cells; y++ )
		{
			for ( int x = 0; x <= Cells; x++ )
			{
				var local = new Vector3( gridCoordinates[x], gridCoordinates[y], 0 );
				var world = center + local;
				SampleWave( world, time, out float height, out var normal );
				local.z = height;
				vertices[y * (Cells + 1) + x] = new Vertex( local, normal,
					Vector3.Forward, new Vector4( world.x / 512, world.y / 512, 0, 0 ) );
			}
		}
		float half = GridCoordinate( Cells );
		float maxHeight = MathF.Abs( WaveHeight ) * 1.82f + 32;
		mesh.Bounds = new BBox( new Vector3( -half, -half, SeaLevel - maxHeight ),
			new Vector3( half, half, SeaLevel + maxHeight ) );
	}

	protected override void OnDestroy()
	{
		if ( renderer.IsValid() ) renderer.Destroy();
		mesh = null;
	}
}
