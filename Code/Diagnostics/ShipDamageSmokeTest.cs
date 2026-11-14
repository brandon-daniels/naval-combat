using System;
using System.Threading.Tasks;

namespace NavalCombat;

/// <summary>Editor-only integration checks using real swept cannonballs and temporary ship fixtures.</summary>
public static class ShipDamageSmokeTest
{
	private static bool running;

	[ConCmd( "naval_test_ship_damage" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || Networking.IsActive || running ) return;
		var sailor = Game.ActiveScene?.GetAllComponents<ShipPlayer>().FirstOrDefault( x => !x.IsNpc );
		if ( !sailor.IsValid() || sailor.Scene.IsEditor ) return;
		running = true;
		GameObject fixture = null;
		GameObject contactFixture = null;
		GameObject tideFixture = null;
		try
		{
			fixture = sailor.Ship.GameObject.Clone( new Vector3( 0, -6000, 500 ), Rotation.Identity );
			fixture.Name = "Damage test fixture";
			fixture.NetworkMode = NetworkMode.Never;
			fixture.Enabled = true;
			var target = fixture.GetComponent<ArcadeShip>();
			var ocean = target.Ocean;
			target.Ocean = null;
			target.Body.Enabled = false;
			var health = fixture.GetComponent<ShipHealth>();
			Check( health.IsValid(), "Authored ship includes hull health" );
			health.ResetHealth();
			await GameTask.DelaySeconds( 0.3f );
			Check( health.HoleCount == 0 && !health.IsDefeated, "New hull starts intact" );
			// Verify an actual dynamic shape contacts a solid surface, not just a moving render effect.
			contactFixture = new GameObject( sailor.Scene, true, "Debris contact fixture" );
			contactFixture.NetworkMode = NetworkMode.Never;
			contactFixture.WorldPosition = target.WorldPosition + new Vector3( 1000, 0, 100 );
			contactFixture.AddComponent<BoxCollider>().Scale = new Vector3( 150, 150, 10 );
			var sample = ShipDebris.Spawn( fixture, null, contactFixture.WorldPosition + Vector3.Up * 40,
				Rotation.Identity, new Vector3( 18, 4, 4 ), Vector3.Down * 20, Vector3.Zero, 2, Color.White );
			await GameTask.DelaySeconds( 0.1f );
			Check( sample.IsValid() && sample.GetComponent<BoxCollider>().IsDynamic, "Splinter owns a dynamic physics shape" );
			await GameTask.DelaySeconds( 0.6f );
			float clearance = sample.WorldPosition.z - contactFixture.WorldPosition.z;
			Check( clearance > 5 && clearance < 15, "Falling splinter lands on solid collision" );
			sample.Lifetime = 0.8f;
			await GameTask.DelaySeconds( 0.2f );
			Check( !sample.IsValid(), "Temporary physics debris expires" );
			for ( int i = 0; i < ShipHealth.MaximumBreaches; i++ )
			{
				await FireAtHull( sailor.Ship, target, i );
				Check( health.HoleCount == i + 1, $"Shot {i + 1} makes exactly one persistent hole" );
				Check( !health.IsDefeated && health.FloodVolume == 0, $"Dry holes do not sink the ship (hit {i + 1})" );
				await GameTask.DelaySeconds( 0.1f );
				var impactVisuals = fixture.GetComponent<ShipDamageVisuals>();
				Check( impactVisuals.ImpactBursts == i + 1, $"Shot {i + 1} emits one burst without replaying previous hits" );
				Check( impactVisuals.ActiveDebrisCount > 0 && impactVisuals.ActiveDebrisCount <= impactVisuals.DebrisLimit, "Physics debris stays within its budget" );
				Check( impactVisuals.ActiveLeakCount == 0, "Dry hull hits do not gush water" );
			}
			await GameTask.DelaySeconds( 0.1f );
			Check( fixture.GetComponent<ShipDamageVisuals>()?.VisibleHoleCount == 10, "All ten breach effects exist" );
			Check( health.Breaches.All( x => x.Normal.Length > 0.9f && x.InteriorNormal.Length > 0.9f ), "Outer and inner breach surfaces have valid normals" );
			Check( health.Breaches.All( x => health.IsInsideHold( health.GetRepairWorldPosition( x ) ) && x.RepairNormal.Length > 0.9f ),
				"Every retained breach has a repair marker inside the accessible hold" );
			Check( health.Breaches.All( x => x.ImpactVelocity.Length > 1000 && x.Radius >= 18 ), "Impact momentum and opening size retained for every peer" );
			var presentation = fixture.GetComponent<ShipDamageVisuals>();
			var first = health.Breaches[0];
			Check( first.Tier == 2 && health.RepairSecondsFor( first ) == 4, "Cannon hits open a tier-two breach with a four-second repair" );
			await FireAtHull( sailor.Ship, target, 0 );
			await GameTask.DelaySeconds( 0.1f );
			Check( health.HoleCount == 10 && health.TryGetBreach( first.Sequence, out var enlarged ) && enlarged.Tier == 3
				&& enlarged.ImpactRevision > first.ImpactRevision && enlarged.Radius > first.Radius, "Repeated hit enlarges one stable breach to tier three" );
			Check( presentation.ImpactBursts == 11 && health.RepairSecondsFor( health.Breaches[0] ) == 6,
				"Widening emits one new impact burst and requires six seconds to patch" );
			// A disabled ocean supplies a controlled water height without rendering another sea.
			tideFixture = new GameObject( sailor.Scene, false, "Damage test tide" );
			tideFixture.NetworkMode = NetworkMode.Never;
			var tide = tideFixture.AddComponent<ArcadeOcean>();
			tide.WaveHeight = 0;
			tide.SeaLevel = target.WorldPosition.z + 100;
			target.Ocean = tide;
			await GameTask.DelaySeconds( 0.6f );
			Check( presentation.ActiveLeakCount == 10 && health.FloodVolume > 0 && health.LeakRate > 120, "Submerged breaches admit accumulated water according to tier" );
			tide.SeaLevel = target.WorldPosition.z - 100;
			await GameTask.DelaySeconds( 0.8f );
			float retainedWater = health.FloodVolume;
			await GameTask.DelaySeconds( 0.2f );
			Check( presentation.ActiveLeakCount == 0 && health.LeakRate == 0 && health.FloodVolume == retainedWater,
				"Leaks stop above the waterline while collected water remains" );
			Check( health.PatchBreach( first.Sequence ) && !health.TryGetBreach( first.Sequence, out _ )
				&& health.HoleCount == 9 && health.FloodVolume == retainedWater, "Patching removes the selected breach without draining the hold" );
			Check( !health.PatchBreach( first.Sequence ), "A patched breach cannot be repaired twice" );
			float removed = health.BailWater( 25 );
			Check( removed == 25 && MathF.Abs( health.FloodVolume - (retainedWater - 25) ) < 0.01f,
				"Bailing removes only the requested volume without repairing other holes" );
			Check( health.BailWater( float.NaN ) == 0 && health.BailWater( -1 ) == 0, "Invalid bucket amounts are rejected" );
			var floating = ShipDebris.Spawn( fixture, tide, target.WorldPosition + new Vector3( 1000, 300, -120 ),
				Rotation.Identity, new Vector3( 18, 4, 4 ), Vector3.Down * 20, Vector3.One * 8, 3, Color.White );
			await GameTask.DelaySeconds( 1 );
			Check( floating.IsValid() && MathF.Abs( floating.WorldPosition.z - tide.SeaLevel ) < 12,
				"Wood debris rises and floats at the ocean surface" );
			floating.GameObject.Destroy();
			presentation.Enabled = false;
			await GameTask.DelaySeconds( 0.1f );
			int bursts = presentation.ImpactBursts;
			presentation.Enabled = true;
			await GameTask.DelaySeconds( 0.1f );
			Check( presentation.VisibleHoleCount == 9 && presentation.ImpactBursts == bursts && presentation.ActiveDebrisCount == 0,
				"Reconstructing retained damage restores holes without replaying bursts" );
			tide.SeaLevel = target.WorldPosition.z + 100;
			for ( int tick = 0; tick < 140 && !health.IsDefeated; tick++ ) await GameTask.DelaySeconds( 0.1f );
			Check( health.IsDefeated && health.FloodFraction == 1 && health.HoleCount == 9,
				"A full hold sinks the ship regardless of hole count" );
			Check( !health.ApplyCannonHit( target.WorldPosition, Vector3.Left ), "Defeated hull rejects further holes" );
			Check( !health.Repair( 1000 ), "Generic repair cannot revive a sinking hull" );
			Check( health.BailWater( 1000 ) == 0 && !health.PatchBreach( health.Breaches[0].Sequence ), "Damage control cannot revive a defeated hull" );
			Check( !target.Helm.CanEnter( sailor.Controller ), "Sinking ship rejects station entry" );
			target.Ocean = ocean;
			target.Body.MotionEnabled = true;
			target.Body.Enabled = true;
			float height = target.WorldPosition.z;
			await GameTask.DelaySeconds( 3 );
			Check( target.WorldPosition.z < height - 150, "Defeated hull physically sinks" );
			health.ResetHealth();
			await GameTask.DelaySeconds( 0.1f );
			Check( health.HoleCount == 0 && health.FloodVolume == 0 && !health.IsDefeated && presentation.VisibleHoleCount == 0 && presentation.ActiveDebrisCount == 0, "Reset clears damage, flood water and effects" );
			target.Body.Enabled = false;
			target.Ocean = null;
			foreach ( float modelY in new[] { 7.5f, -8.0f } )
			{
				var ballObject = new GameObject( sailor.Scene, true, "Outer compartment test cannonball" );
				ballObject.NetworkMode = NetworkMode.Never;
				ballObject.WorldPosition = target.WalkableModelObject.WorldTransform.PointToWorld( new Vector3( 5, modelY, 1.8f ) );
				var ball = ballObject.AddComponent<Cannonball>();
				ball.Source = sailor.Ship;
				ball.Velocity = target.WalkableModelObject.WorldRotation * new Vector3( -1200, 0, 0 );
				int before = health.HoleCount;
				for ( int tick = 0; tick < 30 && ballObject.IsValid(); tick++ ) await GameTask.DelaySeconds( 0.05f );
				Check( !ballObject.IsValid() && health.HoleCount == before + 1, "Cannon damage can reach the outer bow/stern compartments" );
				var breach = health.Breaches[health.HoleCount - 1];
				var repairWorld = health.GetRepairWorldPosition( breach );
				var repairModel = target.WalkableModelObject.WorldTransform.PointToLocal( repairWorld );
				var standing = target.WalkableModelObject.WorldTransform.PointToWorld( new Vector3( repairModel.x,
					repairModel.y + (modelY > 0 ? -0.5f : 0.5f), 0.94f ) );
				Check( health.IsInsideHold( repairWorld ) && health.IsInsideHold( standing ) && (standing - repairWorld).Length < 90
					&& repairModel.z >= 1.44f && repairModel.z <= 2.11f && MathF.Abs( repairModel.y - modelY ) > 0.5f,
					"Bow/stern damage maps to a reachable bulkhead repair marker" );
				Check( health.PatchBreach( breach.Sequence ), "Mapped compartment breach can be patched" );
			}
			health.ResetHealth();
			Log.Info( "SHIP DAMAGE TEST PASSED: swept impacts, physical splinter collision/expiry/budgets, tiered repeated damage, dry/submerged holes, patch/bail persistence, reconstruction, full-hold sinking and reset." );
		}
		catch ( Exception e ) { Log.Error( $"SHIP DAMAGE TEST FAILED: {e.Message}" ); }
		finally
		{
			if ( fixture.IsValid() ) fixture.Destroy();
			if ( contactFixture.IsValid() ) contactFixture.Destroy();
			if ( tideFixture.IsValid() ) tideFixture.Destroy();
			running = false;
		}
	}

	[ConCmd( "naval_damage_preview" )]
	public static void Preview( int holes = 3 )
	{
		if ( !Game.IsEditor || Networking.IsActive ) return;
		var sailor = Game.ActiveScene?.GetAllComponents<ShipPlayer>().FirstOrDefault( x => !x.IsNpc );
		if ( !sailor.IsValid() || sailor.Scene.IsEditor ) return;
		foreach ( var npc in sailor.Scene.GetAllComponents<ShipNpc>() ) npc.Enabled = false;
		var ship = sailor.Ship;
		var health = ship.GetComponent<ShipHealth>();
		if ( !health.IsValid() ) return;
		health.ResetHealth();
		for ( int i = 0; i < Math.Clamp( holes, 0, ShipHealth.MaximumBreaches ); i++ )
		{
			float side = i < 5 ? -1 : 1;
			var start = ship.WorldTransform.PointToWorld( new Vector3( -160 + i % 5 * 80, side * 300, 0 ) );
			var end = ship.WorldTransform.PointToWorld( new Vector3( -160 + i % 5 * 80, 0, 0 ) );
			var hit = sailor.Scene.Trace.Ray( start, end ).Run();
			if ( hit.Hit && (hit.GameObject == ship.GameObject || ship.GameObject.IsDescendant( hit.GameObject )) )
					health.ApplyCannonHit( hit.HitPosition, hit.Normal, (end - start).Normal * 1200 );
		}
		Log.Info( $"DAMAGE PREVIEW: {health.HoleCount} breaches, flooded={health.FloodPercent}%, sinking={health.IsDefeated}; ship={ship.WorldPosition}. Restart Play for a fresh voyage." );
	}

	internal static async Task FireAtHull( ArcadeShip source, ArcadeShip target, int slot, bool networked = false )
	{
		float side = slot < 5 ? 1 : -1;
		var ballObject = new GameObject( source.Scene, true, "Damage test cannonball" );
		ballObject.NetworkMode = networked ? NetworkMode.Object : NetworkMode.Never;
		ballObject.WorldPosition = target.WorldTransform.PointToWorld( new Vector3( -160 + slot % 5 * 80, side * 300, 20 ) );
		var ball = ballObject.AddComponent<Cannonball>();
		ball.Source = source;
		ball.Velocity = target.WorldRotation * new Vector3( 0, -side * 1200, 0 );
		if ( networked ) ballObject.NetworkSpawn( null );
		for ( int tick = 0; tick < 30 && ballObject.IsValid(); tick++ ) await GameTask.DelaySeconds( 0.05f );
		Check( !ballObject.IsValid(), $"Shot at hull slot {slot + 1} terminates on collision" );
	}

	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
		Log.Info( $"SHIP DAMAGE TEST OK: {message}" );
	}
}
