using System;
using System.Collections.Generic;

namespace NavalCombat;

/// <summary>Authored hull collision and individually removable beams, reconstructed on every peer.</summary>
public sealed class ShipStructure : Component, Component.ExecuteInEditor, Component.ICollisionListener
{
	private ArcadeShip ship;
	private GameObject partsRoot;
	private readonly List<ShipStructurePart> parts = new();
	private readonly List<ShipDebris> debris = new();
	private readonly HashSet<int> removed = new();
	private bool initialized;
	public ModelRenderer HullRenderer => null;
	[Property] public float CollisionBreakSpeed { get; set; } = 180;
	private float nextCollisionDamage;
	public int VisiblePartCount => parts.Count( x => x.IsValid() && x.GameObject.Enabled );
	public int DetachedPartCount => debris.Count( x => x.IsValid() );
	public IReadOnlyList<ShipStructurePart> Parts => parts;
	public bool SailSupported => !GetComponent<ShipHealth>().IsValid() ||
		!GetComponent<ShipHealth>().BrokenParts.Any( x => ShipStructureCatalog.Parts[x.Index].SupportsSail );

	internal void Configure( ArcadeShip owner )
	{
		ship = owner;
		if ( ship.Body.IsValid() ) ship.Body.CollisionEventsEnabled = true;
		if ( !ship.WalkableModelObject.IsValid() ) return;
		if ( partsRoot.IsValid() ) return;
		parts.Clear();
		removed.Clear();
		initialized = false;
		partsRoot = LocalObject( ship.WalkableModelObject, "Destructible ship structure" );
		for ( int i = 0; i < ShipStructureCatalog.Parts.Length; i++ )
		{
			var definition = ShipStructureCatalog.Parts[i];
			var go = LocalObject( partsRoot, $"{definition.Name} {i}" );
			go.LocalPosition = (definition.Start + definition.End) * 0.5f;
			go.LocalRotation = definition.IsPlank ? Rotation.Identity : Rotation.LookAt( definition.End - definition.Start );
			var renderer = go.AddComponent<ModelRenderer>();
			renderer.Model = definition.IsPlank ? BuildPlank( definition ) : BuildBeam( definition );
			if ( definition.IsPlank )
			{
				go.Tags.Add( "ship_hull" );
				go.AddComponent<ModelCollider>().Model = renderer.Model;
			}
			else
			{
				var collider = go.AddComponent<CapsuleCollider>();
				float half = Math.Max( 0, definition.Length * 0.5f - definition.Radius );
				collider.Start = Vector3.Backward * half;
				collider.End = Vector3.Forward * half;
				collider.Radius = definition.Radius;
			}
			var part = go.AddComponent<ShipStructurePart>();
			part.Structure = this;
			part.Index = i;
			parts.Add( part );
		}
	}

	protected override void OnUpdate()
	{
		ship ??= GetComponent<ArcadeShip>();
		if ( !ship.IsValid() ) return;
		Configure( ship );
		if ( Scene.IsEditor ) return;
		RefreshDamage();
	}

	internal void RefreshDamage()
	{
		var health = GetComponent<ShipHealth>();
		if ( !health.IsValid() || parts.Count != ShipStructureCatalog.Parts.Length ) return;
		debris.RemoveAll( x => !x.IsValid() );
		var broken = health.BrokenParts.Select( x => x.Index ).ToHashSet();
		if ( removed.Any( x => !broken.Contains( x ) ) ) ClearDebris();
		// Remove all affected colliders before giving the detached pieces physics bodies.
		foreach ( var part in parts ) part.GameObject.Enabled = !broken.Contains( part.Index );
		foreach ( var impact in health.BrokenParts )
		{
			if ( removed.Contains( impact.Index ) || !initialized || Time.Now - impact.CreatedAt is < 0 or > 0.75f ) continue;
			var part = parts[impact.Index];
			var definition = ShipStructureCatalog.Parts[impact.Index];
			var inherited = ship.Body.IsValid() ? ship.Body.GetVelocityAtPoint( part.WorldPosition ) : Vector3.Zero;
			var direction = ship.WorldRotation * impact.Velocity;
			var velocity = inherited + direction.Normal * Math.Clamp( direction.Length * 0.14f, 80, 250 ) + Vector3.Up * 30;
			var fragment = definition.IsPlank
				? ShipDebris.SpawnPlank( GameObject, ship.Ocean, part.WorldTransform, part.GetComponent<ModelRenderer>( true ).Model, velocity )
				: ShipDebris.SpawnBeam( GameObject, ship.Ocean, part.WorldTransform,
				part.GetComponent<ModelRenderer>( true ).Model, definition.Length, definition.Radius, velocity );
			if ( fragment.IsValid() ) debris.Add( fragment );
		}
		removed.Clear();
		removed.UnionWith( broken );
		initialized = true;
	}

	private static GameObject LocalObject( GameObject parent, string name )
	{
		var go = new GameObject( parent, true, name );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		return go;
	}

	internal void BlastPlanks( Vector3 position, Vector3 velocity, int sequence, float radius )
	{
		if ( !GameplayAuthority.CanMutate ) return;
		var health = GetComponent<ShipHealth>();
		var point = ship.WalkableModelObject.WorldTransform.PointToLocal( position );
		float reach = 2 / ship.ShipModelScale;
		foreach ( var part in parts )
		{
			if ( !part.IsPlank || !part.GameObject.Enabled ) continue;
			var definition = ShipStructureCatalog.Parts[part.Index];
			var bounds = BBox.FromPoints( definition.Points );
			var local = point - definition.Start;
			var closest = new Vector3( Math.Clamp( local.x, bounds.Mins.x, bounds.Maxs.x ),
				Math.Clamp( local.y, bounds.Mins.y, bounds.Maxs.y ), Math.Clamp( local.z, bounds.Mins.z, bounds.Maxs.z ) );
			if ( local.Distance( closest ) <= reach ) health.BreakPart( part.Index, velocity, sequence );
		}
	}

	void Component.ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( Scene.IsEditor || !GameplayAuthority.CanMutate || Time.Now < nextCollisionDamage ) return;
		var other = collision.Other.GameObject;
		if ( !other.IsValid() || other.Tags.Has( ShipDebris.Tag ) || other.GetComponent<ShipPlayer>().IsValid()
			|| other == GameObject || GameObject.IsDescendant( other ) ) return;
		float speed = MathF.Abs( collision.Contact.NormalSpeed );
		if ( speed < Math.Max( 1, CollisionBreakSpeed ) ) return;
		nextCollisionDamage = Time.Now + 0.35f;
		var normal = collision.Contact.Normal;
		GetComponent<ShipHealth>()?.ApplyCannonHit( collision.Contact.Point, normal, -normal * speed );
	}

	private static Model BuildBeam( ShipPartDefinition definition )
	{
		var vertices = new List<Vertex>();
		var indices = new List<int>();
		float half = definition.Length * 0.5f;
		Vector3 Ring( int index, float end )
		{
			float a = MathF.Tau * index / definition.Sides;
			return new Vector3( end, MathF.Cos( a ) * definition.Radius, MathF.Sin( a ) * definition.Radius );
		}
		void Triangle( Vector3 a, Vector3 b, Vector3 c )
		{
			var normal = Vector3.Cross( b - a, c - a ).Normal;
			int first = vertices.Count;
			foreach ( var p in new[] { a, b, c } ) vertices.Add( new Vertex( p, normal, Vector3.Up, Vector4.Zero ) );
			indices.AddRange( new[] { first, first + 1, first + 2 } );
		}
		for ( int i = 0; i < definition.Sides; i++ )
		{
			var a = Ring( i, -half ); var b = Ring( i + 1, -half );
			var c = Ring( i + 1, half ); var d = Ring( i, half );
			Triangle( a, b, c ); Triangle( a, c, d );
			Triangle( Vector3.Backward * half, b, a );
			Triangle( Vector3.Forward * half, d, c );
		}
		var mesh = new Mesh( Material.Load( $"models/reference_ship/{definition.Material}.vmat" ) );
		mesh.CreateVertexBuffer<Vertex>( vertices.Count, vertices.ToArray() );
		mesh.CreateIndexBuffer( indices.Count, indices.ToArray() );
		mesh.Bounds = new BBox( new Vector3( -half, -definition.Radius, -definition.Radius ), new Vector3( half, definition.Radius, definition.Radius ) );
		return Model.Builder.AddMesh( mesh ).Create();
	}

	private static readonly Dictionary<string, Model> plankModels = new();
	private static Model BuildPlank( ShipPartDefinition definition )
	{
		if ( plankModels.TryGetValue( definition.Name, out var model ) ) return model;
		var vertices = new List<Vertex>();
		var indices = new List<int>();
		foreach ( var face in definition.Faces )
		{
			var a = definition.Points[face[0]];
			for ( int i = 1; i < face.Length - 1; i++ )
			{
				var b = definition.Points[face[i]];
				var c = definition.Points[face[i + 1]];
				var normal = Vector3.Cross( b - a, c - a ).Normal;
				foreach ( var point in new[] { a, b, c } )
				{
					indices.Add( vertices.Count );
					vertices.Add( new Vertex( point, normal, Vector3.Up, Vector4.Zero ) );
				}
			}
		}
		var mesh = new Mesh( Material.Load( $"models/reference_ship/{definition.Material}.vmat" ) );
		mesh.CreateVertexBuffer<Vertex>( vertices.Count, vertices.ToArray() );
		mesh.CreateIndexBuffer( indices.Count, indices.ToArray() );
		mesh.Bounds = BBox.FromPoints( definition.Points );
		model = Model.Builder.AddMesh( mesh ).AddCollisionHull( definition.Points.ToList() ).Create();
		plankModels[definition.Name] = model;
		return model;
	}

	private void ClearDebris()
	{
		foreach ( var fragment in debris ) if ( fragment.IsValid() ) fragment.GameObject.Destroy();
		debris.Clear();
	}

	protected override void OnDestroy()
	{
		ClearDebris();
		if ( partsRoot.IsValid() ) partsRoot.Destroy();
	}
}

/// <summary>A traceable beam identity. Authoritative state belongs to the parent ShipHealth.</summary>
public sealed class ShipStructurePart : Component
{
	public ShipStructure Structure { get; internal set; }
	public int Index { get; internal set; }
	public string PartName => ShipStructureCatalog.Parts[Index].Name;
	public bool IsPlank => ShipStructureCatalog.Parts[Index].IsPlank;
}

internal record ShipPartDefinition( string Name, Vector3 Start, Vector3 End, float Radius,
	string Material, int Sides, int Parent, int SecondParent, bool SupportsSail, Vector3[] Points = null, int[][] Faces = null )
{
	internal bool IsPlank => Points is not null;
	internal float Length => Start.Distance( End );
}
