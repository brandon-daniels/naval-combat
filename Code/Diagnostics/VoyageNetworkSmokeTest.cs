using System;
using System.Threading.Tasks;

namespace NavalCombat;

/// <summary>Exercises real owner RPCs from two instances. Restart Play after this destructive test voyage.</summary>
public static class VoyageNetworkSmokeTest
{
	private static bool running;

	[ConCmd( "naval_test_voyage_network" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || !Networking.IsActive || !Networking.IsHost || running ) return;
		var voyages = Game.ActiveScene.GetAllComponents<PlayerVoyage>().ToArray();
		if ( voyages.Length != 2 ) { Log.Warning( "Network voyage test needs exactly two active players." ); return; }
		if ( voyages.Any( x => x.Wallet.Balance != 500 || x.Cargo.UsedCapacity != 0 ) )
		{
			Log.Warning( "Network voyage test needs fresh wallets and empty holds. Restart Play first." );
			return;
		}
		running = true;
		try
		{
			NavalNetworkSmokeTest.Run();
			foreach ( var voyage in voyages ) voyage.Production.ProductionSeconds = 0.5f;
			Stage( 0 );
			await GameTask.DelaySeconds( 2 );
			Stage( 1 );
			await GameTask.DelaySeconds( 1 );
			foreach ( var voyage in voyages ) Log.Info( $"NETWORK VOYAGE STATE {voyage.Network.Owner?.DisplayName}: money={voyage.Wallet.Balance}, ready={voyage.Production.HasOutput}, ship={voyage.Sailor.Ship.WorldPosition}, sailor={voyage.WorldPosition}, feedback={voyage.Feedback}" );
			Check( voyages.All( x => x.Wallet.Balance == 400 && x.Production.HasOutput ), "Both owner investment RPCs charge 100 and finish production" );
			Stage( 1 );
			await GameTask.DelaySeconds( 1 );
			Check( voyages.All( x => x.Cargo.UsedCapacity == 10 ), "Both owners load ten goods" );
			Stage( 2 );
			await GameTask.DelaySeconds( 2 );
			Stage( 3 );
			await GameTask.DelaySeconds( 1 );
			Check( voyages.All( x => x.Cargo.UsedCapacity == 0 && x.Wallet.Balance == 750 ), "Both sale RPCs conserve cargo and pay 350" );
			Stage( 4 );
			await GameTask.DelaySeconds( 1 );
			Check( voyages.All( x => x.Cargo.Capacity == 25 && x.Wallet.Balance == 450 ), "Both cargo upgrade RPCs apply and charge correctly" );
			Stage( 5 );
			await GameTask.DelaySeconds( 1 );
			for ( int i = 0; i < 12; i++ )
			{
				Stage( 6 );
				await GameTask.DelaySeconds( 0.25f );
				if ( voyages.All( x => x.Sailor.ActiveCannon.IsValid() ) ) break;
			}
			Check( voyages.All( x => x.Sailor.ActiveCannon.IsValid() ), "Host accepts both owners taking their own cannons" );
			Stage( 7 );
			await GameTask.DelaySeconds( 0.35f );
			Check( voyages.All( x => x.Sailor.ActiveCannon.ReloadRemaining > 0 ), "Both owner fire RPCs start host reload cooldowns" );
			Stage( 8 );
			await GameTask.DelaySeconds( 1 );
			Check( voyages.All( x => !x.Sailor.CurrentStation.IsValid() ), "Both stations release on the host" );
			Log.Info( "VOYAGE NETWORK TEST PASSED: two real owners invested, loaded, sold, upgraded, mounted and fired." );
		}
		catch ( Exception e ) { Log.Error( $"VOYAGE NETWORK TEST FAILED: {e.Message}" ); }
		finally
		{
			foreach ( var voyage in voyages ) if ( voyage.IsValid() ) voyage.Production.ProductionSeconds = 60;
			Stage( 9 );
			running = false;
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void Stage( int stage )
	{
		var voyage = Game.ActiveScene?.GetAllComponents<PlayerVoyage>().FirstOrDefault( x => !x.IsProxy && x.Network.Owner == Connection.Local );
		if ( !voyage.IsValid() ) return;
		var sailor = voyage.Sailor;
		var controller = sailor.Controller;
		if ( stage == 0 )
		{
			controller.UseInputControls = false;
			controller.UseLookControls = false;
			sailor.Enabled = false;
			sailor.Sails.Deployment = 0;
		}
		if ( stage is 0 or 2 )
		{
			var port = sailor.Scene.GetAllComponents<VoyagePort>().Single( x => stage == 0 ? x.ProducesGoods : x.PortName == "Palm Island" );
			sailor.Ship.WorldPosition = port.WorldPosition + new Vector3( 0, Networking.IsHost ? -300 : 300, 10 );
			sailor.Ship.Body.Velocity = Vector3.Zero;
			sailor.Ship.Body.AngularVelocity = Vector3.Zero;
			sailor.Ship.Transform.ClearInterpolation();
			sailor.ReturnToDeck();
		}
		if ( stage == 1 ) voyage.RequestAction( 1 );
		if ( stage == 3 ) voyage.RequestAction( 2 );
		if ( stage == 4 ) voyage.RequestAction( 3 );
		var cannon = sailor.Scene.GetAllComponents<ShipCannon>().FirstOrDefault( x => x.Ship == sailor.Ship );
		if ( stage == 5 && cannon.IsValid() )
		{
			controller.WorldPosition = cannon.SeatPosition.WorldPosition + Vector3.Up * 5;
			controller.Body.Velocity = sailor.Ship.Body.GetVelocityAtPoint( controller.WorldPosition );
		}
		if ( stage == 6 && cannon.IsValid() ) cannon.TryTake( controller );
		if ( stage == 7 && cannon.IsValid() ) cannon.Fire( controller );
		if ( stage is 8 or 9 ) sailor.CurrentStation?.Release( controller );
		if ( stage == 9 )
		{
			sailor.Enabled = true;
			controller.UseInputControls = true;
			controller.UseLookControls = true;
		}
	}

	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
		Log.Info( $"VOYAGE NETWORK TEST OK: {message}" );
	}
}
