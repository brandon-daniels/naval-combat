namespace NavalCombat;

/// <summary>Shared local standing interaction for a station on the moving deck.</summary>
public abstract class ShipStation : BaseChair
{
	[Property] public ArcadeShip Ship { get; set; }
	[Property] public float UseDistance { get; set; } = 75;
	[Sync( SyncFlags.FromHost )] public PlayerController StationOccupant { get; private set; }
	public bool Occupied => Networking.IsActive ? StationOccupant.IsValid() : GetOccupant().IsValid();
	public PlayerController Occupant => Networking.IsActive ? StationOccupant : GetOccupant();

	public override bool CanEnter( PlayerController player )
	{
		if ( !Enabled || !Ship.IsValid() || !player.IsValid() || Occupied ) return false;
		if ( !player.Enabled || player.IsProxy || !player.IsOnGround || !player.Body.IsValid() || !player.Body.Enabled ) return false;
		if ( !SeatPosition.IsValid() || (player.WorldPosition - SeatPosition.WorldPosition).Length > UseDistance ) return false;
		var target = WorldPosition + WorldRotation.Up * 45;
		var trace = Scene.Trace.Ray( player.EyePosition, target ).IgnoreGameObjectHierarchy( player.GameObject ).Run();
		return !trace.Hit || trace.GameObject == GameObject || trace.GameObject.IsDescendant( GameObject );
	}

	public bool TryTake( PlayerController player )
	{
		if ( !CanEnter( player ) ) return false;
		if ( Networking.IsActive )
		{
			RequestTake( player );
			return true;
		}
		player.WishVelocity = Vector3.Zero;
		Sit( player );
		return GetOccupant() == player;
	}

	public void Release( PlayerController player )
	{
		if ( !player.IsValid() || Occupant != player ) return;
		if ( Networking.IsActive )
		{
			RequestRelease( player );
			return;
		}
		ReleaseLocal( player );
	}

	[Rpc.Host]
	private void RequestTake( PlayerController player )
	{
		if ( !player.IsValid() || player.Network.Owner != Rpc.Caller || !CanEnter( player ) ) return;
		StationOccupant = player;
		player.WishVelocity = Vector3.Zero;
		using ( Rpc.FilterInclude( player.Network.Owner ) ) Sit( player );
	}

	[Rpc.Host]
	private void RequestRelease( PlayerController player )
	{
		if ( !player.IsValid() || player.Network.Owner != Rpc.Caller || StationOccupant != player ) return;
		StationOccupant = null;
		using ( Rpc.FilterInclude( player.Network.Owner ) ) Eject( player );
	}

	private void ReleaseLocal( PlayerController player )
	{
		var eyes = player.EyeTransform.Rotation.Angles();
		Eject( player );
		player.EyeAngles = eyes.WithRoll( 0 );
		player.WishVelocity = Vector3.Zero;
		if ( player.Renderer.IsValid() )
		{
			player.Renderer.ClearIk( "hand_left" );
			player.Renderer.ClearIk( "hand_right" );
		}
		player.GetComponent<ShipPlayer>()?.InheritDeckVelocity();
	}

	protected override void OnDisabled()
	{
		if ( Networking.IsActive && Networking.IsHost && StationOccupant.IsValid() )
		{
			var player = StationOccupant;
			StationOccupant = null;
			using ( Rpc.FilterInclude( player.Network.Owner ) ) Eject( player );
			return;
		}
		var occupant = GetOccupant();
		if ( occupant.IsValid() ) ReleaseLocal( occupant );
	}
}
