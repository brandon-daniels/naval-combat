namespace NavalCombat;

/// <summary>A standing helm using the engine's mounted player movement mode.</summary>
public sealed class ShipHelm : ShipStation
{
	[Property] public GameObject LeftGrip { get; set; }
	[Property] public GameObject RightGrip { get; set; }

	public override void UpdatePlayerAnimator( PlayerController player, SkinnedModelRenderer renderer )
	{
		base.UpdatePlayerAnimator( player, renderer );
		if ( LeftGrip.IsValid() ) renderer.SetIk( "hand_left", LeftGrip.WorldTransform );
		if ( RightGrip.IsValid() ) renderer.SetIk( "hand_right", RightGrip.WorldTransform );
	}
}
