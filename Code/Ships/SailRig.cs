using System;

namespace NavalCombat;

/// <summary>Rotating square sail and local rigging station; lowered cloth catches wind.</summary>
public sealed class SailRig : ShipStation, Component.ExecuteInEditor
{
	[Property] public SailingWind Wind { get; set; }
	[Property] public GameObject Yard { get; set; }
	[Property] public GameObject WindArrow { get; set; }
	[Property] public GameObject LowerBoom { get; set; }
	[Property] public float RotationSpeed { get; set; } = 50;
	[Property] public float DeploymentSpeed { get; set; } = 0.4f;
	private float deployment;
	private float sailAngle;
	[Property, Sync] public float Deployment { get => deployment; set => deployment = Math.Clamp( value, 0, 1 ); }
	[Property, Sync] public float SailAngle { get => sailAngle; set => sailAngle = ((value + 180) % 360 + 360) % 360 - 180; }
	public Vector3 SailDirection => Rotation.FromYaw( Ship.WorldRotation.Angles().yaw + SailAngle ).Forward;
	public float Alignment => Wind.IsValid() && Ship.IsValid()
		? MathF.Pow( Math.Max( 0, Vector3.Dot( SailDirection, Wind.Direction ) ), 2 ) : 0;
	public float DriveFraction => Enabled && Wind.IsValid() && Wind.Enabled && Ship.IsValid()
		? Deployment * Alignment * HeadingPower( Vector3.Dot( Ship.WorldRotation.Forward.WithZ( 0 ).Normal, Wind.Direction ) ) * Wind.CurrentStrength : 0;

	/// <summary>Arcade sailing polar: no drive within 45 degrees of directly upwind.</summary>
	public static float HeadingPower( float windAlignment )
	{
		float alignment = Math.Clamp( windAlignment, -1, 1 );
		if ( alignment <= -0.707107f ) return 0;
		if ( alignment < -0.5f ) return 0.35f * (alignment + 0.707107f) / 0.207107f;
		if ( alignment < 0 ) return 0.35f + (alignment + 0.5f) * 0.5f;
		return 0.6f + alignment * 0.4f;
	}

	private const int Columns = 12;
	private const int Rows = 12;
	private Vertex[] vertices;
	private Mesh mesh;
	private GameObject cloth;

	public void Adjust( PlayerController source, float rotation, float lower, float delta )
	{
		if ( !Enabled || !source.IsValid() || Occupant != source ) return;
		float step = Math.Clamp( delta, 0, 0.1f );
		SailAngle += Math.Clamp( rotation, -1, 1 ) * Math.Max( 0, RotationSpeed ) * step;
		Deployment += Math.Clamp( lower, -1, 1 ) * Math.Max( 0, DeploymentSpeed ) * step;
	}

	protected override void OnStart() => BuildCloth();
	protected override void OnRefresh()
	{
		if ( mesh is not null ) BuildCloth();
	}

	private void BuildCloth()
	{
		if ( !Yard.IsValid() ) return;
		if ( cloth.IsValid() ) cloth.Destroy();
		cloth = new GameObject( Yard, true, "Sail canvas" );
		cloth.Flags |= GameObjectFlags.NotSaved;
		int faceCount = (Columns + 1) * (Rows + 1);
		vertices = new Vertex[faceCount * 2];
		mesh = new Mesh( Material.Load( "materials/default.vmat" ) );
		UpdateCloth();
		mesh.CreateVertexBuffer<Vertex>( vertices.Length, vertices );
		var indices = new int[Columns * Rows * 12];
		int n = 0;
		for ( int y = 0; y < Rows; y++ )
		for ( int x = 0; x < Columns; x++ )
		{
			int a = y * (Columns + 1) + x;
			int b = a + 1;
			int c = a + Columns + 2;
			int d = a + Columns + 1;
			indices[n++] = a; indices[n++] = c; indices[n++] = b;
			indices[n++] = a; indices[n++] = d; indices[n++] = c;
			indices[n++] = a + faceCount; indices[n++] = b + faceCount; indices[n++] = c + faceCount;
			indices[n++] = a + faceCount; indices[n++] = c + faceCount; indices[n++] = d + faceCount;
		}
		mesh.CreateIndexBuffer( indices.Length, indices );
		mesh.Bounds = new BBox( new Vector3( -40, -110, 80 ), new Vector3( 40, 110, 235 ) );
		var renderer = cloth.AddComponent<ModelRenderer>();
		renderer.Model = Model.Builder.AddMesh( mesh ).Create();
		renderer.Tint = new Color( 0.94f, 0.88f, 0.68f );
	}

	protected override void OnUpdate()
	{
		if ( Yard.IsValid() ) Yard.LocalRotation = Rotation.FromYaw( SailAngle );
		if ( WindArrow.IsValid() && Wind.IsValid() ) WindArrow.WorldRotation = Rotation.LookAt( Wind.Direction );
		if ( LowerBoom.IsValid() ) LowerBoom.LocalPosition = new Vector3( 0, 0, 230 - (8 + Deployment * 135) );
		if ( mesh is null ) return;
		UpdateCloth();
		mesh.SetVertexBufferData<Vertex>( vertices );
	}

	private void UpdateCloth()
	{
		int faceCount = (Columns + 1) * (Rows + 1);
		float drop = 8 + Deployment * 135;
		for ( int y = 0; y <= Rows; y++ )
		for ( int x = 0; x <= Columns; x++ )
		{
			float u = x / (float)Columns;
			float v = y / (float)Rows;
			float envelope = MathF.Sin( u * MathF.PI ) * MathF.Sin( v * MathF.PI );
			float belly = envelope * Deployment * (7 + 20 * Alignment);
			float ripple = MathF.Sin( u * 18 + v * 12 - Time.Now * 4 ) * envelope * Deployment * 2;
			var position = new Vector3( belly + ripple, (u - 0.5f) * 190, 230 - v * drop );
			int index = y * (Columns + 1) + x;
			vertices[index] = new Vertex( position, Vector3.Forward, Vector3.Left, new Vector4( u, v, 0, 0 ) );
			vertices[index + faceCount] = new Vertex( position, Vector3.Backward, Vector3.Left, new Vector4( u, v, 0, 0 ) );
		}
	}

	protected override void OnDestroy()
	{
		if ( cloth.IsValid() ) cloth.Destroy();
		mesh = null;
	}
}
