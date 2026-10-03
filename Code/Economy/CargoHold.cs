using System;
using System.Collections.Generic;

namespace NavalCombat;

public sealed class CargoHold : Component
{
	[Property] public int Capacity { get; set; } = 20;
	[Sync( SyncFlags.FromHost )] public int UsedCapacity { get; private set; }
	[Sync( SyncFlags.FromHost )] public int Revision { get; private set; }

	private readonly Dictionary<string, int> quantities = new( StringComparer.OrdinalIgnoreCase );

	public int FreeCapacity => Math.Max( 0, Capacity - UsedCapacity );

	public int GetQuantity( GoodsDefinition goods )
	{
		if ( goods is null ) return 0;
		return quantities.TryGetValue( goods.StableId, out var quantity ) ? quantity : 0;
	}

	public IReadOnlyDictionary<string, int> GetSnapshot()
	{
		return new Dictionary<string, int>( quantities, StringComparer.OrdinalIgnoreCase );
	}

	public bool CanAdd( GoodsDefinition goods, int quantity )
	{
		if ( goods is null || quantity <= 0 ) return false;
		long required = (long)goods.SafeUnitSize * quantity;
		return required <= FreeCapacity;
	}

	public bool CanRemove( GoodsDefinition goods, int quantity )
	{
		return goods is not null && quantity > 0 && GetQuantity( goods ) >= quantity;
	}

	public bool TryAdd( GoodsDefinition goods, int quantity )
	{
		if ( !GameplayAuthority.CanMutate || !CanAdd( goods, quantity ) ) return false;
		quantities[goods.StableId] = GetQuantity( goods ) + quantity;
		UsedCapacity += goods.SafeUnitSize * quantity;
		Revision++;
		return true;
	}

	public bool TryRemove( GoodsDefinition goods, int quantity )
	{
		if ( !GameplayAuthority.CanMutate || !CanRemove( goods, quantity ) ) return false;
		var remaining = GetQuantity( goods ) - quantity;
		if ( remaining == 0 ) quantities.Remove( goods.StableId );
		else quantities[goods.StableId] = remaining;
		UsedCapacity = Math.Max( 0, UsedCapacity - goods.SafeUnitSize * quantity );
		Revision++;
		return true;
	}
}
