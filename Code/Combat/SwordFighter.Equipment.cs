namespace NavalCombat;

public enum HeldItem { Sword, Bucket, Planks }

public sealed partial class SwordFighter
{
	[Sync( SyncFlags.FromHost )] public HeldItem EquippedItem { get; private set; } = HeldItem.Sword;

	public bool CanHoldItem => Active && Health > 0 && Controller.IsValid() && Controller.Enabled && !Controller.IsSwimming
		&& (Sailor is not { } sailor || (!sailor.CurrentStation.IsValid() && !sailor.CarriedBarrel.IsValid()
			&& GetComponent<PlayerVoyage>() is not { InvestmentOpen: true }));

	private void ReadEquipmentInput()
	{
		if ( Input.Pressed( "Slot1" ) ) Equip( HeldItem.Sword );
		else if ( Input.Pressed( "Slot2" ) ) Equip( HeldItem.Bucket );
		else if ( Input.Pressed( "Slot3" ) ) Equip( HeldItem.Planks );
		else if ( Input.Pressed( "SlotPrev" ) ) SelectItem( EquippedItem, -1 );
		else if ( Input.Pressed( "SlotNext" ) ) SelectItem( EquippedItem, 1 );
	}

	public void Equip( HeldItem item ) => SelectItem( item, 0 );

	private void SelectItem( HeldItem item, int step )
	{
		if ( !IsLocalPlayer || InDuel || !CanHoldItem ) return;
		if ( Authority ) ApplySelection( item, step );
		else RequestEquip( item, step );
	}

	[Rpc.Host( NetFlags.OwnerOnly | NetFlags.Reliable )]
	private void RequestEquip( HeldItem item, int step )
	{
		if ( Rpc.Caller == Network.Owner ) ApplySelection( item, step );
	}

	private void ApplySelection( HeldItem item, int step )
	{
		// Resolve each cycle on the host, even if several presses arrive before the owner sees a sync.
		if ( step is -1 or 1 ) item = (HeldItem)(((int)EquippedItem + step + 3) % 3);
		else if ( step != 0 ) return;
		TryEquip( item );
	}

	internal bool TryEquip( HeldItem item )
	{
		if ( !Authority || IsNpc || InDuel || !CanHoldItem || item is not (HeldItem.Sword or HeldItem.Bucket or HeldItem.Planks) ) return false;
		if ( EquippedItem == item ) return true;
		Sailor?.CancelDamageControl();
		EquippedItem = item;
		return true;
	}
}
