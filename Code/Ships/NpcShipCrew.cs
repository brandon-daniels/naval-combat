using System;

namespace NavalCombat;

/// <summary>Saved NPC ship setup. Disable this component to remove its captain without removing the ship.</summary>
public sealed class NpcShipCrew : Component
{
	[Property] public ArcadeShip Ship { get; set; }
	[Property] public GameObject SpawnPoint { get; set; }
	[Property] public ArcadeShip Target { get; set; }
	[Property] public bool AutoAcquireTarget { get; set; } = true;
	/// <summary>Distance in units at which the captain prepares a broadside.</summary>
	[Property, Range( 1000, 6000 )] public float EngagementRange { get; set; } = 1800;
	/// <summary>Additional shoreline clearance for hull size, turning and drift.</summary>
	[Property, Range( 350, 3000 )] public float IslandClearance { get; set; } = 700;
	/// <summary>Fraction of sail deployed while following a route.</summary>
	[Property, Range( 0.2f, 0.75f )] public float CruisingDeployment { get; set; } = 0.65f;
	[Property, Range( 40, 220 )] public float WalkSpeed { get; set; } = 120;
	[Property] public Color CaptainTint { get; set; } = new( 0.85f, 0.25f, 0.18f );
	private ShipNpc captain;
	private ArcadeShip appliedTarget;
	private int? appliedSettings;

	protected override void OnUpdate()
	{
		if ( Scene.IsEditor ) return;
		if ( Networking.IsActive ) { RemoveCaptain(); return; }
		if ( !Ship.IsValid() || !Ship.Active || !SpawnPoint.IsValid() || !Ship.Helm.IsValid() || !Ship.Sails.IsValid() || !Ship.Ocean.IsValid() )
		{
			RemoveCaptain();
			return;
		}
		if ( !captain.IsValid() )
		{
			// A walking controller must be outside the ship's physics hierarchy.
			var root = new GameObject( Scene, true, "NPC crew" );
			root.NetworkMode = NetworkMode.Never;
			var sailor = NavalPrototype.CreateSailor( root, Ship, SpawnPoint, true );
			captain = sailor.GameObject.AddComponent<ShipNpc>();
			captain.Sailor = sailor;
			captain.Target = Target;
			appliedTarget = Target;
			crewRoot = root;
			appliedSettings = null;
		}
		if ( captain.Sailor.Ship != Ship ) { RemoveCaptain(); return; }
		if ( appliedTarget != Target )
		{
			captain.Target = Target;
			appliedTarget = Target;
		}
		captain.Sailor.SpawnPoint = SpawnPoint;
		int settings = HashCode.Combine( AutoAcquireTarget, EngagementRange, IslandClearance, CruisingDeployment, WalkSpeed, CaptainTint );
		if ( appliedSettings == settings ) return;
		appliedSettings = settings;
		captain.AutoAcquireTarget = AutoAcquireTarget;
		captain.EngagementRange = Math.Clamp( EngagementRange, 1000, 6000 );
		captain.IslandClearance = Math.Clamp( IslandClearance, 350, 3000 );
		captain.CruisingDeployment = Math.Clamp( CruisingDeployment, 0.2f, 0.75f );
		captain.Sailor.Controller.WalkSpeed = Math.Clamp( WalkSpeed, 40, 220 );
		captain.DeckWalkSpeed = captain.Sailor.Controller.WalkSpeed;
		captain.Sailor.Controller.Renderer.Tint = CaptainTint;
	}

	private GameObject crewRoot;
	private void RemoveCaptain()
	{
		if ( captain.IsValid() )
		{
			captain.Sailor.CurrentStation?.Release( captain.Sailor.Controller );
			captain.GameObject.Destroy();
		}
		if ( crewRoot.IsValid() ) crewRoot.Destroy();
		captain = null;
		crewRoot = null;
	}
	protected override void OnDisabled() => RemoveCaptain();
	protected override void OnDestroy() => RemoveCaptain();
}
