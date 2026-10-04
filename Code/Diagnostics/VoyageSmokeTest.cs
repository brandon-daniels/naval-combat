using System;
using System.Threading.Tasks;

namespace NavalCombat;

public static class VoyageSmokeTest
{
	private static bool running;

	[ConCmd( "naval_test_voyage" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || Networking.IsActive || running ) return;
		var voyage = Game.ActiveScene?.GetAllComponents<PlayerVoyage>().FirstOrDefault();
		if ( !voyage.IsValid() ) return;
		running = true;
		var sailor = voyage.Sailor;
		var position = sailor.Ship.WorldPosition;
		var duration = voyage.Production.ProductionSeconds;
		var controller = sailor.Controller;
		var input = controller.UseInputControls;
		var look = controller.UseLookControls;
		try
		{
			controller.UseInputControls = false;
			controller.UseLookControls = false;
			sailor.CurrentStation?.Release( controller );
			sailor.Sails.Deployment = 0;
			Check( voyage.Production.OutputGoods?.StableId == "goods.trade_goods", $"Goods resource loads its authored values (actual: {voyage.Production.OutputGoods?.StableId}, path: {voyage.Production.OutputGoods?.ResourcePath})" );
			Check( voyage.Upgrades.AvailableUpgrades.Count == 3 && voyage.Upgrades.AvailableUpgrades.All( x => x.Id.StartsWith( "upgrade." ) ), "All three upgrade resources load" );
			Check( voyage.Wallet.Balance == 500 && voyage.Cargo.UsedCapacity == 0, "Fresh voyage starts with 500 money and empty cargo" );
			Check( !voyage.Execute( 1 ).Success, "Investment rejected outside port" );
			var beacon = sailor.Scene.GetAllComponents<VoyagePort>().Single( x => x.ProducesGoods );
			var palm = sailor.Scene.GetAllComponents<VoyagePort>().Single( x => x.PortName == "Palm Island" );
			Move( beacon.WorldPosition );
			await GameTask.DelaySeconds( 0.4f );
			voyage.Production.ProductionSeconds = 0.5f;
			Check( voyage.Execute( 1 ).Success && voyage.Wallet.Balance == 400, "Investment atomically debits 100" );
			Check( !voyage.Production.Claim( voyage.Identity, voyage.Cargo ).Success, "Early claim rejected" );
			await GameTask.DelaySeconds( 0.8f );
			Check( voyage.Cargo.TryAdd( voyage.Production.OutputGoods, 20 ), "Full-hold fixture loaded" );
			Check( !voyage.Execute( 1 ).Success && voyage.Production.HasOutput, "Full hold preserves claimable production" );
			voyage.Cargo.TryRemove( voyage.Production.OutputGoods, 20 );
			await GameTask.DelaySeconds( 0.3f );
			Check( voyage.Execute( 1 ).Success && voyage.Cargo.UsedCapacity == 10, "Completed production loads ten goods" );
			Check( !voyage.Production.Claim( voyage.Identity, voyage.Cargo ).Success && voyage.Cargo.UsedCapacity == 10, "Duplicate claim cannot mint goods" );
			Move( palm.WorldPosition );
			await GameTask.DelaySeconds( 0.4f );
			Check( voyage.Execute( 2 ).Success && voyage.Wallet.Balance == 750 && voyage.Cargo.UsedCapacity == 0, "Palm sale pays 350 and removes cargo" );
			await GameTask.DelaySeconds( 0.3f );
			Check( voyage.Execute( 3 ).Success && voyage.Wallet.Balance == 450, "Expanded hold costs 300" );
			await GameTask.DelaySeconds( 0.3f );
			Check( voyage.Cargo.Capacity == 25, "Cargo upgrade applies its modifier" );
			Check( voyage.Execute( 5 ).Success && voyage.Wallet.Balance == 125, "Improved rigging costs 325" );
			await GameTask.DelaySeconds( 0.3f );
			Check( sailor.Ship.ForwardSpeed > 650, "Rigging upgrade applies its speed modifier" );
			Check( !voyage.Execute( 4 ).Success && voyage.Wallet.Balance == 125, "Unaffordable hull upgrade does not debit money" );
			voyage.Wallet.TryCredit( 225 );
			await GameTask.DelaySeconds( 0.3f );
			Check( voyage.Execute( 4 ).Success && voyage.Wallet.Balance == 0, "Hull upgrade costs 350" );
			var hull = sailor.Ship.GetComponent<ShipHealth>();
			Check( hull.MaximumHealth == 1150 && hull.Health == 1150, "Hull upgrade adds 150 maximum and current health" );
			Log.Info( "VOYAGE TEST PASSED: invest, produce, load, sell and upgrade. Restart Play for a fresh wallet." );
		}
		catch ( Exception e ) { Log.Error( $"VOYAGE TEST FAILED: {e.Message}" ); }
		finally
		{
			voyage.Production.ProductionSeconds = duration;
			Move( position );
			controller.UseInputControls = input;
			controller.UseLookControls = look;
			running = false;
		}

		void Move( Vector3 target )
		{
			sailor.Ship.WorldPosition = target.WithZ( 10 );
			sailor.Ship.Body.Velocity = Vector3.Zero;
			sailor.Ship.Body.AngularVelocity = Vector3.Zero;
			sailor.Ship.Transform.ClearInterpolation();
			sailor.ReturnToDeck();
		}
	}

	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
		Log.Info( $"VOYAGE TEST OK: {message}" );
	}
}
