using System;
using System.Threading.Tasks;

namespace NavalCombat;

/// <summary>Runs the real 60-second cycle in a local editor session. Restart Play afterward.</summary>
public static class ProductionSmokeTest
{
	private static bool running;

	[ConCmd( "naval_preview_investment" )]
	public static void PreviewDialog()
	{
		if ( !Game.IsEditor || Networking.IsActive || running ) return;
		var voyage = Game.ActiveScene?.GetAllComponents<PlayerVoyage>().FirstOrDefault();
		if ( !voyage.IsValid() ) return;
		var port = voyage.Scene.GetAllComponents<VoyagePort>().FirstOrDefault( x => x.ProducesGoods );
		if ( !port.IsValid() ) return;
		var sailor = voyage.Sailor;
		sailor.CurrentStation?.Release( sailor.Controller );
		sailor.Ship.WorldPosition = port.WorldPosition + new Vector3( -200, 0, 10 );
		sailor.Ship.Body.Velocity = Vector3.Zero;
		sailor.Ship.Body.MotionEnabled = false;
		sailor.ReturnToDeck();
		voyage.OpenInvestment();
		Log.Info( "Investment preview opened. Restart Play after inspecting the dialog." );
	}
	[ConCmd( "naval_test_production" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || Networking.IsActive || running ) return;
		var voyage = Game.ActiveScene?.GetAllComponents<PlayerVoyage>().FirstOrDefault();
		if ( !voyage.IsValid() ) return;
		running = true;
		var sailor = voyage.Sailor;
		var ship = sailor.Ship;
		var position = ship.WorldPosition;
		var motion = ship.Body.MotionEnabled;
		var input = sailor.Controller.UseInputControls;
		var look = sailor.Controller.UseLookControls;
		GameObject fixture = null;
		try
		{
			var port = sailor.Scene.GetAllComponents<VoyagePort>().Single( x => x.ProducesGoods );
			Check( port.BarrelSpawn.IsValid() && port.CaneField.IsValid(), "Saved spawn marker and growing area resolve" );
			Check( voyage.Production.ProductionSeconds == 60 && voyage.Production.InvestmentCost == 100, "60-second / $100 settings" );
			Check( !voyage.Production.IsProducing && voyage.Wallet.Balance >= 100, "Fresh available production" );
			sailor.CurrentStation?.Release( sailor.Controller );
			ship.WorldPosition = port.WorldPosition + new Vector3( -200, 0, 10 );
			ship.Body.Velocity = Vector3.Zero;
			ship.Body.AngularVelocity = Vector3.Zero;
			ship.Body.MotionEnabled = false;
			sailor.ReturnToDeck();
			await GameTask.DelaySeconds( 0.5f );
			int balance = voyage.Wallet.Balance;
			voyage.OpenInvestment();
			Check( voyage.InvestmentOpen && !sailor.Controller.UseInputControls && !sailor.Controller.UseLookControls, "Dialog opens and captures player controls" );
			voyage.CloseInvestment();
			Check( voyage.Wallet.Balance == balance && !voyage.Production.IsProducing, "Cancel is free and starts nothing" );
			Check( sailor.Controller.UseInputControls == input && sailor.Controller.UseLookControls == look, "Cancel restores controls" );
			voyage.OpenInvestment();
			voyage.ConfirmInvestment();
			Check( !voyage.InvestmentOpen && voyage.Production.IsProducing && voyage.Wallet.Balance == balance - 100, "Confirm debits exactly $100 and starts production" );
			Check( voyage.Production.SourcePort == port, "Production retains the chosen buoy" );
			Check( !voyage.Production.Invest( voyage.Identity, voyage.Wallet ).Success && voyage.Wallet.Balance == balance - 100, "Duplicate investment rejected without debit" );
			fixture = new GameObject( sailor.Scene, true, "Production contention test" );
			var other = fixture.AddComponent<ProductionSite>();
			other.SourcePort = port;
			other.OutputGoods = voyage.Production.OutputGoods;
			Check( !other.Invest( "other", voyage.Wallet ).Success && voyage.Wallet.Balance == balance - 100, "Shared field rejects overlapping batches" );
			fixture.Destroy();
			sailor.Controller.UseInputControls = false;
			sailor.Controller.UseLookControls = false;
			await GameTask.DelaySeconds( 41 );
			Check( voyage.Production.IsProducing && voyage.Production.Progress > 2f / 3 && voyage.Production.Progress < 52f / 60, "Real-time harvest phase begins after growth" );
			Check( port.CaneField.GameObject.GetAllObjects( true ).Count( x => x.Name == "Spinning scythe" && x.Enabled ) == 2, "Both animated scythes are visible during live harvesting" );
			await GameTask.DelaySeconds( 12 );
			Check( voyage.Production.IsProducing && voyage.Production.Progress > 52f / 60, "Packing follows harvesting" );
			Check( port.CaneField.GameObject.GetAllObjects( true ).Any( x => x.Name == "Barrel being packed" && x.Enabled ), "Live packing barrel is visible" );
			Check( !sailor.Scene.GetAllComponents<GoodsBarrel>().Any( x => x.OwnerId == voyage.Identity ), "No usable goods before packing finishes" );
			await GameTask.DelaySeconds( 7.2f );
			var barrel = sailor.Scene.GetAllComponents<GoodsBarrel>().Single( x => x.OwnerId == voyage.Identity );
			Check( !voyage.Production.IsProducing && barrel.Quantity == voyage.Production.OutputQuantity, "Exactly one completed batch after 60 seconds" );
			Check( barrel.GetComponent<Rigidbody>().Enabled && barrel.GetComponent<CapsuleCollider>().IsDynamic, "Spawned barrel has a real dynamic collision shape" );
			Check( barrel.WorldPosition.WithZ( 0 ).Distance( port.BarrelPosition.WithZ( 0 ) ) < 60, "Barrel uses the authored spawn position" );
			barrel.WorldPosition = port.WorldPosition + new Vector3( 0, -1200, 300 );
			barrel.GetComponent<Rigidbody>().Velocity = Vector3.Zero;
			barrel.GetComponent<Rigidbody>().Sleeping = false;
			await GameTask.DelaySeconds( 0.5f );
			Check( barrel.WorldPosition.z < port.WorldPosition.z + 260, "Loose barrel actually falls under gravity" );
			await GameTask.DelaySeconds( 3 );
			Check( Math.Abs( barrel.WorldPosition.z - ship.Ocean.HeightAt( barrel.WorldPosition, Time.Now ) ) < 100, "Physical barrel floats near the water surface" );
			Check( sailor.Scene.GetAllComponents<GoodsBarrel>().Count( x => x.OwnerId == voyage.Identity ) == 1, "Completion does not duplicate cargo" );
			Log.Info( "PRODUCTION TEST PASSED: dialog/cancel, payment, contention, real 60-second phases, authored spawn and floating physics. Restart Play for a fresh voyage." );
		}
		catch ( Exception e ) { Log.Error( $"PRODUCTION TEST FAILED: {e.Message}" ); }
		finally
		{
			if ( fixture.IsValid() ) fixture.Destroy();
			voyage.CloseInvestment();
			ship.WorldPosition = position;
			ship.Body.MotionEnabled = motion;
			sailor.ReturnToDeck();
			sailor.Controller.UseInputControls = input;
			sailor.Controller.UseLookControls = look;
			running = false;
		}
	}

	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
		Log.Info( $"PRODUCTION TEST OK: {message}" );
	}
}
