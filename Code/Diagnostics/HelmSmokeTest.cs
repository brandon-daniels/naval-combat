using System;
using System.Threading.Tasks;

namespace NavalCombat;

/// <summary>Editor-only integration check against the running physics scene.</summary>
public static class HelmSmokeTest
{
	private static bool running;

	[ConCmd( "naval_test_helm" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || Networking.IsActive || running ) return;
		var sailor = Game.ActiveScene?.GetAllComponents<ShipPlayer>().FirstOrDefault( x => !x.IsNpc );
		if ( !sailor.IsValid() || sailor.Scene.IsEditor ) return;
		running = true;
		var player = sailor.Controller;
		var helm = sailor.Helm;
		var ship = sailor.Ship;
		float deployment = ship.Sails.Deployment;
		float angle = ship.Sails.SailAngle;
		float heading = ship.Sails.Wind.Heading;
		bool input = player.UseInputControls;
		bool look = player.UseLookControls;
		bool sailorEnabled = sailor.Enabled;
		try
		{
			ship.Sails.Deployment = 0;
			sailor.ReturnToDeck();
			sailor.Enabled = false;
			player.UseInputControls = false;
			player.UseLookControls = false;
			player.WorldPosition = ship.WorldTransform.PointToWorld( new Vector3( 250, -60, 110 ) );
			player.Transform.ClearInterpolation();
			player.Body.Velocity = ship.Body.GetVelocityAtPoint( player.WorldPosition );
			for ( int i = 0; i < 40 && !player.IsOnGround; i++ ) await GameTask.DelaySeconds( 0.1f );
			Check( player.IsOnGround, "Player lands on the deck" );
			Check( !helm.TryTake( player ), "Cannot use helm from across the deck" );
			var start = ship.WorldPosition;
			// The authored wheel is on the main deck beside the cabin, so
			// place this interaction test on that deck instead of driving the test
			// controller in a straight line through the cabin shell.
			var approach = helm.SeatPosition.WorldPosition - helm.WorldRotation.Forward * 25;
			player.WorldPosition = approach + Vector3.Up * 70;
			player.Body.Velocity = ship.Body.GetVelocityAtPoint( player.WorldPosition );
			for ( int i = 0; i < 40 && !player.IsOnGround; i++ ) await GameTask.DelaySeconds( 0.1f );
			Check( player.IsOnGround, "Player lands on the physical helm deck" );
			approach = helm.SeatPosition.WorldPosition - helm.WorldRotation.Forward * 25;
			player.WorldPosition = approach + Vector3.Up * 5;
			player.Body.Velocity = ship.Body.GetVelocityAtPoint( player.WorldPosition );
			player.WishVelocity = Vector3.Zero;
			approach = helm.SeatPosition.WorldPosition - helm.WorldRotation.Forward * 25;
			Check( (player.WorldPosition - helm.SeatPosition.WorldPosition).Length < helm.UseDistance, "Main-deck helm is physically reachable" );
			// The large hull can drift appreciably during the longer walk to the relocated wheel.
			Check( (ship.WorldPosition - start).WithZ( 0 ).Length < 160, "Walking does not drive the ship" );
			// Rolling waves can briefly lift the player off the deck.
			for ( int i = 0; i < 20 && !sailor.IsAtHelm; i++ )
			{
				sailor.ToggleHelm();
				await GameTask.DelaySeconds( 0.1f );
			}
			await GameTask.DelaySeconds( 0.3f );
			Check( sailor.IsAtHelm && !player.Body.Enabled, "Taking helm switches to mounted movement" );
			Check( !helm.TryTake( player ), "Occupied helm rejects repeated boarding" );
			start = ship.WorldPosition;
			var forward = ship.WorldRotation.Forward;
			ship.Sails.Wind.Heading = ship.WorldRotation.Angles().yaw;
			ship.Sails.SailAngle = 0;
			ship.Sails.Deployment = 1;
			for ( int i = 0; i < 20; i++ )
			{
				ship.SetHelmInput( helm, 0.5f, 0.1f );
				await GameTask.DelaySeconds( 0.1f );
			}
			Check( (ship.WorldPosition - start).Length > 100, "Wind-filled sail accelerates ship" );
			Check( ship.Rudder > 0.25f, "Helm input winds and holds the rudder" );
			await GameTask.DelaySeconds( 2 );
			Check( Vector3.Dot( forward, ship.WorldRotation.Forward ) < 0.98f, "Occupied helm steers ship" );
			Check( (player.WorldPosition - helm.SeatPosition.WorldPosition).Length < 2, "Helmsman follows moving ship" );
			sailor.ToggleHelm();
			sailor.Enabled = true;
			for ( int i = 0; i < 40 && !player.IsOnGround; i++ ) await GameTask.DelaySeconds( 0.1f );
			Check( !sailor.IsAtHelm && player.Body.Enabled && player.ColliderObject.Enabled, "Release restores body and colliders" );
			Check( !player.IsSwimming && (player.WorldPosition - helm.SeatPosition.WorldPosition).Length < 140, "Release keeps player on the moving helm deck" );
			var localStart = ship.WorldTransform.PointToLocal( player.WorldPosition );
			player.WishVelocity = -ship.WorldRotation.Forward.WithZ( 0 ).Normal * 70;
			await GameTask.DelaySeconds( 0.5f );
			player.WishVelocity = Vector3.Zero;
			Check( (ship.WorldTransform.PointToLocal( player.WorldPosition ) - localStart).Length > 15, "Walking resumes after release" );
			player.WorldPosition = ship.WorldPosition + Vector3.Down * 200;
			await GameTask.DelaySeconds( 0.8f );
			Check( player.IsSwimming && (player.WorldPosition - sailor.SpawnPoint.WorldPosition).Length > 30, "Overboard player enters swimming mode" );
			sailor.ReturnToDeck();
			for ( int i = 0; i < 40 && !player.IsOnGround; i++ )
			{
				await GameTask.DelaySeconds( 0.1f );
			}
			Check( player.IsOnGround && (player.WorldPosition - sailor.SpawnPoint.WorldPosition).Length < 40, "Manual recovery returns player to deck" );
			Log.Info( "HELM TEST PASSED: walk, range, mount, wind propulsion, steer, release, moving deck, recovery." );
		}
		catch ( Exception error )
		{
			Log.Error( $"HELM TEST FAILED: {error.Message}" );
		}
		finally
		{
			if ( sailor.IsValid() && player.IsValid() )
			{
				ship.Sails.Deployment = deployment;
				ship.Sails.SailAngle = angle;
				ship.Sails.Wind.Heading = heading;
				sailor.ReturnToDeck();
				sailor.Enabled = sailorEnabled;
				player.UseInputControls = input;
				player.UseLookControls = look;
			}
			running = false;
		}
	}

	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
		Log.Info( $"HELM TEST OK: {message}" );
	}
}
