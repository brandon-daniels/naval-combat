using System;
using System.Collections.Generic;
using System.Linq;

namespace NavalCombat;

/// <summary>Weighty arcade sailing with spring buoyancy, persistent rudder steering and controlled hull drift.</summary>
public sealed class ArcadeShip : Component, Component.ExecuteInEditor
{
	private const string ReferenceShipModel = "models/reference_ship/reference_ship.vmdl";
	private static readonly HashSet<string> PlaceholderGeometry = new()
	{
		"Hull", "Deck", "Cabin", "Port rail", "Starboard rail", "Bow rail", "Stern rail"
	};
	private ModelCollider shipModelCollider;

	[Property] public ArcadeOcean Ocean { get; set; }
	[Property] public Rigidbody Body { get; set; }
	[Property] public ShipHelm Helm { get; set; }
	[Property, Group( "Presentation" )] public float ShipModelScale { get; set; } = 48.8f;
	[Property, Group( "Presentation" )] public float ShipModelVerticalOffset { get; set; } = -90;
	[Property] public float ForwardSpeed { get; set; } = 650;
	[Property] public SailRig Sails { get; set; }
	[Property] public float Acceleration { get; set; } = 260;
	[Property] public float TurnRate { get; set; } = 55;
	[Property, Group( "Handling" )] public float DriveResponse { get; set; } = 0.38f;
	[Property, Group( "Handling" )] public float WheelSpeed { get; set; } = 0.7f;
	[Property, Group( "Handling" )] public float LateralResistance { get; set; } = 1.35f;
	[Property, Group( "Handling" )] public float CoastingResistance { get; set; } = 0.045f;
	[Property, Group( "Handling" )] public float MinimumTurnAuthority { get; set; } = 0.1f;
	[Property, Group( "Handling" )] public float MaximumTurnAuthority { get; set; } = 0.58f;
	[Property, Group( "Handling" )] public float YawResponse { get; set; } = 2.4f;
	[Property] public float BuoyancySpring { get; set; } = 50;
	[Property] public float BuoyancyDamping { get; set; } = 12;
	public float Rudder => steering;
	public bool WalkableModelReady => shipModelCollider.IsValid() && shipModelCollider.Enabled;
	public GameObject WalkableModelObject => shipModelCollider.IsValid() ? shipModelCollider.GameObject : null;
	public BBox WalkableModelBounds => shipModelCollider.IsValid() ? shipModelCollider.GetWorldBounds() : default;
	public BBox WalkableModelLocalBounds => shipModelCollider.IsValid() ? shipModelCollider.LocalBounds : default;

	/// <summary>The hollow room bounds in model space, used to keep its interior dry.</summary>
	public bool ContainsCargoInterior( Vector3 worldPosition )
	{
		if ( !WalkableModelReady ) return false;
		var point = WalkableModelObject.WorldTransform.PointToLocal( worldPosition );
		return MathF.Abs( point.x ) < 1.75f && point.y > -7.3f && point.y < 5.0f
			&& point.z > 0.72f && point.z < 3.34f;
	}

	private static readonly Vector3[] BaseFloats =
	{
		new( 200, 90, -18 ), new( 200, -90, -18 ),
		new( -200, 90, -18 ), new( -200, -90, -18 )
	};
	private float steering;

	protected override void OnStart()
	{
		ConfigureWalkableModel();
	}

	/// <summary>
	/// Replaces the prototype box shell with the authored compound model collision.
	/// Station trigger colliders are deliberately retained.
	/// </summary>
	private void ConfigureWalkableModel()
	{
		var visuals = GameObject.GetComponentsInChildren<ModelRenderer>( true, true ).Where( renderer =>
			(renderer.GameObject.Name == "Reference ship visual" || renderer.GameObject.Name == "Pirate ship 02 visual") &&
			GameObject.IsDescendant( renderer.GameObject ) ).ToList();
		var visual = visuals.FirstOrDefault( renderer =>
			(renderer.Flags & ComponentFlags.NotSaved) == 0 ) ?? visuals.FirstOrDefault();
		if ( !visual.IsValid() )
		{
			var visualObject = new GameObject( GameObject, true, "Reference ship visual" );
			visualObject.NetworkMode = NetworkMode.Never;
			visualObject.Flags |= GameObjectFlags.NotSaved;
			visualObject.LocalPosition = new Vector3( 0, 0, -45 );
			visualObject.LocalRotation = Rotation.FromYaw( -90 );
			visualObject.LocalScale = Vector3.One * 24.4f;
			visual = visualObject.AddComponent<ModelRenderer>();
			visual.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked;
		}
		foreach ( var duplicate in visuals.Where( renderer => renderer != visual ) )
		{
			duplicate.Enabled = false;
			var duplicateCollider = duplicate.GameObject.GetComponent<ModelCollider>( true );
			if ( duplicateCollider.IsValid() ) duplicateCollider.Enabled = false;
		}

		var model = Model.Load( ReferenceShipModel );
		if ( model is null )
		{
			Log.Error( $"Unable to load {ReferenceShipModel}; retaining prototype ship collision." );
			return;
		}

		visual.GameObject.Name = "Reference ship visual";
		visual.Model = model;
		visual.Enabled = true;
		visual.GameObject.LocalPosition = new Vector3( 0, 0, ShipModelVerticalOffset );
		visual.GameObject.LocalRotation = Rotation.FromYaw( -90 );
		visual.GameObject.LocalScale = Vector3.One * Math.Max( 1, ShipModelScale );
		shipModelCollider = visual.GameObject.GetComponent<ModelCollider>( true );
		if ( !shipModelCollider.IsValid() )
		{
			shipModelCollider = visual.GameObject.AddComponent<ModelCollider>();
			shipModelCollider.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked;
		}
		shipModelCollider.Model = model;
		shipModelCollider.Enabled = true;

		// The old root box encloses the cargo room, while the saved deck/cabin boxes
		// cap its entrance. The model's PhysicsHullFile now supplies those shapes.
		var rootBox = GetComponent<BoxCollider>();
		if ( rootBox.IsValid() ) rootBox.Enabled = false;
		foreach ( var collider in GameObject.GetComponentsInChildren<BoxCollider>( true, true ).Where( collider =>
			GameObject.IsDescendant( collider.GameObject ) && PlaceholderGeometry.Contains( collider.GameObject.Name ) ) )
		{
			collider.Enabled = false;
		}
		foreach ( var renderer in GameObject.GetComponentsInChildren<ModelRenderer>( true, true ).Where( renderer =>
			GameObject.IsDescendant( renderer.GameObject ) && PlaceholderGeometry.Contains( renderer.GameObject.Name ) ) )
		{
			renderer.Enabled = false;
		}

		ConfigureStationLayout();
	}

	private void ConfigureStationLayout()
	{
		foreach ( var child in GameObject.GetAllObjects( true ) )
		{
			switch ( child.Name )
			{
				case "Bow helm":
					child.LocalPosition = new Vector3( -290, -80, 73 );
					child.LocalRotation = Rotation.FromYaw( 180 );
					break;
				case "Helm exit": child.LocalPosition = new Vector3( -34, 0, 6 ); break;
				case "Mast and sail controls": child.LocalPosition = new Vector3( 80, 0, 73 ); break;
				case "Starboard forward cannon": child.LocalPosition = new Vector3( 155, -105, 73 ); break;
				case "Port forward cannon": child.LocalPosition = new Vector3( 155, 105, 73 ); break;
				case "Deck spawn": child.LocalPosition = new Vector3( -200, -85, 95 ); break;
				case "NPC captain spawn": child.LocalPosition = new Vector3( -200, -85, 95 ); break;
			}
		}

		// The low-poly ship already contains its own wheel. Keep the station,
		// seat and hand grips, but remove the old gold placeholder wheel.
		if ( Helm.IsValid() )
		{
			foreach ( var renderer in GameObject.GetComponentsInChildren<ModelRenderer>( true, true ).Where( renderer =>
				Helm.GameObject.IsDescendant( renderer.GameObject ) ) )
			{
				renderer.Enabled = false;
			}
		}

		// The authored hull supplies the complete mast and yards, while SailRig
		// adds the matching separated sail model. Hide all prototype box rigging.
		if ( Sails.IsValid() )
		{
			if ( Sails.WindArrow.IsValid() ) Sails.WindArrow.Enabled = false;
			foreach ( var renderer in GameObject.GetComponentsInChildren<ModelRenderer>( true, true ).Where( renderer =>
				Sails.GameObject.IsDescendant( renderer.GameObject ) ) )
			{
				renderer.Enabled = false;
			}
		}

		// Disabled legacy cannon stations retain serialized references, but none
		// of their old geometry or colliders should appear on the rebuilt ship.
		foreach ( var cannon in GameObject.GetComponentsInChildren<ShipCannon>( true, true ).Where( cannon =>
			GameObject.IsDescendant( cannon.GameObject ) && !cannon.Enabled ) )
		{
			foreach ( var renderer in GameObject.GetComponentsInChildren<ModelRenderer>( true, true ).Where( renderer =>
				cannon.GameObject.IsDescendant( renderer.GameObject ) ) ) renderer.Enabled = false;
			foreach ( var collider in GameObject.GetComponentsInChildren<BoxCollider>( true, true ).Where( collider =>
				cannon.GameObject.IsDescendant( collider.GameObject ) ) ) collider.Enabled = false;
		}
	}
	/// <summary>Turns the persistent wheel while held. Releasing input leaves the rudder where it was set.</summary>
	public void SetHelmInput( ShipHelm source, float turn, float delta )
	{
		if ( source != Helm || !source.IsValid() || !source.Occupied ) return;
		float step = Math.Clamp( delta, 0, 0.1f );
		steering = Math.Clamp( steering + Math.Clamp( turn, -1, 1 ) * Math.Max( 0, WheelSpeed ) * step, -1, 1 );
	}

	/// <summary>Lets an authorized automated helmsman aim the rudder without emulating key-repeat timing.</summary>
	public void SetHelmTarget( ShipHelm source, float target )
	{
		if ( source != Helm || !source.IsValid() || !source.Occupied ) return;
		steering = Math.Clamp( target, -1, 1 );
	}

	protected override void OnFixedUpdate()
	{
		if ( Scene.IsEditor ) return;
		if ( IsProxy ) return;
		if ( !Body.IsValid() || !Ocean.IsValid() ) return;
		float mass = Body.Mass;
		// Custom gravity makes the spring equilibrium independent of project gravity settings.
		Body.ApplyForce( Vector3.Down * 900 * mass );
		int submerged = 0;
		float hullScale = Math.Max( 1, ShipModelScale ) / 24.4f;
		foreach ( var baseSample in BaseFloats )
		{
			var sample = new Vector3( baseSample.x * hullScale, baseSample.y * hullScale, baseSample.z * hullScale );
			var point = WorldPosition + WorldRotation * sample;
			float depth = Ocean.HeightAt( point, Time.Now ) - point.z;
			if ( depth <= 0 ) continue;
			submerged++;
			float lift = Math.Clamp( depth * BuoyancySpring - Body.GetVelocityAtPoint( point ).z * BuoyancyDamping, 0, 2700 );
			Body.ApplyForceAt( point, Vector3.Up * lift * mass / BaseFloats.Length );
		}

		float wet = submerged / (float)BaseFloats.Length;
		var forward = WorldRotation.Forward.WithZ( 0 ).Normal;
		var velocity = Body.Velocity.WithZ( 0 );
		float speed = Vector3.Dot( velocity, forward );
		float desired = ForwardSpeed * (Sails.IsValid() ? Sails.DriveFraction : 0);
		float drive = desired > speed
			? Math.Clamp( (desired - speed) * Math.Max( 0, DriveResponse ), 0, Acceleration * Math.Max( 0, DriveResponse ) )
			: Math.Clamp( (desired - speed) * Math.Max( 0, CoastingResistance ), -Acceleration, 0 );
		var sideways = velocity - forward * speed;
		Body.ApplyForce( (forward * drive - sideways * Math.Max( 0, LateralResistance )) * mass * wet );

		// A moving hull gets useful rudder authority; a nearly stopped ship can only creep around.
		float speedFraction = Math.Clamp( Math.Abs( speed ) / Math.Max( 1, ForwardSpeed * 0.65f ), 0, 1 );
		float turnAuthority = MinimumTurnAuthority + (MaximumTurnAuthority - MinimumTurnAuthority) * speedFraction;
		var angular = Body.AngularVelocity;
		float yaw = steering * TurnRate * turnAuthority * MathF.PI / 180 * wet;
		float blend = 1 - MathF.Exp( -Math.Max( 0, YawResponse ) * Time.Delta );
		angular.z += (yaw - angular.z) * blend;
		Body.AngularVelocity = angular;
	}
}
