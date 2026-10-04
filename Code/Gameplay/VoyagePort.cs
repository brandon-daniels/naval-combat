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
		var buoy = new GameObject( GameObject, true, "Trade buoy" );
		buoy.NetworkMode = NetworkMode.Never;
		buoy.LocalPosition = Vector3.Up * 70;
		buoy.LocalScale = new Vector3( 1.5f, 1.5f, 4 );
		var model = buoy.AddComponent<ModelRenderer>();
		model.Model = Model.Load( "models/dev/box.vmdl" );
		model.Tint = ProducesGoods ? new Color( 1, 0.7f, 0.2f ) : new Color( 0.2f, 0.9f, 0.65f );
	}

	public bool CanTrade( ShipPlayer sailor ) => sailor.IsValid() && sailor.Ship.IsValid()
		&& Port.IsValid() && Port.Contains( sailor.Ship.GameObject )
		&& sailor.WorldPosition.Distance( sailor.Ship.WorldPosition ) < 450
		&& sailor.Ship.Body.Velocity.WithZ( 0 ).Length < 100;
}
