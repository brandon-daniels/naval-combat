using System;
using System.Collections.Generic;

namespace NavalCombat;

/// <summary>Short-lived local physics splinters. Never participate in projectile damage or player collision.</summary>
public sealed class ShipDebris : Component
{
	public const string Tag = "ship_debris";
	internal const int SceneBudget = 160;
	internal GameObject Owner { get; set; }
	internal ArcadeOcean Ocean { get; set; }
	internal float Lifetime { get; set; } = 7;
	private Rigidbody body;
	private GameObject visual;
	private float age;
	private static Model splinter;

	internal static ShipDebris Spawn( GameObject owner, ArcadeOcean ocean, Vector3 position, Rotation rotation,
		Vector3 size, Vector3 velocity, Vector3 spin, float lifetime, Color color )
	{
		// Scene-local accounting also works when several ships break in the same frame.
		if ( owner.Scene.GetAllComponents<ShipDebris>().Count() >= SceneBudget ) return null;
		var go = new GameObject( owner.Scene, false, "Physical hull splinter" );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.Tags.Add( Tag );
		go.WorldPosition = position;
		go.WorldRotation = rotation;
		var fragment = go.AddComponent<ShipDebris>();
		fragment.Owner = owner;
		fragment.Ocean = ocean;
		fragment.Lifetime = lifetime;
		fragment.visual = new GameObject( go, true, "Broken wood" );
		fragment.visual.NetworkMode = NetworkMode.Never;
		fragment.visual.LocalScale = size;
		var renderer = fragment.visual.AddComponent<ModelRenderer>();
		renderer.Model = splinter ??= BuildSplinter();
		renderer.Tint = color;
		var collider = go.AddComponent<BoxCollider>();
		collider.Scale = size * 0.8f;
		collider.Friction = 0.65f;
		collider.Elasticity = 0.15f;
		fragment.body = go.AddComponent<Rigidbody>();
		fragment.body.MassOverride = Math.Clamp( size.x * size.y * size.z * 0.0003f, 0.03f, 0.8f );
		fragment.body.EnhancedCcd = true;
		fragment.body.EnableImpactDamage = false;
		go.Enabled = true;
		fragment.body.Velocity = velocity;
		fragment.body.AngularVelocity = spin;
		return fragment;
	}

	protected override void OnFixedUpdate()
	{
		if ( !body.IsValid() || !Ocean.IsValid() ) return;
		// The ocean is a rendered surface, so supply buoyancy while the engine handles motion/contact.
		if ( Scene.GetAllComponents<ArcadeShip>().Any( ship => ship.ContainsCargoInterior( WorldPosition ) ) ) return;
		float depth = Ocean.HeightAt( WorldPosition, Time.Now ) - WorldPosition.z;
		if ( depth < -2 ) return;
		float acceleration = Math.Clamp( 800 + depth * 55 - body.Velocity.z * 16, 0, 2400 );
		body.ApplyForce( Vector3.Up * body.MassOverride * acceleration );
		float drag = Math.Clamp( Time.Delta * 2.5f, 0, 1 );
		body.Velocity *= 1 - drag;
		body.AngularVelocity *= 1 - drag;
	}

	internal static ShipDebris SpawnBeam( GameObject owner, ArcadeOcean ocean, Transform transform,
		Model model, float length, float radius, Vector3 velocity )
	{
		if ( owner.Scene.GetAllComponents<ShipDebris>().Count() >= SceneBudget ) return null;
		var go = new GameObject( owner.Scene, false, "Detached ship timber" );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.Tags.Add( Tag );
		go.WorldTransform = transform;
		// Move clear of the intact frames before enabling the detached convex body.
		go.WorldPosition += velocity.Normal * 12;
		var fragment = go.AddComponent<ShipDebris>();
		fragment.Owner = owner;
		fragment.Ocean = ocean;
		fragment.Lifetime = 12;
		fragment.visual = new GameObject( go, true, "Broken structural beam" );
		fragment.visual.NetworkMode = NetworkMode.Never;
		fragment.visual.AddComponent<ModelRenderer>().Model = model;
		var collider = go.AddComponent<CapsuleCollider>();
		float half = Math.Max( 0, length * 0.5f - radius );
		collider.Start = Vector3.Backward * half;
		collider.End = Vector3.Forward * half;
		collider.Radius = radius;
		collider.Friction = 0.65f;
		collider.Elasticity = 0.1f;
		fragment.body = go.AddComponent<Rigidbody>();
		fragment.body.MassOverride = Math.Clamp( length * radius * radius * 250, 0.2f, 8 );
		fragment.body.EnableImpactDamage = false;
		fragment.body.EnhancedCcd = true;
		go.Enabled = true;
		fragment.body.Velocity = velocity;
		fragment.body.AngularVelocity = new Vector3( 1.5f, 2.3f, 0.7f );
		return fragment;
	}

	protected override void OnUpdate()
	{
		age += Time.Delta;
		if ( !Owner.IsValid() || age >= Lifetime )
		{
			GameObject.Destroy();
			return;
		}
		// Shrink only the render child during the last second; keep physics shapes stable.
		if ( age > Lifetime - 1 && visual.IsValid() )
			visual.LocalScale *= Math.Max( 0.01f, 1 - Time.Delta * 5 );
	}

	internal static ShipDebris SpawnPlank( GameObject owner, ArcadeOcean ocean, Transform transform, Model model, Vector3 velocity )
	{
		if ( owner.Scene.GetAllComponents<ShipDebris>().Count() >= SceneBudget ) return null;
		var go = new GameObject( owner.Scene, false, "Detached hull plank" );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.Tags.Add( Tag );
		go.WorldTransform = transform;
		// Move clear of the intact frames before enabling the detached convex body.
		go.WorldPosition += velocity.Normal * 12;
		var fragment = go.AddComponent<ShipDebris>();
		fragment.Owner = owner;
		fragment.Ocean = ocean;
		fragment.Lifetime = 12;
		fragment.visual = new GameObject( go, true, "Broken planking" );
		fragment.visual.NetworkMode = NetworkMode.Never;
		fragment.visual.AddComponent<ModelRenderer>().Model = model;
		go.AddComponent<ModelCollider>().Model = model;
		fragment.body = go.AddComponent<Rigidbody>();
		fragment.body.MassOverride = 3;
		fragment.body.EnableImpactDamage = false;
		fragment.body.EnhancedCcd = true;
		go.Enabled = true;
		fragment.body.Velocity = velocity;
		fragment.body.AngularVelocity = new Vector3( 2, 3, 1 );
		return fragment;
	}

	internal static Model BuildSplinter()
	{
		// A closed, asymmetric shard, rather than a scaled cube or a billboard particle.
		var points = new Vector3[]
		{
			new( -0.5f, -0.3f, -0.4f ), new( -0.5f, 0.4f, -0.35f ),
			new( -0.32f, 0.5f, 0.4f ), new( -0.5f, -0.45f, 0.5f ),
			new( 0.5f, -0.14f, -0.2f ), new( 0.35f, 0.22f, -0.25f ),
			new( 0.46f, 0.12f, 0.16f ), new( 0.2f, -0.2f, 0.3f )
		};
		var faces = new int[][] { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 },
			new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
		var vertices = new List<Vertex>();
		var indices = new List<int>();
		foreach ( var face in faces )
		{
			int start = vertices.Count;
			var normal = Vector3.Cross( points[face[1]] - points[face[0]], points[face[2]] - points[face[0]] ).Normal;
			foreach ( var index in face ) vertices.Add( new Vertex( points[index], normal, Vector3.Up, Vector4.Zero ) );
			indices.AddRange( new[] { start, start + 1, start + 2, start, start + 2, start + 3 } );
		}
		var mesh = new Mesh( Material.Load( "materials/default.vmat" ) );
		mesh.CreateVertexBuffer<Vertex>( vertices.Count, vertices.ToArray() );
		mesh.CreateIndexBuffer( indices.Count, indices.ToArray() );
		mesh.Bounds = new BBox( Vector3.One * -0.5f, Vector3.One * 0.5f );
		return Model.Builder.AddMesh( mesh ).Create();
	}
}
