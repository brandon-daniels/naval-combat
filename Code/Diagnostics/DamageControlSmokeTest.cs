using System;
using System.Threading.Tasks;

namespace NavalCombat;

/// <summary>Exercises the sailor's timed repair and bucket actions on a disposable flooded ship.</summary>
public static class DamageControlSmokeTest
{
	private static bool running;

	[ConCmd( "naval_test_damage_control" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || Networking.IsActive || running ) return;
		var original = Game.ActiveScene?.GetAllComponents<ShipPlayer>().FirstOrDefault( x => !x.IsNpc );
		if ( !original.IsValid() || original.Scene.IsEditor ) return;
		running = true;
		GameObject fixture = null;
		GameObject crewFixture = null;
		GameObject tideFixture = null;
		try
		{
			fixture = original.Ship.GameObject.Clone( new Vector3( 0, -16000, 500 ), Rotation.Identity );
			fixture.Name = "Damage control test ship";
			fixture.NetworkMode = NetworkMode.Never;
			fixture.Enabled = true;
			var ship = fixture.GetComponent<ArcadeShip>();
			ship.Ocean = null;
			ship.Body.Enabled = true;
			ship.Body.MotionEnabled = false;
			var health = fixture.GetComponent<ShipHealth>();
			Check( health.IsValid(), "Temporary ship has hull health" );
			health.ResetHealth();
			await GameTask.DelaySeconds( 0.3f );
			Check( ship.WalkableModelReady, "Temporary ship has authored hull collision" );
			var start = ship.WorldTransform.PointToWorld( new Vector3( -80, 300, 20 ) );
			var end = ship.WorldTransform.PointToWorld( new Vector3( -80, 0, 20 ) );
			var hit = ship.Scene.Trace.Ray( start, end ).WithoutTags( ShipDebris.Tag ).Run();
			Check( hit.Hit && (hit.GameObject == fixture || fixture.IsDescendant( hit.GameObject )), "Repair fixture hits the physical hull" );
			Check( health.ApplyCannonHit( hit.HitPosition, hit.Normal, (end - start).Normal * 1200 ) && health.HoleCount == 1,
				"Hull impact creates one repairable breach" );
			var breach = health.Breaches[0];
			var anchor = health.GetRepairWorldPosition( breach );
			var modelTransform = ship.WalkableModelObject.WorldTransform;
			var repairPoint = anchor + ship.WorldRotation * breach.RepairNormal * 35;
			var repairModel = modelTransform.PointToLocal( repairPoint ).WithZ( 1.0f );
			repairPoint = modelTransform.PointToWorld( repairModel );
			Check( health.IsInsideHold( anchor ) && health.IsInsideHold( repairPoint ) && repairPoint.Distance( anchor ) < 90,
				"Repair marker is reachable from standing inside the hold" );

			crewFixture = new GameObject( original.Scene, true, "Damage control test crew" );
			crewFixture.NetworkMode = NetworkMode.Never;
			var spawn = new GameObject( crewFixture, true, "Damage control crew spawn" );
			spawn.WorldPosition = repairPoint;
			// NPC construction leaves the real player's camera and input untouched. No AI is attached.
			var sailor = NavalPrototype.CreateSailor( crewFixture, ship, spawn, true );
			sailor.IsNpc = false;
			var player = sailor.Controller;
			var fighter = sailor.GameObject.AddComponent<SwordFighter>();
			fighter.Controller = player;
			player.UseInputControls = false;
			player.UseLookControls = false;
			player.UseCameraControls = false;
			await GameTask.DelaySeconds( 0.1f );
			Check( player.Body.IsValid(), "Disposable sailor has a controller body" );
			player.Body.MotionEnabled = false;
			Place( sailor, repairPoint );
			Check( sailor.NearbyBreachSequence == breach.Sequence, "Sailor finds the interior repair marker" );

			sailor.SetRepairing( breach.Sequence, true );
			Check( fighter.EquippedItem == HeldItem.Sword && !sailor.IsRepairing, "The default sword cannot patch a reachable breach" );
			fighter.TryEquip( HeldItem.Planks );
			sailor.SetRepairing( breach.Sequence, true );
			Check( sailor.IsRepairing, "Equipped planks start the patch" );
			fighter.TryEquip( HeldItem.Bucket );
			Check( !sailor.IsRepairing && health.HoleCount == 1, "Stowing the planks cancels the patch without closing the breach" );
			sailor.SetRepairing( breach.Sequence, true );
			Check( !sailor.IsRepairing, "The bucket cannot patch a breach" );
			fighter.TryEquip( HeldItem.Planks );
			sailor.SetRepairing( breach.Sequence, true );
			Check( sailor.IsRepairing && MathF.Abs( sailor.RepairFinishesAt - sailor.RepairStartedAt - health.RepairSecondsFor( breach ) ) < 0.01f,
				"Repair starts with the full tier-dependent host timer" );
			await GameTask.DelaySeconds( 0.2f );
			Check( sailor.RepairProgress > 0 && health.HoleCount == 1, "Partial repair does not remove a breach" );
			sailor.SetRepairing( breach.Sequence, false );
			Check( !sailor.IsRepairing && sailor.RepairProgress == 0 && health.HoleCount == 1, "Releasing repair cancels unfinished progress" );
			sailor.SetRepairing( breach.Sequence, true );
			await GameTask.DelaySeconds( 0.8f );
			Check( !sailor.IsRepairing && health.HoleCount == 1, "Missing held-input renewals expire the repair lease" );
			var farPoint = modelTransform.PointToWorld( new Vector3( 0, 4.4f, 1 ) );
			Place( sailor, farPoint );
			Check( health.IsInsideHold( farPoint ) && farPoint.Distance( anchor ) > 110, "Range rejection uses a distant position within the same hold" );
			sailor.SetRepairing( breach.Sequence, true );
			Check( !sailor.IsRepairing, "Repairing a distant breach is rejected" );
			Place( sailor, repairPoint );
			sailor.SetRepairing( breach.Sequence, true );
			await GameTask.DelaySeconds( 0.15f );
			Check( health.ApplyCannonHit( hit.HitPosition, hit.Normal, (end - start).Normal * 1200 ), "A second cannon hit strikes the active repair" );
			await GameTask.DelaySeconds( 0.1f );
			Check( !sailor.IsRepairing && health.TryGetBreach( breach.Sequence, out var enlarged ) && enlarged.Tier == 3,
				"New damage cancels the repair and enlarges the same breach" );

			tideFixture = new GameObject( original.Scene, false, "Damage control test tide" );
			tideFixture.NetworkMode = NetworkMode.Never;
			var tide = tideFixture.AddComponent<ArcadeOcean>();
			tide.WaveHeight = 0;
			tide.SeaLevel = ship.WorldPosition.z + 100;
			ship.Ocean = tide;
			health.LeakRatePerTier = 80;
			for ( int i = 0; i < 50 && health.FloodVolume < 175; i++ ) await GameTask.DelaySeconds( 0.05f );
			tide.SeaLevel = ship.WorldPosition.z - 200;
			await GameTask.DelaySeconds( 0.1f );
			float water = health.FloodVolume;
			Check( water >= 175 && !health.IsDefeated && health.LeakRate == 0, "Controlled tide fills the hold, then stops adding water" );
			float required = health.RepairSecondsFor( health.Breaches[0] );
			float began = Time.Now;
			for ( int i = 0; i < 100 && health.HoleCount > 0; i++ )
			{
				sailor.SetRepairing( breach.Sequence, true );
				await GameTask.DelaySeconds( 0.1f );
			}
			Check( health.HoleCount == 0 && !sailor.IsRepairing && Time.Now - began >= required,
				"An uninterrupted full-duration repair removes the selected breach" );
			Check( MathF.Abs( health.FloodVolume - water ) < 0.01f, "Repairing planks leaves the collected flood water in the hold" );

			Place( sailor, modelTransform.PointToWorld( new Vector3( 0, -3, 1 ) ) );
			sailor.UseBucket();
			Check( sailor.WaterInReach && !sailor.CanScoop && !sailor.IsUsingBucket, "Planks cannot scoop reachable water" );
			fighter.TryEquip( HeldItem.Bucket );
			Check( sailor.CanScoop && !sailor.BucketFull && !sailor.CanThrowOverboard, "Sailor reaches flood water inside the hold" );
			sailor.UseBucket();
			Check( sailor.IsUsingBucket, "Equipped bucket starts the interrupted scoop" );
			fighter.TryEquip( HeldItem.Sword );
			Check( !sailor.IsUsingBucket && !sailor.BucketFull && health.FloodVolume == water, "Stowing the bucket cancels a scoop without transferring water" );
			fighter.TryEquip( HeldItem.Bucket );
			float scoopStarted = Time.Now;
			sailor.UseBucket();
			Check( sailor.IsUsingBucket && sailor.BucketWater == 0 && health.FloodVolume == water, "Starting a scoop does not transfer water early" );
			sailor.UseBucket();
			for ( int i = 0; i < 40 && sailor.IsUsingBucket; i++ ) await GameTask.DelaySeconds( 0.1f );
			Check( sailor.BucketWater > 0 && sailor.BucketWater <= 100 && Time.Now - scoopStarted >= Math.Clamp( sailor.BucketSeconds, 0.3f, 3 ),
				"Timed scoop moves at most one hundred units into the bucket" );
			Check( MathF.Abs( water - health.FloodVolume - sailor.BucketWater ) < 0.01f, "Scoop conserves water between hold and bucket" );
			float held = sailor.BucketWater;
			float remaining = health.FloodVolume;
			sailor.UseBucket();
			await GameTask.DelaySeconds( sailor.BucketSeconds + 0.1f );
			Check( !sailor.IsUsingBucket && sailor.BucketWater == held && health.FloodVolume == remaining,
				"A full bucket cannot scoop twice or throw water through the hold ceiling" );

			Place( sailor, modelTransform.PointToWorld( new Vector3( 1.6f, -3, 3.5f ) ) );
			var outward = ship.WalkableModelObject.WorldRotation * Vector3.Forward;
			player.EyeAngles = new Angles( 0, MathF.Atan2( outward.y, outward.x ) * 180 / MathF.PI, 0 );
			await GameTask.DelaySeconds( 0.1f );
			Check( sailor.CanThrowOverboard, "Deck sailor has a clear outward throw beyond the rail" );
			sailor.UseBucket();
			Check( sailor.IsUsingBucket, "Equipped bucket starts the interrupted throw" );
			fighter.TryEquip( HeldItem.Planks );
			sailor.UseBucket();
			Check( !sailor.CanThrowOverboard && !sailor.IsUsingBucket && sailor.BucketWater == held && health.FloodVolume == remaining,
				"A stowed bucket keeps its water and cannot finish or start a throw" );
			fighter.TryEquip( HeldItem.Bucket );
			float throwStarted = Time.Now;
			sailor.UseBucket();
			Check( sailor.IsUsingBucket && sailor.BucketWater == held, "Starting a throw retains water until its timer completes" );
			for ( int i = 0; i < 40 && sailor.IsUsingBucket; i++ ) await GameTask.DelaySeconds( 0.1f );
			Check( !sailor.BucketFull && Time.Now - throwStarted >= Math.Clamp( sailor.BucketSeconds, 0.3f, 3 ) && health.FloodVolume == remaining,
				"Legal overboard throw empties only the bucket after its full timer" );
			Log.Info( "DAMAGE CONTROL TEST PASSED: equipped tool requirements, interruption on switch, interior repair reach, timed holds and water conservation through scoop, stow and throw." );
		}
		catch ( Exception e ) { Log.Error( $"DAMAGE CONTROL TEST FAILED: {e.Message}" ); }
		finally
		{
			if ( crewFixture.IsValid() ) crewFixture.Destroy();
			if ( fixture.IsValid() ) fixture.Destroy();
			if ( tideFixture.IsValid() ) tideFixture.Destroy();
			running = false;
		}
	}

	private static void Place( ShipPlayer sailor, Vector3 position )
	{
		sailor.WorldPosition = position;
		sailor.Controller.WishVelocity = Vector3.Zero;
		sailor.Controller.Body.Velocity = Vector3.Zero;
	}

	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
		Log.Info( $"DAMAGE CONTROL TEST OK: {message}" );
	}
}
