namespace NavalCombat;

/// <summary>Shared local standing interaction for a station on the moving deck.</summary>
public abstract class ShipStation : BaseChair
{
	[Property] public ArcadeShip Ship { get; set; }
	[Property] public float UseDistance { get; set; } = 75;
	[Sync( SyncFlags.FromHost )] public PlayerController StationOccupant { get; private set; }
	public bool Occupied => Networking.IsActive ? StationOccupant.IsValid() : GetOccupant().IsValid();
	public PlayerController Occupant => Networking.IsActive ? StationOccupant : GetOccupant();

	/// <summary>Only the host may directly operate an unowned NPC's own stations.</summary>
	protected bool CanControlNpc( PlayerController player ) => Networking.IsHost && player.IsValid() && Ship.IsValid()
		&& player.Network.Owner is null && Ship.Network.Owner is null
		&& player.GetComponent<ShipPlayer>() is { IsNpc: true } sailor && sailor.Ship == Ship;

	public override bool CanEnter( PlayerController player )
	{
		if ( !Enabled || !Ship.IsValid() || !player.IsValid() || Occupied ) return false;
		if ( !player.Enabled || !player.IsOnGround || !player.Body.IsValid() ) return false;
		if ( Networking.IsActive && player.Network.Owner != Ship.Network.Owner ) return false;
		if ( !player.IsProxy && !player.Body.Enabled ) return false;
		if ( !SeatPosition.IsValid() || (player.WorldPosition - SeatPosition.WorldPosition).Length > UseDistance ) return false;
		var target = WorldPosition + WorldRotation.Up * 45;
		// The authored hull now has detailed deck, rail and interior collision. Do
		// not let parts of the player's own ship occlude its station interaction;
		// distance and grounded checks still prevent remote use.
		var trace = Scene.Trace.Ray( player.EyePosition, target )
			.IgnoreGameObjectHierarchy( player.GameObject )
			.IgnoreGameObjectHierarchy( Ship.GameObject )
			.Run();
		return !trace.Hit;
	}

	public bool TryTake( PlayerController player )
	{
		if ( player.IsValid() && player.IsProxy ) return false;
		if ( !CanEnter( player ) ) return false;
		if ( Networking.IsActive )
		{
			if ( CanControlNpc( player ) )
			{
				StationOccupant = player;
				player.WishVelocity = Vector3.Zero;
				using ( Rpc.FilterInclude( Connection.Local ) ) Sit( player );
				return GetOccupant() == player;
			}
			RequestTake( player );
			return true;
		}
		player.WishVelocity = Vector3.Zero;
		Sit( player );
		StationOccupant = GetOccupant();
		return GetOccupant() == player;
	}

	public void Release( PlayerController player )
	{
		if ( !player.IsValid() || Occupant != player ) return;
		if ( Networking.IsActive )
		{
			if ( CanControlNpc( player ) )
			{
				StationOccupant = null;
				using ( Rpc.FilterInclude( Connection.Local ) ) ReleaseLocal( player );
				return;
			}
			RequestRelease( player );
			return;
		}
		StationOccupant = null;
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
			using ( Rpc.FilterInclude( player.Network.Owner ?? Connection.Local ) ) Eject( player );
			return;
		}
		var occupant = GetOccupant();
		if ( occupant.IsValid() ) ReleaseLocal( occupant );
	}
}
