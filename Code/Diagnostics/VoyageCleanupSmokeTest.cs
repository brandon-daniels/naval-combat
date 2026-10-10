using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NavalCombat;

/// <summary>Checks disconnect cargo cleanup without disconnecting or changing a real voyage.</summary>
public static class VoyageCleanupSmokeTest
{
	private static bool running;
	private static bool watchingDisconnect;

	/// <summary>Stages one batch per owner, then waits for the remote client to leave.</summary>
	[ConCmd( "naval_test_disconnect_cargo" )]
	public static async Task WatchDisconnect()
	{
		if ( !Game.IsEditor || !Networking.IsActive || !Networking.IsHost || watchingDisconnect ) return;
		var scene = Game.ActiveScene;
		var voyages = scene.GetAllComponents<PlayerVoyage>().ToArray();
		if ( voyages.Length != 2 || scene.GetAllComponents<GoodsBarrel>().Any()
			|| voyages.Any( x => x.Production.IsProducing || !x.Wallet.CanAfford( x.Production.InvestmentCost ) ) )
		{
			Log.Warning( "Disconnect cargo test needs two players, no existing barrels or production, and enough money to invest. It spends one investment per player." );
			return;
		}
		var remote = voyages.Single( x => x.Network.Owner != Connection.Local );
		var remaining = voyages.Single( x => x.Network.Owner == Connection.Local );
		var owner = remote.Network.Owner.Id;
		var identity = remote.Identity;
		var ship = remote.Sailor.Ship;
		var durations = voyages.Select( x => x.Production.ProductionSeconds ).ToArray();
		watchingDisconnect = true;
		try
		{
			foreach ( var voyage in voyages )
			{
				voyage.Production.ProductionSeconds = 0.1f;
				Check( voyage.Production.Invest( voyage.Identity, voyage.Wallet ).Success, "Disconnect fixture invests in one real batch" );
			}
			await GameTask.DelaySeconds( 1 );
			var departingCargo = scene.GetAllComponents<GoodsBarrel>().Single( x => x.OwnerId == identity );
			var retainedCargo = scene.GetAllComponents<GoodsBarrel>().Single( x => x.OwnerId == remaining.Identity );
			var balance = remaining.Wallet.Balance;
			var capacity = remaining.Cargo.UsedCapacity;
			Log.Info( "DISCONNECT CARGO TEST READY: close the remote client within 60 seconds; leave the editor host running." );
			for ( int i = 0; i < 120 && Connection.All.Any( x => x.Id == owner ); i++ )
				await GameTask.DelaySeconds( 0.5f );
			Check( !Connection.All.Any( x => x.Id == owner ), "Remote connection left" );
			await GameTask.DelaySeconds( 0.5f );
			Check( !remote.IsValid() && !ship.IsValid() && !departingCargo.IsValid(), "Actual disconnect removes the sailor, ship and produced cargo" );
			Check( retainedCargo.IsValid() && retainedCargo.OwnerId == remaining.Identity
				&& retainedCargo.Quantity == remaining.Production.OutputQuantity
				&& remaining.Wallet.Balance == balance && remaining.Cargo.UsedCapacity == capacity,
				"Other player's batch, wallet and hold survive disconnect unchanged" );
			NavalNetworkSmokeTest.Run();
			Log.Info( "DISCONNECT CARGO TEST PASSED. Rejoin and run naval_test_network_shell to check the fresh voyage." );
		}
		catch ( Exception e ) { Log.Error( $"DISCONNECT CARGO TEST FAILED: {e.Message}" ); }
		finally
		{
			for ( int i = 0; i < voyages.Length; i++ )
				if ( voyages[i].IsValid() ) voyages[i].Production.ProductionSeconds = durations[i];
			watchingDisconnect = false;
		}
	}

	[ConCmd( "naval_test_voyage_cleanup" )]
	public static async Task Run()
	{
		if ( running ) return;
		if ( !Game.IsEditor || Networking.IsActive )
		{
			Log.Warning( "Voyage cleanup test requires local editor Play with networking inactive." );
			return;
		}

		var scene = Game.ActiveScene;
		var session = scene.GetAllComponents<NavalMultiplayerSession>().SingleOrDefault();
		if ( !session.IsValid() ) return;
		var fixture = new GameObject( scene, true, "Voyage cleanup test fixture" );
		var barrels = new List<GoodsBarrel>();
		running = true;
		try
		{
			// Tear down fixture gameplay objects before yielding to the next frame.
			var ship = fixture.AddComponent<ArcadeShip>();
			fixture.AddComponent<CargoHold>();
			var player = fixture.AddComponent<ShipPlayer>();
			player.Ship = ship;
			var voyage = fixture.AddComponent<PlayerVoyage>();
			voyage.PlayerId = Guid.NewGuid().ToString();
			var identity = voyage.Identity;
			var wallet = fixture.AddComponent<PlayerWallet>();
			var production = fixture.AddComponent<ProductionSite>();
			production.InvestmentCost = 0;
			production.OutputGoods = ResourceLibrary.Get<GoodsDefinition>( "definitions/goods/trade_goods.ngoods" );
			Check( production.Invest( voyage.Identity, wallet ).Success, "Fixture production reserves an identity" );

			GoodsBarrel Spawn()
			{
				var barrel = GoodsBarrel.Spawn( production, fixture.WorldPosition );
				barrels.Add( barrel );
				return barrel;
			}

			var loose = Spawn();
			var secured = Spawn();
			Check( secured.TryPickup( player ) && secured.DropOrSecure( player ) && secured.SecuredShip == ship,
				"Fixture has secured cargo" );
			var carried = Spawn();
			Check( carried.TryPickup( player ), "Fixture has carried cargo" );
			var otherSource = new GameObject( fixture, true, "Other voyage" ).AddComponent<ProductionSite>();
			otherSource.OutputGoods = production.OutputGoods;
			otherSource.InvestmentCost = 0;
			Check( otherSource.Invest( Guid.NewGuid().ToString(), wallet ).Success, "Other voyage has a separate identity" );
			var other = GoodsBarrel.Spawn( otherSource, fixture.WorldPosition );
			barrels.Add( other );

			session.RemoveVoyageCargo( null );
			Check( barrels.All( x => x.IsValid() ), "Missing identity does not delete cargo" );
			session.RemoveVoyageCargo( identity );
			var quantity = production.OutputQuantity;
			fixture.Destroy();
			await GameTask.DelaySeconds( 0.1f );
			Check( !loose.IsValid() && !secured.IsValid() && !carried.IsValid(), "Departing voyage loses loose, secured and carried barrels" );
			Check( other.IsValid() && other.Quantity == quantity, "Other voyage cargo is unchanged" );
			session.RemoveVoyageCargo( identity );
			Check( other.IsValid(), "Repeated cleanup is harmless" );
			Log.Info( "VOYAGE CLEANUP TEST PASSED" );
		}
		catch ( Exception e )
		{
			Log.Error( $"VOYAGE CLEANUP TEST FAILED: {e.Message}" );
		}
		finally
		{
			foreach ( var barrel in barrels ) if ( barrel.IsValid() ) barrel.GameObject.Destroy();
			if ( fixture.IsValid() ) fixture.Destroy();
			running = false;
		}
	}

	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
		Log.Info( $"VOYAGE CLEANUP TEST OK: {message}" );
	}
}
