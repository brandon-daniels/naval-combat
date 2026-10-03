namespace NavalCombat;

/// <summary>Switches a Citizen between deck movement and an occupied helm.</summary>
public sealed class ShipPlayer : Component
{
	[Property] public PlayerController Controller { get; set; }
	[Property] public ShipHelm Helm { get; set; }
	[Property] public SailRig Sails { get; set; }
	[Property] public ArcadeShip Ship { get; set; }
	[Property] public GameObject SpawnPoint { get; set; }
	[Property] public bool IsNpc { get; set; }
	public bool IsAtHelm => Helm.IsValid() && Helm.Occupant == Controller;
	public bool CanUseHelm => Controller.IsValid() && Helm.IsValid() && Helm.CanEnter( Controller );
	public bool IsAtMast => Sails.IsValid() && Sails.Occupant == Controller;
	public ShipCannon ActiveCannon => Scene.GetAllComponents<ShipCannon>().FirstOrDefault( x => x.Occupant == Controller );
	public ShipStation CurrentStation => IsAtHelm ? Helm : IsAtMast ? Sails : ActiveCannon;
	public ShipStation AvailableStation => CurrentStation.IsValid() ? null : Scene.GetAllComponents<ShipStation>()
		.Where( x => x.Ship == Ship && x.CanEnter( Controller ) )
		.OrderBy( x => (WorldPosition - x.SeatPosition.WorldPosition).Length ).FirstOrDefault();
	private bool inheritVelocity;

	public void InheritDeckVelocity() => inheritVelocity = true;

	public void ToggleHelm()
	{
		if ( IsAtHelm ) Helm.Release( Controller );
		else if ( !CurrentStation.IsValid() && Helm.IsValid() ) Helm.TryTake( Controller );
	}

	public void ToggleStation()
	{
		if ( CurrentStation.IsValid() ) CurrentStation.Release( Controller );
		else AvailableStation?.TryTake( Controller );
	}

	protected override void OnUpdate()
	{
		if ( IsNpc || IsProxy || !Controller.IsValid() ) return;
		if ( Input.Pressed( "Use" ) ) ToggleStation();
		if ( Input.Pressed( "Reload" ) ) ReturnToDeck();

		if ( ActiveCannon is { } cannon )
		{
			cannon.Aim( Controller, (Input.Down( "Left" ) ? 1 : 0) - (Input.Down( "Right" ) ? 1 : 0), (Input.Down( "Forward" ) ? 1 : 0) - (Input.Down( "Backward" ) ? 1 : 0), Time.Delta );
			if ( Input.Pressed( "Attack1" ) ) cannon.Fire( Controller );
		}
		else if ( IsAtHelm || IsAtMast )
		{
			float forward = (Input.Down( "Forward" ) ? 1 : 0) - (Input.Down( "Backward" ) ? 1 : 0);
			float turn = (Input.Down( "Left" ) ? 1 : 0) - (Input.Down( "Right" ) ? 1 : 0);
			if ( IsAtHelm ) Ship.SetHelmInput( Helm, turn );
			else Sails.Adjust( Controller, turn, -forward, Time.Delta );
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy || !Controller.IsValid() || !Ship.IsValid() ) return;
		if ( inheritVelocity && Controller.Body.IsValid() && Controller.Body.Enabled )
		{
			Controller.Body.Velocity = Ship.Body.GetVelocityAtPoint( WorldPosition );
			inheritVelocity = false;
		}
		// Human players swim; only the autonomous captain needs overboard recovery.
		if ( IsNpc && !CurrentStation.IsValid() && WorldPosition.z < Ship.Ocean.HeightAt( WorldPosition, Time.Now ) - 80 ) ReturnToDeck();
	}

	public void ReturnToDeck()
	{
		if ( !SpawnPoint.IsValid() || !Controller.IsValid() ) return;
		CurrentStation?.Release( Controller );
		WorldPosition = SpawnPoint.WorldPosition;
		WorldRotation = Rotation.FromYaw( Ship.WorldRotation.Angles().yaw );
		Controller.EyeAngles = WorldRotation.Angles().WithPitch( 15 );
		Controller.WishVelocity = Vector3.Zero;
		Transform.ClearInterpolation();
		InheritDeckVelocity();
	}

	protected override void OnDisabled()
	{
		CurrentStation?.Release( Controller );
	}
}
