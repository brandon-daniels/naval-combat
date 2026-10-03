namespace NavalCombat;

/// <summary>Shared local standing interaction for a station on the moving deck.</summary>
public abstract class ShipStation : BaseChair
{
	[Property] public ArcadeShip Ship { get; set; }
	[Property] public float UseDistance { get; set; } = 75;

	public override bool CanEnter( PlayerController player )
	{
		if ( Networking.IsActive || !Enabled || !Ship.IsValid() || !player.IsValid() ) return false;
		if ( !player.Enabled || player.IsProxy || !player.IsOnGround || !player.Body.IsValid() || !player.Body.Enabled ) return false;
		if ( !base.CanEnter( player ) || !SeatPosition.IsValid() || (player.WorldPosition - SeatPosition.WorldPosition).Length > UseDistance ) return false;
		var target = WorldPosition + WorldRotation.Up * 45;
		var trace = Scene.Trace.Ray( player.EyePosition, target ).IgnoreGameObjectHierarchy( player.GameObject ).Run();
		return !trace.Hit || trace.GameObject == GameObject || trace.GameObject.IsDescendant( GameObject );
	}

	public bool TryTake( PlayerController player )
	{
		if ( !CanEnter( player ) ) return false;
		player.WishVelocity = Vector3.Zero;
		Sit( player );
		return GetOccupant() == player;
	}

	public void Release( PlayerController player )
	{
		if ( !player.IsValid() || GetOccupant() != player ) return;
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

	protected override void OnDisabled() => Release( GetOccupant() );
}
