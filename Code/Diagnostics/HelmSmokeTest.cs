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
		var sailor = Game.ActiveScene?.GetAllComponents<ShipPlayer>().FirstOrDefault();
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
			await GameTask.DelaySeconds( 1 );
			Check( player.IsOnGround, "Player lands on the deck" );
			Check( !helm.TryTake( player ), "Cannot use helm from across the deck" );
			var start = ship.WorldPosition;
			var approach = helm.SeatPosition.WorldPosition - helm.WorldRotation.Forward * 25;
			// Allow time for the player to regain footing on the stronger rolling swells.
			for ( int i = 0; i < 80 && (player.WorldPosition - approach).WithZ( 0 ).Length > 15; i++ )
			{
				approach = helm.SeatPosition.WorldPosition - helm.WorldRotation.Forward * 25;
				player.WishVelocity = (approach - player.WorldPosition).WithZ( 0 ).Normal * 90;
				await GameTask.DelaySeconds( 0.1f );
			}
			player.WishVelocity = Vector3.Zero;
			await GameTask.DelaySeconds( 0.3f );
			approach = helm.SeatPosition.WorldPosition - helm.WorldRotation.Forward * 25;
			Check( (player.WorldPosition - helm.SeatPosition.WorldPosition).Length < helm.UseDistance, "Walk to bow on physical deck" );
			Check( (ship.WorldPosition - start).WithZ( 0 ).Length < 40, "Walking does not drive the ship" );
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
			ship.SetHelmInput( helm, 0.5f );
			await GameTask.DelaySeconds( 2 );
			Check( (ship.WorldPosition - start).Length > 100, "Wind-filled sail accelerates ship" );
			Check( Vector3.Dot( forward, ship.WorldRotation.Forward ) < 0.98f, "Occupied helm steers ship" );
			Check( (player.WorldPosition - helm.SeatPosition.WorldPosition).Length < 2, "Helmsman follows moving ship" );
			sailor.ToggleHelm();
			sailor.Enabled = true;
			await GameTask.DelaySeconds( 0.7f );
			Check( !sailor.IsAtHelm && player.Body.Enabled && player.ColliderObject.Enabled, "Release restores body and colliders" );
			Check( player.IsOnGround, "Release lands on moving deck" );
			var localStart = ship.WorldTransform.PointToLocal( player.WorldPosition );
			player.WishVelocity = -ship.WorldRotation.Forward.WithZ( 0 ).Normal * 70;
			await GameTask.DelaySeconds( 0.5f );
			player.WishVelocity = Vector3.Zero;
			Check( (ship.WorldTransform.PointToLocal( player.WorldPosition ) - localStart).Length > 15, "Walking resumes after release" );
			player.WorldPosition = ship.WorldPosition + Vector3.Down * 200;
			await GameTask.DelaySeconds( 0.8f );
			Check( player.IsSwimming, "Falling overboard enters swimming" );
			sailor.ReturnToDeck();
			await GameTask.DelaySeconds( 0.8f );
			Check( player.IsOnGround, "Manual recovery returns player to deck" );
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
