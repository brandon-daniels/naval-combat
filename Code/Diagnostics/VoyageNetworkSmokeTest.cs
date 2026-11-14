using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NavalCombat;

/// <summary>Exercises real owner RPCs from two instances. Restart Play after this destructive test voyage.</summary>
public static class VoyageNetworkSmokeTest
{
	private static bool running;
	private static ShipPlayer preparedSailor;
	private static bool savedInput;
	private static bool savedLook;
	private static bool savedEnabled;
	private static readonly HashSet<string> receipts = new();
	private static readonly Dictionary<ShipCannon, int> cannonSequences = new();

	[ConCmd( "naval_test_cannons_network" )]
	public static async Task RunCannons()
	{
		if ( !Game.IsEditor || !Networking.IsActive || !Networking.IsHost || running ) return;
		var voyages = Game.ActiveScene.GetAllComponents<PlayerVoyage>().ToArray();
		if ( voyages.Length != 2 ) { Log.Warning( "Cannon network test needs exactly two active players." ); return; }
		running = true;
		receipts.Clear();
		try
		{
			Stage( 0 );
			await GameTask.DelaySeconds( 1 );
			await ExerciseCannons( voyages );
			Log.Info( "CANNON NETWORK TEST PASSED: two owners loaded, interrupted, fired and released; forged ownership requests rejected and shot effects observed on both peers." );
		}
		catch ( Exception e ) { Log.Error( $"CANNON NETWORK TEST FAILED: {e.Message}" ); }
		finally
		{
			Stage( 9 );
			running = false;
			receipts.Clear();
		}
	}

	[ConCmd( "naval_test_voyage_network" )]
	public static async Task Run( bool includeCannons = false )
	{
		if ( !Game.IsEditor || !Networking.IsActive || !Networking.IsHost || running ) return;
		var voyages = Game.ActiveScene.GetAllComponents<PlayerVoyage>().ToArray();
		if ( voyages.Length != 2 ) { Log.Warning( "Network voyage test needs exactly two active players." ); return; }
		if ( voyages.Any( x => x.Wallet.Balance != 500 || x.Cargo.UsedCapacity != 0 || x.Production.IsProducing )
			|| Game.ActiveScene.GetAllComponents<GoodsBarrel>().Any() )
		{
			Log.Warning( "Network voyage test needs fresh wallets and empty holds. Restart Play first." );
			return;
		}
		if ( includeCannons && voyages.Any( x => !x.Scene.GetAllComponents<ShipCannon>().Any( c => c.Ship == x.Sailor.Ship && c.Enabled ) ) )
		{
			Log.Warning( "Cannon checks require an enabled cannon on each player's ship. Run naval_test_voyage_network false for cargo only." );
			return;
		}
		var producer = voyages[0].Scene.GetAllComponents<VoyagePort>().Single( x => x.ProducesGoods );
		var listing = producer.Port.Market.Listings.Single( x => x.Goods == producer.Goods );
		var interactionRadius = producer.Port.InteractionRadius;
		var salePrice = listing.SellPrice;
		var durations = voyages.Select( x => x.Production.ProductionSeconds ).ToArray();
		receipts.Clear();
		running = true;
		try
		{
			NavalNetworkSmokeTest.Run();
			// Keep networked rigidbodies under their owners' normal simulation. Directly
			// teleporting them from a broadcast diagnostic creates correction impulses.
			producer.Port.InteractionRadius = 5000;
			foreach ( var voyage in voyages ) voyage.Production.ProductionSeconds = 0.5f;
			Stage( 0 );
			await GameTask.DelaySeconds( 2 );
			// A shared crop field runs one batch at a time, even for different owners.
			foreach ( var voyage in voyages )
			{
				Stage( 1, voyage.Identity );
				await WaitFor( () => voyage.IsValid() && voyage.Wallet.Balance == 400
					&& voyage.Scene.GetAllComponents<GoodsBarrel>().Count( b => b.OwnerId == voyage.Identity ) == 1,
					"Owner investment RPC charges 100 and produces one barrel" );
			}
			var barrels = voyages.Select( x => x.Scene.GetAllComponents<GoodsBarrel>().Single( b => b.OwnerId == x.Identity ) ).ToArray();
			Check( barrels.All( x => x.GetComponent<CapsuleCollider>().IsDynamic ), "Both produced barrels have dynamic collision shapes" );
			// These transfer fixtures must stay within pickup range while RPCs travel.
			// Free fall and flotation are exercised separately by naval_test_production.
			foreach ( var barrel in barrels ) barrel.GetComponent<Rigidbody>().MotionEnabled = false;
			Check( barrels.All( x => x.Quantity == 10 && x.IsLoose ) && voyages.All( x => x.Cargo.UsedCapacity == 0 ),
				"Production creates twenty physical goods without loading either hold" );

			// Stage only host-owned barrels, never teleport owner-simulated ships or sailors.
			for ( int i = 0; i < barrels.Length; i++ )
				barrels[i].WorldPosition = voyages[1 - i].Sailor.WorldPosition + Vector3.Up * 40;
			await GameTask.DelaySeconds( 1 );
			Check( barrels.Select( (b, i) => b.WorldPosition.Distance( voyages[1 - i].Sailor.WorldPosition ) <= GoodsBarrel.InteractionDistance ).All( x => x ),
				"Foreign barrels are in pickup range for the ownership denial check" );
			Stage( 10 );
			await WaitFor( () => Acknowledged( 10 ), "Both owners sent foreign-barrel pickup requests" );
			await GameTask.DelaySeconds( 1 );
			Check( barrels.All( x => x.IsLoose ) && voyages.All( x => !x.Sailor.CarriedBarrel.IsValid() && x.Cargo.UsedCapacity == 0 ),
				"Foreign-owner pickup requests cannot take or load another voyage's barrel" );
			for ( int i = 0; i < barrels.Length; i++ )
				barrels[i].WorldPosition = voyages[i].Sailor.WorldPosition + Vector3.Up * 40;
			await GameTask.DelaySeconds( 1 );
			Stage( 2 );
			await WaitFor( () => voyages.All( x => x.Sailor.CarriedBarrel.IsValid()
				&& x.Sailor.CarriedBarrel.OwnerId == x.Identity && x.Sailor.CarriedBarrel.Carrier == x.Sailor ),
				"Both owners pick up their physical barrels through owner RPCs" );
			Stage( 2 );
			await WaitFor( () => voyages.All( x => x.Cargo.UsedCapacity == 10 && !x.Sailor.CarriedBarrel.IsValid() )
				&& barrels.Select( (b, i) => b.SecuredShip == voyages[i].Sailor.Ship ).All( x => x ),
				"Both owner drop RPCs secure exactly ten goods to their own ships" );
			Stage( 11 );
			await WaitFor( () => Acknowledged( 11 ), "Both owners sent repeated secured-barrel requests" );
			await GameTask.DelaySeconds( 0.3f );
			Check( voyages.All( x => x.Cargo.UsedCapacity == 10 && !x.Sailor.CarriedBarrel.IsValid() ) && barrels.All( x => x.SecuredShip.IsValid() ),
				"Repeated interaction with secured barrels cannot duplicate cargo" );
			listing.SellPrice = 35;
			Stage( 3 );
			await WaitFor( () => voyages.All( x => x.Cargo.UsedCapacity == 0 && x.Wallet.Balance == 750 ) && barrels.All( x => !x.IsValid() ),
				"Both sale RPCs remove the delivered barrels and pay 350" );
			await GameTask.DelaySeconds( 0.3f );
			Stage( 12 );
			await WaitFor( () => Acknowledged( 12 ), "Both owners sent repeated sale requests" );
			await GameTask.DelaySeconds( 0.3f );
			Check( voyages.All( x => x.Cargo.UsedCapacity == 0 && x.Wallet.Balance == 750 ), "Repeated sale cannot pay twice" );
			Stage( 4 );
			await WaitFor( () => voyages.All( x => x.Cargo.Capacity == 25 && x.Wallet.Balance == 450 ), "Both cargo upgrade RPCs apply and charge correctly" );
			if ( includeCannons ) await ExerciseCannons( voyages );
			Log.Info( $"VOYAGE NETWORK TEST PASSED: two real owners invested, carried, secured, sold and upgraded; foreign pickup and duplicate transfers rejected. Cannons: {(includeCannons ? "passed" : "not tested")}." );
		}
		catch ( Exception e ) { Log.Error( $"VOYAGE NETWORK TEST FAILED: {e.Message}" ); }
		finally
		{
			for ( int i = 0; i < voyages.Length; i++ ) if ( voyages[i].IsValid() ) voyages[i].Production.ProductionSeconds = durations[i];
			if ( producer.IsValid() && producer.Port.IsValid() ) producer.Port.InteractionRadius = interactionRadius;
			listing.SellPrice = salePrice;
			Stage( 9 );
			running = false;
			receipts.Clear();
		}

	}

	private static async Task ExerciseCannons( PlayerVoyage[] voyages )
	{
		Check( voyages.All( x => x.Scene.GetAllComponents<ShipCannon>().Any( c => c.Ship == x.Sailor.Ship && c.Enabled ) ), "Both owners have enabled cannons" );
		Stage( 5 );
		await GameTask.DelaySeconds( 1 );
		for ( int i = 0; i < 16; i++ )
		{
			Stage( 6 );
			await GameTask.DelaySeconds( 0.25f );
			if ( voyages.All( x => x.Sailor.ActiveCannon.IsValid() ) ) break;
		}
		Check( voyages.All( x => x.Sailor.ActiveCannon.IsValid() ), "Host accepts both owners taking their own cannons" );
		var guns = voyages.Select( x => x.Sailor.ActiveCannon ).ToArray();
		Check( guns.All( x => !x.IsLoaded && !x.IsLoading ), "Both cannons begin empty" );
		var sequences = guns.Select( x => x.ShotSequence ).ToArray();
		Stage( 7 );
		await GameTask.DelaySeconds( 0.35f );
		Check( guns.Select( (x, i) => x.ShotSequence == sequences[i] ).All( x => x ), "Host rejects empty and repeated fire requests" );
		Stage( 15 );
		await WaitFor( () => Acknowledged( 15 ), "Both peers sent forged foreign-player loading RPCs" );
		await GameTask.DelaySeconds( 0.2f );
		Check( guns.All( x => !x.IsLoading && !x.IsLoaded ), "Host rejects loading another owner's cannon even with its real occupant supplied" );
		Stage( 13 );
		await WaitFor( () => guns.All( x => x.IsLoading ), "Both owner loading RPCs begin host-timed loading" );
		Stage( 14 );
		await WaitFor( () => guns.All( x => !x.IsLoading && !x.IsLoaded && x.LoadProgress == 0 ), "Both owner cancellation RPCs discard unfinished loading" );
		if ( guns.All( x => Math.Clamp( x.ReloadSeconds, 0.5f, 10 ) > 1 ) )
		{
			Stage( 13 );
			await WaitFor( () => guns.All( x => x.IsLoading ), "Both loading leases begin" );
			await GameTask.DelaySeconds( 0.8f );
			Check( guns.All( x => !x.IsLoading && !x.IsLoaded ), "Missing hold renewals expire without loading either cannon" );
		}
		float started = Time.Now;
		for ( int i = 0; i < 120 && guns.Any( x => !x.IsLoaded ); i++ )
		{
			Stage( 13 );
			await GameTask.DelaySeconds( 0.1f );
		}
		Check( guns.All( x => x.IsLoaded && !x.IsLoading && Time.Now - started >= Math.Clamp( x.ReloadSeconds, 0.5f, 10 ) ), "Both owner holds complete only after authoritative loading time" );
		for ( int i = 0; i < 30 && !Acknowledged( 17 ); i++ )
		{
			Stage( 17 );
			await GameTask.DelaySeconds( 0.1f );
		}
		Check( Acknowledged( 17 ), "Both peers observe both cannons loaded" );
		Stage( 16 );
		await WaitFor( () => Acknowledged( 16 ), "Both peers sent forged foreign-player fire RPCs" );
		await GameTask.DelaySeconds( 0.2f );
		Check( guns.Select( (x, i) => x.IsLoaded && x.ShotSequence == sequences[i] ).All( x => x ), "Host rejects firing another owner's loaded cannon" );
		Stage( 7 );
		await WaitFor( () => guns.Select( (x, i) => !x.IsLoaded && !x.IsLoading && x.ShotSequence == sequences[i] + 1 ).All( x => x ), "Duplicate owner fire requests consume exactly one charge each" );
		for ( int i = 0; i < 30 && !Acknowledged( 18 ); i++ )
		{
			Stage( 18 );
			await GameTask.DelaySeconds( 0.1f );
		}
		Check( Acknowledged( 18 ), "Both peers receive both shots and muzzle presentation exactly once" );
		await GameTask.DelaySeconds( guns.Max( x => Math.Clamp( x.ReloadSeconds, 0.5f, 10 ) ) + 0.2f );
		Check( guns.All( x => !x.IsLoaded && !x.IsLoading ), "Both cannons remain empty without an explicit reload hold" );
		Stage( 13 );
		await WaitFor( () => guns.All( x => x.IsLoading ), "Both owners begin another load before dismounting" );
		Stage( 8 );
		await WaitFor( () => voyages.All( x => !x.Sailor.CurrentStation.IsValid() ) && guns.All( x => !x.IsLoading && !x.IsLoaded ), "Dismounting releases both stations and cancels their loading" );
	}

	private static bool Acknowledged( int stage ) => Game.ActiveScene.GetAllComponents<PlayerVoyage>().All( x => x.IsValid() && x.Network.Owner is not null
		&& receipts.Contains( $"{stage}:{x.Network.Owner.Id}" ) );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void Stage( int stage, string investor = null )
	{
		// Joined test instances can be standalone clients; only Run requires an editor host.
		if ( !Networking.IsActive ) return;
		if ( stage == 9 )
		{
			if ( preparedSailor.IsValid() )
			{
				preparedSailor.ActiveCannon?.SetLoading( preparedSailor.Controller, false );
				preparedSailor.CurrentStation?.Release( preparedSailor.Controller );
				preparedSailor.Enabled = savedEnabled;
				preparedSailor.Controller.UseInputControls = savedInput;
				preparedSailor.Controller.UseLookControls = savedLook;
			}
			preparedSailor = null;
			cannonSequences.Clear();
			return;
		}
		var voyage = Game.ActiveScene?.GetAllComponents<PlayerVoyage>().FirstOrDefault( x => !x.IsProxy && x.Network.Owner == Connection.Local );
		if ( !voyage.IsValid() ) return;
		var sailor = voyage.Sailor;
		var controller = sailor.Controller;
		if ( stage == 0 )
		{
			cannonSequences.Clear();
			preparedSailor = sailor;
			savedInput = controller.UseInputControls;
			savedLook = controller.UseLookControls;
			savedEnabled = sailor.Enabled;
			controller.UseInputControls = false;
			controller.UseLookControls = false;
			sailor.Enabled = false;
			sailor.Sails.Deployment = 0;
		}
		if ( stage == 1 && voyage.Identity == investor ) voyage.RequestAction( 1 );
		if ( stage is 2 or 10 or 11 )
		{
			var barrel = sailor.Scene.GetAllComponents<GoodsBarrel>().FirstOrDefault( x =>
				stage == 10 ? x.OwnerId != voyage.Identity : x.OwnerId == voyage.Identity );
			if ( barrel.IsValid() ) sailor.RequestBarrelInteraction( barrel );
			else
			{
				Log.Error( $"Network voyage stage {stage}: expected replicated barrel is missing." );
				return;
			}
		}
		if ( stage is 3 or 12 ) voyage.RequestAction( 2 );
		if ( stage is 10 or 11 or 12 ) Acknowledge( stage );
		if ( stage == 4 ) voyage.RequestAction( 3 );
		var cannon = sailor.Scene.GetAllComponents<ShipCannon>().FirstOrDefault( x => x.Ship == sailor.Ship && x.Enabled );
		if ( stage == 5 && cannon.IsValid() )
		{
			controller.WorldPosition = cannon.SeatPosition.WorldPosition + Vector3.Up * 5;
			controller.Body.Velocity = sailor.Ship.Body.GetVelocityAtPoint( controller.WorldPosition );
		}
		if ( stage == 6 && cannon.IsValid() ) cannon.TryTake( controller );
		if ( stage == 7 && cannon.IsValid() )
		{
			cannon.RequestFire( controller );
			cannon.RequestFire( controller );
		}
		if ( stage == 8 ) sailor.CurrentStation?.Release( controller );
		if ( stage == 13 && cannon.IsValid() ) cannon.SetLoading( controller, true );
		if ( stage == 14 && cannon.IsValid() ) cannon.SetLoading( controller, false );
		if ( stage is 15 or 16 )
		{
			var foreign = sailor.Scene.GetAllComponents<PlayerVoyage>().FirstOrDefault( x => x.Network.Owner != Connection.Local );
			if ( !foreign.IsValid() || !foreign.Sailor.ActiveCannon.IsValid() ) return;
			var foreignGun = foreign.Sailor.ActiveCannon;
			if ( stage == 15 ) foreignGun.RequestLoading( foreign.Sailor.Controller, true );
			else foreignGun.RequestFire( foreign.Sailor.Controller );
			Acknowledge( stage );
		}
		if ( stage == 17 )
		{
			var guns = sailor.Scene.GetAllComponents<PlayerVoyage>().Select( x => x.Sailor.ActiveCannon ).ToArray();
			if ( guns.Length != 2 || guns.Any( x => !x.IsValid() || !x.IsLoaded || x.IsLoading ) ) return;
			foreach ( var gun in guns ) cannonSequences[gun] = gun.ShotSequence;
			Acknowledge( stage );
		}
		if ( stage == 18 && cannonSequences.Count == 2 && cannonSequences.All( x => x.Key.IsValid()
			&& !x.Key.IsLoaded && !x.Key.IsLoading && x.Key.ShotSequence == x.Value + 1 && x.Key.PresentedShotSequence == x.Value + 1 ) ) Acknowledge( stage );
	}

	[Rpc.Host]
	private static void Acknowledge( int stage )
	{
		if ( !Game.IsEditor || !running || Rpc.Caller is null || stage is not (10 or 11 or 12 or 15 or 16 or 17 or 18) ) return;
		if ( !Game.ActiveScene.GetAllComponents<PlayerVoyage>().Any( x => x.Network.Owner == Rpc.Caller ) ) return;
		receipts.Add( $"{stage}:{Rpc.Caller.Id}" );
	}

	private static async Task WaitFor( Func<bool> predicate, string message )
	{
		for ( int i = 0; i < 40; i++ )
		{
			if ( predicate() ) { Check( true, message ); return; }
			await GameTask.DelaySeconds( 0.2f );
		}
		Check( false, $"Timed out: {message}" );
	}

	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
		Log.Info( $"VOYAGE NETWORK TEST OK: {message}" );
	}
}
