using System;
using System.Threading.Tasks;

namespace NavalCombat;

/// <summary>Real projectiles verify that fittings break and leave physical gaps instead of hull holes.</summary>
public static class ShipStructureSmokeTest
{
	private static bool running;

	[ConCmd( "naval_test_ship_structure" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || Networking.IsActive || running ) return;
		var sailor = Game.ActiveScene?.GetAllComponents<ShipPlayer>().FirstOrDefault( x => !x.IsNpc );
		if ( !sailor.IsValid() || sailor.Scene.IsEditor ) return;
		running = true;
		GameObject fixture = null;
		try
		{
			fixture = sailor.Ship.GameObject.Clone( new Vector3( 0, -6000, 500 ), Rotation.Identity );
			fixture.Name = "Structure damage test fixture";
			fixture.NetworkMode = NetworkMode.Never;
			fixture.Enabled = true;
			var ship = fixture.GetComponent<ArcadeShip>();
			ship.Ocean = null;
			ship.Body.Enabled = true;
			ship.Body.MotionEnabled = false;
			var health = fixture.GetComponent<ShipHealth>();
			health.ResetHealth();
			await GameTask.DelaySeconds( 0.5f );
			var structure = fixture.GetComponent<ShipStructure>();
			Check( structure.IsValid() && structure.VisiblePartCount == ShipStructureCatalog.Parts.Length, "All authored structural sections are present" );
			var rail = structure.Parts.First( x => x.PartName.StartsWith( "Gunwale_2_-1" ) );
			var center = rail.WorldPosition;
			var intact = TraceAcross( ship, center );
			Check( intact.Hit && intact.Collider?.GameObject == rail.GameObject, "Cannon-height railing has its own collision within the ship rigidbody" );
			await FireAt( sailor.Ship, ship, center );
			Check( health.BrokenParts.Count == 1 && health.BrokenParts[0].Index == rail.Index, "Shot removes only the struck railing span" );
			Check( !rail.GameObject.Enabled && !TraceAcross( ship, center ).Hit, "Broken railing leaves a real collision gap" );
			Check( health.HoleCount == 0 && health.Health == health.MaximumHealth, "Railing hit does not create a hull circle or sinking damage" );
			Check( structure.DetachedPartCount == 1, "The struck rail becomes a physical detached beam" );
			var beam = ship.Scene.GetAllComponents<ShipDebris>().First( x => x.Owner == fixture );
			var start = beam.WorldPosition;
			await GameTask.DelaySeconds( 0.25f );
			Check( beam.GetComponent<CapsuleCollider>().IsDynamic && beam.WorldPosition.Distance( start ) > 5, "Detached beam is simulated by the physics engine" );
			Check( !health.BreakPart( rail.Index, Vector3.Left ), "Repeated destruction is rejected" );
			var mast = structure.Parts.First( x => x.PartName == "Mast main" );
			await FireAt( sailor.Ship, ship, mast.WorldPosition );
			Check( !mast.GameObject.Enabled && !structure.Parts.First( x => x.PartName == "Mast top" ).GameObject.Enabled,
				"Severing the main mast removes the unsupported upper mast" );
			Check( !structure.Parts.First( x => x.PartName == "Main yard" ).GameObject.Enabled && !ship.Sails.RigSupported && ship.Sails.DriveFraction == 0,
				"Unsupported yard and sail are removed and propulsion stops" );
			Check( structure.Parts.First( x => x.PartName == "Mast base" ).GameObject.Enabled, "The mast stump remains attached" );
			Check( health.HoleCount == 0, "Mast impact does not create a giant circular hole" );
			await FireAt( sailor.Ship, ship, ship.WorldTransform.PointToWorld( new Vector3( -100, 120, 20 ) ) );
			Check( health.HoleCount == 1, "A planking impact still makes a hull breach" );
			int broken = health.BrokenParts.Count;
			structure.Destroy();
			await GameTask.DelaySeconds( 0.1f );
			structure = fixture.AddComponent<ShipStructure>();
			structure.Configure( ship );
			await GameTask.DelaySeconds( 0.1f );
			Check( structure.VisiblePartCount == ShipStructureCatalog.Parts.Length - broken && structure.DetachedPartCount == 0,
				"Retained breakage reconstructs without replaying detached beams" );
			health.ResetHealth();
			await GameTask.DelaySeconds( 0.1f );
			Check( structure.VisiblePartCount == ShipStructureCatalog.Parts.Length && ship.Sails.RigSupported, "Reset restores structure and sail support" );
			Check( TraceAcross( ship, center ).Hit, "Reset restores the railing collider" );
			Log.Info( "SHIP STRUCTURE TEST PASSED: targeted rail/mast impacts, real gaps, physical detached beams, support loss, hull classification, reconstruction and reset." );
		}
		catch ( Exception e ) { Log.Error( $"SHIP STRUCTURE TEST FAILED: {e.Message}" ); }
		finally
		{
			if ( fixture.IsValid() ) fixture.Destroy();
			running = false;
		}
	}

	[ConCmd( "naval_structure_preview" )]
	public static async Task Preview( bool breakMast = true )
	{
		if ( !Game.IsEditor || !GameplayAuthority.CanMutate ) return;
		var sailor = Game.ActiveScene?.GetAllComponents<ShipPlayer>().FirstOrDefault( x => !x.IsNpc );
		if ( !sailor.IsValid() || sailor.Scene.IsEditor ) return;
		foreach ( var npc in sailor.Scene.GetAllComponents<ShipNpc>() ) npc.Enabled = false;
		var ship = sailor.Ship;
		var source = sailor.Scene.GetAllComponents<ArcadeShip>().FirstOrDefault( x => x != ship );
		if ( !source.IsValid() ) return;
		ship.Body.MotionEnabled = false;
		var structure = ship.GetComponent<ShipStructure>();
		await FireAt( source, ship, structure.Parts.First( x => x.PartName.StartsWith( "Gunwale_2_-1" ) ).WorldPosition );
		if ( breakMast ) await FireAt( source, ship, structure.Parts.First( x => x.PartName == "Mast main" ).WorldPosition );
		Check( ship.GetComponent<ShipHealth>().BrokenParts.Count > 0, "Preview shots broke the intended structure" );
		Log.Info( $"STRUCTURE PREVIEW: {ship.GetComponent<ShipHealth>().BrokenParts.Count} removed parts. Restart Play for a fresh voyage." );
	}

	internal static async Task FireAt( ArcadeShip source, ArcadeShip target, Vector3 position )
	{
		var go = new GameObject( target.Scene, true, "Structure test cannonball" );
		go.NetworkMode = Networking.IsActive ? NetworkMode.Object : NetworkMode.Never;
		go.WorldPosition = position + target.WorldRotation * Vector3.Left * 150 + Vector3.Up * 4;
		var ball = go.AddComponent<Cannonball>();
		ball.Source = source;
		ball.Velocity = target.WorldRotation * Vector3.Right * 1200;
		if ( Networking.IsActive ) go.NetworkSpawn( null );
		for ( int i = 0; i < 40 && go.IsValid(); i++ ) await GameTask.DelaySeconds( 0.05f );
		if ( go.IsValid() ) { go.Destroy(); throw new InvalidOperationException( "Structure test cannonball missed" ); }
		await GameTask.DelaySeconds( 0.1f );
	}

	private static SceneTraceResult TraceAcross( ArcadeShip ship, Vector3 position ) => ship.Scene.Trace
		.Ray( position + ship.WorldRotation * Vector3.Left * 15, position + ship.WorldRotation * Vector3.Right * 15 )
		.WithoutTags( ShipDebris.Tag ).Run();

	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
		Log.Info( $"SHIP STRUCTURE TEST OK: {message}" );
	}
}
