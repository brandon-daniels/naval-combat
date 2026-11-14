using System;
using System.Threading.Tasks;

namespace NavalCombat;

public static class ShipPlankSmokeTest
{
	[ConCmd( "naval_test_ship_planks" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || Networking.IsActive ) return;
		var sailor = Game.ActiveScene?.GetAllComponents<ShipPlayer>().FirstOrDefault( x => !x.IsNpc );
		if ( !sailor.IsValid() || sailor.Scene.IsEditor ) return;
		GameObject fixture = null;
		GameObject ram = null;
		try
		{
			fixture = sailor.Ship.GameObject.Clone( new Vector3( 0, -7000, 500 ), Rotation.Identity );
			fixture.NetworkMode = NetworkMode.Never;
			fixture.Enabled = true;
			var ship = fixture.GetComponent<ArcadeShip>();
			ship.Ocean = null;
			ship.Body.MotionEnabled = false;
			var health = fixture.GetComponent<ShipHealth>();
			health.ResetHealth();
			await GameTask.DelaySeconds( 0.5f );
			var structure = fixture.GetComponent<ShipStructure>();
			var model = ship.WalkableModelObject;
			var outward = model.WorldRotation * Vector3.Forward;
			var mouth = model.WorldTransform.PointToWorld( new Vector3( 3.0f, -1, 1.9f ) );
			SceneTraceResult Trace() => ship.Scene.Trace.Ray( mouth + outward * 35, mouth - outward * 35 ).WithoutTags( ShipDebris.Tag ).Run();
			Check( Trace().Hit, "Intact hull blocks an exterior-to-interior trace" );
			var ball = new GameObject( ship.Scene, true, "Plank test cannonball" );
			ball.NetworkMode = NetworkMode.Never;
			ball.WorldPosition = mouth + outward * 110 + Vector3.Up * 3;
			var projectile = ball.AddComponent<Cannonball>();
			projectile.Source = sailor.Ship;
			projectile.Velocity = -outward * 1200;
			await GameTask.DelaySeconds( 0.4f );
			Check( health.HoleCount >= 1 && health.BrokenParts.Any( x => structure.Parts[x.Index].IsPlank ), "Swept cannonball removes actual hull planks and creates a leak" );
			Check( !Trace().Hit, "The opening has no remaining hull collision" );
			var debris = ship.Scene.GetAllComponents<ShipDebris>().First( x => x.Owner == fixture && x.GameObject.Name == "Detached hull plank" );
			var start = debris.WorldPosition;
			await GameTask.DelaySeconds( 0.2f );
			Log.Info( $"PLANK PHYSICS: dynamic={debris.GetComponent<ModelCollider>().IsDynamic} moved={debris.WorldPosition.Distance( start )} velocity={debris.GetComponent<Rigidbody>().Velocity} mass={debris.GetComponent<Rigidbody>().Mass} override={debris.GetComponent<Rigidbody>().MassOverride} object={debris.GetComponent<Rigidbody>().GameObject.Name} parent={debris.GameObject.Parent?.Name}" );
			Check( debris.GetComponent<ModelCollider>().IsDynamic && debris.WorldPosition.Distance( start ) > 2, "Detached planks move under real physics" );
			int sequence = health.Breaches[0].Sequence;
			Check( health.PatchBreach( sequence ), "The breach can be patched" );
			await GameTask.DelaySeconds( 0.1f );
			Check( Trace().Hit && !health.BrokenParts.Any( x => x.BreachSequence == sequence ), "Patching restores both planking and collision" );
			ram = new GameObject( ship.Scene, false, "Collision test ram" );
			ram.NetworkMode = NetworkMode.Never;
			ram.WorldPosition = mouth + outward * 100;
			ram.AddComponent<BoxCollider>().Scale = new Vector3( 35, 35, 35 );
			var body = ram.AddComponent<Rigidbody>();
			body.MassOverride = 2000;
			body.Gravity = false;
			body.EnhancedCcd = true;
			ram.Enabled = true;
			body.Velocity = -outward * 500;
			await GameTask.DelaySeconds( 0.5f );
			Check( health.HoleCount > 0 && health.BrokenParts.Any(), "A real physics collision blasts planks off" );
			ram.Destroy();
			int count = health.BrokenParts.Count;
			structure.Destroy();
			await GameTask.DelaySeconds( 0.1f );
			structure = fixture.AddComponent<ShipStructure>();
			structure.Configure( ship );
			await GameTask.DelaySeconds( 0.1f );
			Check( structure.VisiblePartCount == ShipStructureCatalog.Parts.Length - count && structure.DetachedPartCount == 0, "Retained damage reconstructs without replaying debris" );
			health.ResetHealth();
			await GameTask.DelaySeconds( 0.1f );
			Check( Trace().Hit, "Reset restores hull collision" );
			Log.Info( "SHIP PLANK TEST PASSED" );
		}
		catch ( Exception e ) { Log.Error( $"SHIP PLANK TEST FAILED: {e}" ); }
		finally
		{
			if ( ram.IsValid() ) ram.Destroy();
			if ( fixture.IsValid() ) fixture.Destroy();
		}
	}

	private static void Check( bool success, string message )
	{
		if ( !success ) throw new InvalidOperationException( message );
		Log.Info( $"SHIP PLANK TEST OK: {message}" );
	}
}

