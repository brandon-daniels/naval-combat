using System;
using System.Collections.Generic;
namespace NavalCombat;

/// <summary>Saved terrain parameters with matching editor and runtime geometry.</summary>

public sealed class IslandSurface : Component, Component.ExecuteInEditor
{
	[Property] public float Radius { get; set; } = 720;
	[Property] public float OuterScale { get; set; } = 1;
	[Property] public float InnerScale { get; set; } = 0;
	[Property] public float LowerHeight { get; set; }
	[Property] public float UpperHeight { get; set; } = 150;
	[Property] public float Phase { get; set; }
	[Property] public Color Tint { get; set; } = Color.White;
	private ModelRenderer renderer;
	private ModelCollider collider;
	private int lastHash;
	protected override void OnEnabled() => Rebuild();
	protected override void OnUpdate()
	{
		int hash = HashCode.Combine( Radius, OuterScale, InnerScale, LowerHeight, UpperHeight, Phase, Tint );
		if ( hash != lastHash || !renderer.IsValid() || !collider.IsValid() ) Rebuild();
	}
	private void Rebuild()
	{
		lastHash = HashCode.Combine( Radius, OuterScale, InnerScale, LowerHeight, UpperHeight, Phase, Tint );
		const int sides = 24;
		var points = new List<Vector3>();
		var indices = new List<int>();
		var vertices = new List<Vertex>();
		Vector3 Point( int side, float scale, float z )
		{
			float a = side * MathF.PI * 2 / sides;
			float r = Radius * scale * (1 + 0.1f * MathF.Sin( a * 3 + Phase ) + 0.06f * MathF.Cos( a * 5 - Phase ));
			return new Vector3( MathF.Cos( a ) * r, MathF.Sin( a ) * r, z );
		}
		void Triangle( Vector3 a, Vector3 b, Vector3 c )
		{
			var normal = Vector3.Cross( b - a, c - a ).Normal;
			foreach ( var p in new[] { a, b, c } )
			{
				indices.Add( points.Count );
				points.Add( p );
				vertices.Add( new Vertex( p, normal, Vector3.Left, new Vector4( p.x / 256, p.y / 256, 0, 0 ) ) );
			}
		}
		for ( int i = 0; i < sides; i++ )
		{
			var a = Point( i, OuterScale, LowerHeight );
			var b = Point( i + 1, OuterScale, LowerHeight );
			var c = Point( i + 1, InnerScale, UpperHeight );
			var d = Point( i, InnerScale, UpperHeight );
			Triangle( a, b, c );
			if ( InnerScale > 0 ) Triangle( a, c, d );
		}
		var mesh = new Mesh( Material.Load( "materials/default.vmat" ) );
		mesh.CreateVertexBuffer<Vertex>( vertices.Count, vertices.ToArray() );
		mesh.CreateIndexBuffer( indices.Count, indices.ToArray() );
		mesh.Bounds = new BBox( new Vector3( -Radius * 1.4f, -Radius * 1.4f, LowerHeight ), new Vector3( Radius * 1.4f, Radius * 1.4f, UpperHeight ) );
		var model = Model.Builder.AddMesh( mesh ).AddCollisionMesh( points, indices ).Create();
		if ( !renderer.IsValid() )
		{
			renderer = GameObject.AddComponent<ModelRenderer>();
			renderer.Flags |= ComponentFlags.NotSaved;
		}
		if ( !collider.IsValid() )
		{
			collider = GameObject.AddComponent<ModelCollider>();
			collider.Flags |= ComponentFlags.NotSaved;
		}
		renderer.Model = model;
		renderer.Tint = Tint;
		collider.Model = model;
		renderer.Enabled = true;
		collider.Enabled = true;
	}

	protected override void OnDisabled()
	{
		if ( renderer.IsValid() ) renderer.Enabled = false;
		if ( collider.IsValid() ) collider.Enabled = false;
	}

	protected override void OnDestroy()
	{
		if ( renderer.IsValid() ) renderer.Destroy();
		if ( collider.IsValid() ) collider.Destroy();
	}
}

