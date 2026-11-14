using System;

namespace NavalCombat;

/// <summary>A local hold-water surface rebuilt from the host's accumulated flood volume.</summary>
public sealed class ShipFloodVisuals : Component
{
	private const int LengthCells = 32;
	private const int WidthCells = 8;
	private GameObject surface;
	private ModelRenderer renderer;
	private Mesh mesh;
	private Vertex[] vertices;
	public bool WaterVisible => renderer.IsValid() && renderer.Enabled;
	public float SurfaceLocalHeight { get; private set; }

	protected override void OnUpdate()
	{
		var ship = GetComponent<ArcadeShip>();
		var health = GetComponent<ShipHealth>();
		if ( Scene.IsEditor || !ship.IsValid() || !health.IsValid() || !ship.WalkableModelReady ) return;
		if ( health.FloodFraction <= 0 )
		{
			if ( renderer.IsValid() ) renderer.Enabled = false;
			SurfaceLocalHeight = health.FloodFloorLocalHeight;
			return;
		}
		SurfaceLocalHeight = health.FloodSurfaceLocalHeight;
		if ( !surface.IsValid() || !renderer.IsValid() || mesh is null
			|| vertices is null || vertices.Length != (LengthCells + 1) * (WidthCells + 1) ) Build( ship, health );
		renderer.Enabled = true;
		UpdateVertices( ship, health );
		mesh.SetVertexBufferData<Vertex>( vertices );
		if ( !renderer.SceneObject.IsValid() ) return;
		// Reuse the ocean's blue/foam treatment without its exterior hold masks.
		renderer.SceneObject.Attributes.Set( "OceanTime", Time.Now );
		renderer.SceneObject.Attributes.Set( "OceanSeaLevel", WorldTransform.PointToWorld( new Vector3( 0, 0, SurfaceLocalHeight ) ).z );
		renderer.SceneObject.Attributes.Set( "OceanWaveHeight", 3.0f );
		renderer.SceneObject.Attributes.Set( "OceanFoamStrength", 0.16f );
		if ( ship.Ocean.IsValid() ) ship.Ocean.ApplyWaterLighting( renderer );
	}

	private void Build( ArcadeShip ship, ShipHealth health )
	{
		Clear();
		surface = new GameObject( GameObject, true, "Accumulated hold water" );
		surface.NetworkMode = NetworkMode.Never;
		surface.Flags |= GameObjectFlags.NotSaved;
		vertices = new Vertex[(LengthCells + 1) * (WidthCells + 1)];
		mesh = new Mesh( Material.Load( "materials/ocean/naval_ocean.vmat" ) );
		// Give Model.Builder real vertex extents at creation, so its initial
		// culling bounds enclose the entire hold instead of an empty origin.
		UpdateVertices( ship, health );
		mesh.CreateVertexBuffer<Vertex>( vertices.Length, vertices );
		var indices = new int[LengthCells * WidthCells * 12];
		int n = 0;
		for ( int x = 0; x < LengthCells; x++ )
		{
			for ( int y = 0; y < WidthCells; y++ )
			{
				int a = x * (WidthCells + 1) + y;
				int b = a + WidthCells + 1;
				indices[n++] = a; indices[n++] = b; indices[n++] = b + 1;
				indices[n++] = a; indices[n++] = b + 1; indices[n++] = a + 1;
				indices[n++] = b + 1; indices[n++] = b; indices[n++] = a;
				indices[n++] = a + 1; indices[n++] = b + 1; indices[n++] = a;
			}
		}
		mesh.CreateIndexBuffer( indices.Length, indices );
		renderer = surface.AddComponent<ModelRenderer>();
		renderer.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked;
		renderer.Model = Model.Builder.AddMesh( mesh ).Create();
	}

	private void UpdateVertices( ArcadeShip ship, ShipHealth health )
	{
		float scale = Math.Max( 1, ship.ShipModelScale );
		float floor = health.FloodFloorLocalHeight;
		float ceiling = health.FloodCeilingLocalHeight;
		float ripple = Math.Min( 1.3f, Math.Min( SurfaceLocalHeight - floor, ceiling - SurfaceLocalHeight ) * 0.4f );
		// Fit the surface to the tapered inner hull at the current water height.
		for ( int x = 0; x <= LengthCells; x++ )
		{
			for ( int y = 0; y <= WidthCells; y++ )
			{
				float along = (-7.26f + x / (float)LengthCells * 12.22f) * scale;
				float modelHeight = (SurfaceLocalHeight - ship.ShipModelVerticalOffset) / scale;
				float width = Math.Max( 0, ShipHullGeometry.InnerWidth( along / scale, modelHeight ) - 0.03f );
				float across = (-1 + y / (float)WidthCells * 2) * width * scale;
				float phase = along * 0.035f + across * 0.05f + Time.Now * 2.1f;
				float height = SurfaceLocalHeight + MathF.Sin( phase ) * ripple;
				var position = new Vector3( along, across, height );
				var normal = new Vector3( -0.035f * MathF.Cos( phase ) * ripple, -0.05f * MathF.Cos( phase ) * ripple, 1 ).Normal;
				var world = WorldTransform.PointToWorld( position );
				vertices[x * (WidthCells + 1) + y] = new Vertex( position, normal, Vector3.Forward,
					new Vector4( world.x / 512, world.y / 512, 0, 0 ) );
			}
		}
		mesh.Bounds = new BBox( new Vector3( -7.27f * scale, -3.2f * scale, floor - 2 ),
			new Vector3( 4.97f * scale, 3.2f * scale, ceiling + 2 ) );
	}

	private void Clear()
	{
		if ( surface.IsValid() ) surface.Destroy();
		surface = null;
		renderer = null;
		mesh = null;
		vertices = null;
	}

	protected override void OnRefresh() => Clear();
	protected override void OnDisabled() => Clear();
	protected override void OnDestroy() => Clear();
}
