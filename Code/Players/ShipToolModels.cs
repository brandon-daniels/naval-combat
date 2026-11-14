using System;

namespace NavalCombat;

/// <summary>Shared render-only hand props. The origin of each model is its right-hand grip.</summary>
internal static class ShipToolModels
{
	private static readonly Color Wood = new( 0.60f, 0.37f, 0.17f );
	private static readonly Color DarkWood = new( 0.44f, 0.24f, 0.11f );
	private static readonly Color Iron = new( 0.26f, 0.32f, 0.35f );
	private static readonly Color Rope = new( 0.81f, 0.64f, 0.38f );
	private static Model planks;
	private static Model emptyBucket;
	private static Model fullBucket;

	public static Model Planks => planks ??= BuildPlanks();
	public static Model Bucket( bool full ) => full ? (fullBucket ??= BuildBucket( true )) : (emptyBucket ??= BuildBucket( false ));

	private static Model BuildPlanks()
	{
		var buffer = new VertexBuffer();
		buffer.Init( true );
		buffer.AddCube( new Vector3( 3, 0, 1 ), new Vector3( 35, 6, 1.6f ), Rotation.FromYaw( 1.5f ), DarkWood );
		buffer.AddCube( new Vector3( 2, 0, 2.7f ), new Vector3( 39, 6.2f, 1.6f ), Rotation.FromYaw( -1 ), Wood );
		buffer.AddCube( new Vector3( 4, 0, 4.4f ), new Vector3( 34, 6, 1.6f ), Rotation.Identity, new Color( 0.73f, 0.48f, 0.25f ) );
		foreach ( float x in new[] { -6f, 12f } )
			buffer.AddCube( new Vector3( x, 0, 2.7f ), new Vector3( 1, 6.9f, 5.6f ), Rotation.Identity, Rope );
		return CreateModel( buffer );
	}

	private static Model BuildBucket( bool full )
	{
		var buffer = new VertexBuffer();
		buffer.Init( true );
		// Each successive band follows the bucket's cross section, including the inside wall and floor.
		Band( 0, -19, 5.1f, -19, DarkWood );
		Band( 5.1f, -19, 5.4f, -17.5f, Iron );
		Band( 5.4f, -17.5f, 6.65f, -10.5f, Wood, DarkWood );
		Band( 6.65f, -10.5f, 6.9f, -9.2f, Iron );
		Band( 6.9f, -9.2f, 7, -8, Wood, DarkWood );
		Band( 7, -8, 6.3f, -8, Iron );
		Band( 6.3f, -8, 4.7f, -18, DarkWood );
		Band( 4.7f, -18, 0, -18, Wood );
		if ( full ) Band( 6.05f, -9.8f, 0, -9.8f, new Color( 0.12f, 0.52f, 0.64f ) );

		var previous = new Vector3( 6.8f, 0, -9 );
		for ( int i = 1; i <= 8; i++ )
		{
			float angle = i * MathF.PI / 8;
			var next = new Vector3( 6.8f * MathF.Cos( angle ), 0, -9 + 9 * MathF.Sin( angle ) );
			var span = next - previous;
			buffer.AddCube( (previous + next) * 0.5f, new Vector3( span.Length + 0.2f, 0.75f, 0.75f ), Rotation.LookAt( span.Normal ), Iron );
			previous = next;
		}
		return CreateModel( buffer );

		void Band( float r0, float z0, float r1, float z1, Color color, Color? alternate = null )
		{
			for ( int i = 0; i < 12; i++ )
			{
				var a = Rotation.FromYaw( i * 30 ).Forward;
				var b = Rotation.FromYaw( (i + 1) * 30 ).Forward;
				var tangent = (b - a).Normal;
				var edge = (a + b).Normal * (r1 - r0) + Vector3.Up * (z1 - z0);
				buffer.Default.Color = i % 3 == 0 ? alternate ?? color : color;
				buffer.Default.Normal = Vector3.Cross( tangent, edge ).Normal;
				buffer.Default.Tangent = new Vector4( tangent, 1 );
				buffer.AddQuad( (a * r0).WithZ( z0 ), (b * r0).WithZ( z0 ), (b * r1).WithZ( z1 ), (a * r1).WithZ( z1 ) );
			}
		}
	}

	private static Model CreateModel( VertexBuffer buffer )
	{
		var mesh = new Mesh( Material.Load( "materials/default/vertex_color.vmat" ) );
		mesh.CreateBuffers( buffer, true );
		return Model.Builder.AddMesh( mesh ).Create();
	}
}
