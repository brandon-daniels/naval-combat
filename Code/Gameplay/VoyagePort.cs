namespace NavalCombat;

/// <summary>Editor-placed offshore trade buoy. Ships trade within its radius at low speed.</summary>
public sealed class VoyagePort : Component
{
	[Property] public string PortName { get; set; } = "Port";
	[Property] public bool ProducesGoods { get; set; }
	[Property] public int SalePrice { get; set; } = 18;
	[Property] public float Radius { get; set; } = 650;
	[Property] public GoodsDefinition Goods { get; set; }
	public IslandPort Port { get; private set; }

	protected override void OnStart()
	{
		Port = GameObject.AddComponent<IslandPort>();
		Port.PortId = PortName;
		Port.InteractionRadius = Radius;
		Port.Market = GameObject.AddComponent<IslandMarket>();
		Port.Market.Listings.Add( new MarketListing { Goods = Goods, SellPrice = SalePrice, BuyPrice = 0 } );
	}

	public bool CanTrade( ShipPlayer sailor ) => sailor.IsValid() && sailor.Ship.IsValid()
		&& Port.IsValid() && Port.Contains( sailor.Ship.GameObject )
		&& sailor.WorldPosition.Distance( sailor.Ship.WorldPosition ) < 450
		&& sailor.Ship.Body.Velocity.WithZ( 0 ).Length < 100;
}
