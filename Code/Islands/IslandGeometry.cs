using System;

namespace NavalCombat;

/// <summary>One vertex-painted mesh and one static collision mesh for the entire island.</summary>
internal sealed class IslandGeometry
{
	private readonly List<Vertex> vertices = new();
	private readonly List<Vector3> solidPoints = new();
	private readonly Dictionary<Vector3, int> solidPointIndices = new();
	private readonly List<int> solidIndices = new();
	private BBox bounds;
	private float solidRadiusSquared;

	public float SolidRadius => MathF.Sqrt( solidRadiusSquared );

	public void Triangle( Vector3 a, Vector3 b, Vector3 c, Color color, bool solid = true )
	{
		var cross = Vector3.Cross( b - a, c - a );
		if ( cross.LengthSquared < 0.00001f ) return;
		var normal = cross.Normal;
		var tangent = (b - a).Normal;
		// Each face owns its normals and paint. The physics mesh shares its edge vertices.
		AddVertex( a, normal, tangent, color );
		AddVertex( b, normal, tangent, color );
		AddVertex( c, normal, tangent, color );
		if ( !solid ) return;
		AddSolidIndex( a );
		AddSolidIndex( b );
		AddSolidIndex( c );
	}

	private void AddVertex( Vector3 position, Vector3 normal, Vector3 tangent, Color color )
	{
		bounds = bounds.AddPoint( position );
		vertices.Add( new Vertex( position, normal, tangent, Vector4.Zero ) { Color = color } );
	}

	private void AddSolidIndex( Vector3 point )
	{
		if ( !solidPointIndices.TryGetValue( point, out int index ) )
		{
			index = solidPoints.Count;
			solidPointIndices.Add( point, index );
			solidPoints.Add( point );
			solidRadiusSquared = Math.Max( solidRadiusSquared, point.x * point.x + point.y * point.y );
		}
		solidIndices.Add( index );
	}

	public Model CreateModel()
	{
		var mesh = new Mesh( Material.Load( "materials/default/vertex_color.vmat" ) );
		mesh.CreateVertexBuffer<Vertex>( vertices.Count, vertices );
		mesh.Bounds = bounds;
		return Model.Builder.AddMesh( mesh ).AddCollisionMesh( solidPoints, solidIndices ).Create();
	}
}
