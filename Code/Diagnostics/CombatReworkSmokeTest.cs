using System;
using System.Threading.Tasks;

namespace NavalCombat;

public static class CombatReworkSmokeTest
{
	[ConCmd( "naval_test_combat_rework" )]
	public static async Task Run()
	{
		var sailor = Game.ActiveScene?.GetAllComponents<ShipPlayer>().FirstOrDefault( x => !x.IsNpc );
		if ( !Game.IsEditor || Networking.IsActive || !sailor.IsValid() ) return;
		GameObject fixture = null;
		GameObject tideObject = null;
		try
		{
			fixture = sailor.Ship.GameObject.Clone( new Vector3( 0, -9000, 500 ), Rotation.Identity );
			fixture.NetworkMode = NetworkMode.Never;
			fixture.Enabled = true;
			var ship = fixture.GetComponent<ArcadeShip>();
			ship.Ocean = null;
			ship.Body.MotionEnabled = false;
			var health = fixture.GetComponent<ShipHealth>();
			health.ResetHealth();
			await GameTask.DelaySeconds( 0.5f );
			var model = ship.WalkableModelObject;
			var outward = model.WorldRotation * Vector3.Forward;
			var mouth = model.WorldTransform.PointToWorld( new Vector3( 3.1f, -1, 1.7f ) );
			var ball = new GameObject( ship.Scene, true, "Penetration test solid shot" );
			ball.NetworkMode = NetworkMode.Never;
			ball.WorldPosition = mouth + outward * 100;
			var projectile = ball.AddComponent<Cannonball>();
			projectile.Source = sailor.Ship;
			projectile.Velocity = -outward * 1800;
			await GameTask.DelaySeconds( 0.18f );
			Check( health.HoleCount > 0, "Swept projectile damages the near hull" );
			Check( ball.IsValid() && projectile.Velocity.Length > 400 && projectile.Velocity.Length < 1800,
				"Solid shot survives timber impact with reduced momentum" );
			await GameTask.DelaySeconds( 0.4f );
			Check( health.HoleCount >= 2, "Shot penetrates the hold and damages the opposite hull" );
			Check( health.BrokenParts.Count >= 2, "Entry and exit leave real missing hull sections" );
			Check( fixture.GetComponent<ShipDamageVisuals>().VisibleHoleCount == health.HoleCount,
				"Retained visible damage matches authoritative openings" );
			Check( health.FloodVolume == 0 && !health.IsDefeated, "Dry damage does not fill or sink a ship" );
			tideObject = new GameObject( ship.Scene, false, "Controlled combat test sea" );
			tideObject.NetworkMode = NetworkMode.Never;
			var tide = tideObject.AddComponent<ArcadeOcean>();
			tide.WaveHeight = 0;
			tide.SeaLevel = ship.WorldPosition.z + 160;
			ship.Ocean = tide;
			health.FloodCapacity = 100;
			health.LeakRatePerTier = 10;
			await GameTask.DelaySeconds( 0.4f );
			float first = health.FloodVolume;
			float level = health.FloodSurfaceLocalHeight;
			Check( first > 0 && first < health.FloodCapacity && !health.IsDefeated, "Submerged openings progressively admit water" );
			await GameTask.DelaySeconds( 0.4f );
			Check( health.FloodVolume > first && health.FloodSurfaceLocalHeight > level
				&& fixture.GetComponent<ShipFloodVisuals>().WaterVisible, "Accumulated water visibly rises in the hold" );
			for ( int i = 0; i < 100 && !health.IsDefeated; i++ ) await GameTask.DelaySeconds( 0.1f );
			Check( health.IsDefeated && ship.IsSinking, "Full flooding triggers sinking" );
			float height = ship.WorldPosition.z;
			ship.Body.MotionEnabled = true;
			await GameTask.DelaySeconds( 2 );
			Check( ship.WorldPosition.z < height - 30, "Flooded hull physically descends" );
			Log.Info( "COMBAT REWORK TEST PASSED" );
		}
		catch ( Exception e ) { Log.Error( $"COMBAT REWORK TEST FAILED: {e}" ); }
		finally
		{
			if ( fixture.IsValid() ) fixture.Destroy();
			if ( tideObject.IsValid() ) tideObject.Destroy();
		}
	}

	private static void Check( bool success, string message )
	{
		if ( !success ) throw new InvalidOperationException( message );
		Log.Info( $"COMBAT REWORK TEST OK: {message}" );
	}
}
