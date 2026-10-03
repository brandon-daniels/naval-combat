using System;

namespace NavalCombat;

/// <summary>Local captain: walks the deck and submits only occupied station controls.</summary>
public sealed class ShipNpc : Component
{
	[Property] public ShipPlayer Sailor { get; set; }
	[Property] public ArcadeShip Target { get; set; }
	[Property] public bool AutoAcquireTarget { get; set; } = true;
	[Property] public float DeckWalkSpeed { get; set; } = 120;
	[Property] public float EngagementRange { get; set; } = 1800;
	[Property] public float IslandClearance { get; set; } = 700;
	[Property] public float CruisingDeployment { get; set; } = 0.65f;
	public string NavigationStatus { get; private set; } = "Planning";
	public int WaypointsRemaining => route.Points.Count;
	public int TacksCompleted { get; private set; }
	private readonly SailingRoute route = new();
	private Vector3 plannedGoal;
	private float plannedWind;
	private float nextPlan;
	private float nextIslandScan;
	private bool turning;
	private float previousCourse;
	private bool hasCourse;
	public string Activity { get; private set; } = "Finding target";
	public int ShotsFired { get; private set; }
	public int StationsVisited { get; private set; }
	public bool UsedSails { get; private set; }
	public bool UsedHelm { get; private set; }
	public bool UsedCannon { get; private set; }
	private ShipStation destination;
	private float desiredDeployment;
	private bool sailing;
	private float blockedTime;
	private Vector3 lastPosition;

	private static float Wrap( float angle ) => (angle + 540) % 360 - 180;
	private static float Heading( Vector3 direction ) => MathF.Atan2( direction.y, direction.x ) * 180 / MathF.PI;

	protected override void OnFixedUpdate()
	{
		if ( !Sailor.IsValid() || !Sailor.Controller.IsValid() ) return;
		var player = Sailor.Controller;
		var ship = Sailor.Ship;
		if ( Networking.IsActive || !ship.IsValid() || !ship.Helm.IsValid() || !ship.Sails.IsValid() )
		{
			Sailor.CurrentStation?.Release( player );
			player.WishVelocity = Vector3.Zero;
			return;
		}
		if ( AutoAcquireTarget && (!Target.IsValid() || Target == ship || !Target.Enabled) )
			Target = Scene.GetAllComponents<ArcadeShip>().Where( x => x != ship && x.Enabled ).OrderBy( x => (x.WorldPosition - ship.WorldPosition).Length ).FirstOrDefault();
		if ( !Target.IsValid() || Target == ship || !Target.Enabled )
		{
			Activity = "Waiting for another ship";
			Sailor.CurrentStation?.Release( player );
			player.WishVelocity = Vector3.Zero;
			return;
		}

		var offset = (Target.WorldPosition - ship.WorldPosition).WithZ( 0 );
		float distance = offset.Length;
		if ( Time.Now >= nextIslandScan )
		{
			route.ReadIslands( Scene, Math.Max( 350, IslandClearance ) );
			nextIslandScan = Time.Now + 2;
		}
		if ( distance > EngagementRange + 400 ) sailing = true;
		if ( distance < EngagementRange && route.Clear( ship.WorldPosition, Target.WorldPosition ) ) sailing = false;
		if ( !route.Clear( ship.WorldPosition, Target.WorldPosition ) ) sailing = true;
		float yaw = ship.WorldRotation.Angles().yaw;
		float bearing = Heading( offset );
		// Choose the closer broadside so a solo sailor need not circle the target.
		float broadside = Math.Abs( Wrap( bearing - 90 - yaw ) ) < Math.Abs( Wrap( bearing + 90 - yaw ) ) ? bearing - 90 : bearing + 90;
		var rig = ship.Sails;
		bool navigable = true;
		float desiredHeading = sailing ? Navigate( ship, out navigable ) : broadside;
		float headingError = Wrap( desiredHeading - yaw );
		if ( Math.Abs( headingError ) > 60 ) turning = true;
		if ( Math.Abs( headingError ) < 8 ) turning = false;
		bool windAvailable = rig.Wind.IsValid() && rig.Wind.Enabled && rig.Wind.CurrentStrength > 0.05f;
		desiredDeployment = sailing && navigable && windAvailable && !turning ? Math.Clamp( CruisingDeployment, 0.2f, 0.75f ) : 0;
		float trim = rig.Wind.IsValid() ? Wrap( rig.Wind.Heading - desiredHeading - rig.SailAngle ) : 0;
		bool needsSails = Math.Abs( rig.Deployment - desiredDeployment ) > 0.005f || (sailing && navigable && Math.Abs( trim ) > (Sailor.IsAtMast ? 5 : 25));
		var cannon = Scene.GetAllComponents<ShipCannon>().Where( x => x.Ship == ship && x.Enabled && (!x.IsOccupied || x.GetOccupant() == player) )
			.OrderBy( x => Math.Abs( Wrap( bearing - x.WorldRotation.Angles().yaw ) ) ).FirstOrDefault();
		if ( Sailor.CurrentStation is ShipCannon occupied && Math.Abs( Wrap( bearing - occupied.WorldRotation.Angles().yaw ) ) < 30 ) cannon = occupied;

		// Complete a station operation before reconsidering the next walk.
		if ( !destination.IsValid() || Sailor.CurrentStation == destination )
		{
			if ( needsSails ) SetDestination( rig );
			else if ( sailing || Math.Abs( headingError ) > (Sailor.CurrentStation is ShipCannon ? 25 : 14) || !cannon.IsValid() ) SetDestination( ship.Helm );
			else SetDestination( cannon );
		}
		if ( !destination.IsValid() ) return;
		if ( Sailor.CurrentStation != destination )
		{
			WalkToStation();
			return;
		}
		player.WishVelocity = Vector3.Zero;
		if ( destination == rig )
		{
			UsedSails = true;
			Activity = desiredDeployment > 0 ? "Setting and trimming sails" : "Furling sails";
			rig.Adjust( player, Math.Clamp( trim / 12, -1, 1 ), Math.Clamp( (desiredDeployment - rig.Deployment) * 10, -1, 1 ), Time.Delta );
		}
		else if ( destination == ship.Helm )
		{
			UsedHelm = true;
			Activity = !windAvailable ? "Waiting for wind" : sailing ? NavigationStatus : "Turning broadside";
			ship.SetHelmInput( ship.Helm, navigable ? Math.Clamp( headingError / 25, -1, 1 ) : 0 );
		}
		else if ( destination is ShipCannon gun )
		{
			UsedCannon = true;
			Activity = "Aiming cannon";
			AimAndFire( gun );
		}
	}

	private float Navigate( ArcadeShip ship, out bool navigable )
	{
		float wind = ship.Sails.Wind.IsValid() ? ship.Sails.Wind.Heading : 0;
		var position = ship.WorldPosition.WithZ( 0 );
		var goal = Target.WorldPosition.WithZ( 0 );
		while ( route.Points.Count > 0 && (route.Points[0] - position).Length < 260 ) route.Points.RemoveAt( 0 );
		bool stale = route.Points.Count == 0 || (goal - plannedGoal).Length > 450 || Math.Abs( Wrap( wind - plannedWind ) ) > 10
			|| !route.Clear( position, route.Points[0] );
		if ( stale && Time.Now >= nextPlan )
		{
			route.Plan( position, goal, wind );
			plannedGoal = goal;
			plannedWind = wind;
			nextPlan = Time.Now + 2;
		}
		navigable = route.Points.Count > 0 && route.Clear( position, route.Points[0] );
		if ( !navigable )
		{
			NavigationStatus = "No safe route; holding";
			return ship.WorldRotation.Angles().yaw;
		}
		float course = Heading( route.Points[0] - position );
		float relative = Wrap( course - wind );
		// Small cross-track drift must never steer a planned close-hauled leg into the no-go zone.
		if ( Math.Abs( relative ) > SailingRoute.TackAngle ) course = wind + Math.Sign( relative ) * SailingRoute.TackAngle;
		if ( hasCourse && Math.Abs( Wrap( course - previousCourse ) ) > 90 ) TacksCompleted++;
		previousCourse = course;
		hasCourse = true;
		NavigationStatus = Math.Abs( Wrap( course - wind ) ) > 100 ? "Sailing upwind tack" : route.Points.Count > 1 ? "Routing around island" : "Closing on target";
		return course;
	}

	private void SetDestination( ShipStation station )
	{
		if ( destination == station ) return;
		Sailor.CurrentStation?.Release( Sailor.Controller );
		destination = station;
		blockedTime = 0;
	}

	private void WalkToStation()
	{
		var player = Sailor.Controller;
		Activity = "Walking to " + destination.GameObject.Name;
		if ( destination.TryTake( player ) )
		{
			StationsVisited++;
			return;
		}
		// The prototype's clear center aisle avoids the mast, cabin and cannon carriages.
		var ship = Sailor.Ship;
		var local = ship.WorldRotation.Inverse * (WorldPosition - ship.WorldPosition);
		var seat = ship.WorldRotation.Inverse * (destination.SeatPosition.WorldPosition - ship.WorldPosition);
		var waypoint = Math.Abs( local.x - seat.x ) > 35 ? new Vector3( seat.x, 0, seat.z ) : seat;
		if ( Math.Abs( local.y ) > 18 && Math.Abs( local.x - seat.x ) > 35 ) waypoint = new Vector3( local.x, 0, seat.z );
		var direction = (ship.WorldPosition + ship.WorldRotation * waypoint - WorldPosition).WithZ( 0 );
		player.WishVelocity = direction.Normal * Math.Min( Math.Clamp( DeckWalkSpeed, 40, 220 ), direction.Length * 4 );
		if ( direction.Length > 5 ) player.EyeAngles = new Angles( 0, Heading( direction ), 0 );
		blockedTime = (local - lastPosition).WithZ( 0 ).Length < 0.25f ? blockedTime + Time.Delta : 0;
		lastPosition = local;
		if ( blockedTime > 8 )
		{
			Activity = "Station obstructed; retrying";
			destination = null;
			blockedTime = 0;
		}
	}

	private void AimAndFire( ShipCannon gun )
	{
		if ( !gun.Muzzle.IsValid() || !Target.Body.IsValid() ) return;
		var start = gun.Muzzle.WorldPosition;
		var aim = Target.WorldPosition + Vector3.Up * 15;
		float flight = (aim - start).WithZ( 0 ).Length / gun.MuzzleSpeed;
		// Compensate for target motion, inherited muzzle velocity, and ballistic drop.
		var relative = aim - start + (Target.Body.Velocity - Sailor.Ship.Body.GetVelocityAtPoint( start )) * flight + Vector3.Up * (200 * flight * flight);
		var local = gun.WorldRotation.Inverse * relative;
		float turn = Heading( local );
		float elevation = MathF.Atan2( local.z, local.WithZ( 0 ).Length ) * 180 / MathF.PI;
		if ( Math.Abs( turn ) > 34 )
		{
			SetDestination( Sailor.Helm );
			return;
		}
		// Wait out wave-induced pitch instead of repeatedly changing stations.
		if ( elevation < 0 || elevation > 45 ) return;
		gun.Aim( Sailor.Controller, Math.Clamp( (turn - gun.Yaw) / 5, -1, 1 ), Math.Clamp( (elevation - gun.Elevation) / 5, -1, 1 ), Time.Delta );
		if ( Math.Abs( turn - gun.Yaw ) > 1.5f || Math.Abs( elevation - gun.Elevation ) > 1.5f ) return;
		var trace = Scene.Trace.Ray( start, aim ).IgnoreGameObjectHierarchy( Sailor.Ship.GameObject ).IgnoreGameObjectHierarchy( GameObject ).Run();
		if ( trace.Hit && trace.GameObject != Target.GameObject && !trace.GameObject.IsDescendant( Target.GameObject ) ) return;
		if ( gun.Fire( Sailor.Controller ) ) ShotsFired++;
		Activity = "Firing broadside";
	}

	protected override void OnDisabled()
	{
		route.Points.Clear();
		destination = null;
		nextPlan = 0;
		nextIslandScan = 0;
		hasCourse = false;
		turning = false;
		sailing = false;
		if ( !Sailor.IsValid() ) return;
		Sailor.CurrentStation?.Release( Sailor.Controller );
		if ( Sailor.Controller.IsValid() ) Sailor.Controller.WishVelocity = Vector3.Zero;
	}
}
