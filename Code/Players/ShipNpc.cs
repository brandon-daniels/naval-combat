using System;

namespace NavalCombat;

/// <summary>Host-controlled captain: walks the deck and submits only occupied station controls.</summary>
public sealed class ShipNpc : Component
{
	[Property] public ShipPlayer Sailor { get; set; }
	[Property, Sync( SyncFlags.FromHost )] public ArcadeShip Target { get; set; }
	[Property] public float EngagementRange { get; set; } = 1800;
	[Property] public float DetectionRange { get; set; } = 6000;
	[Property] public float DisengageRange { get; set; } = 8500;
	[Property] public float MaximumPursuitSeconds { get; set; } = 120;
	[Property] public float ReengageDelay { get; set; } = 30;
	[Property] public float PatrolRadius { get; set; } = 1800;
	[Property] public float HomeLeash { get; set; } = 10000;
	[Sync( SyncFlags.FromHost )] public string Behaviour { get; private set; } = "Patrolling";
	[Sync( SyncFlags.FromHost )] public Vector3 PatrolCenter { get; private set; }
	[Sync( SyncFlags.FromHost )] public int Recoveries { get; private set; }
	private bool hasPatrolCenter;
	private Vector3 patrolGoal;
	private int patrolLeg;
	private float nextPatrolGoal;
	private float pursuitStarted;
	private float reacquireAt;
	private bool alternateAisle;
	private float recoveryUntil;
	[Property] public float IslandClearance { get; set; } = 700;
	[Property] public float CruisingDeployment { get; set; } = 0.65f;
	[Property] public float TargetAimHeight { get; set; } = 73;
	[Sync( SyncFlags.FromHost )] public string NavigationStatus { get; private set; } = "Planning";
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
	[Sync( SyncFlags.FromHost )] public string Activity { get; private set; } = "Finding target";
	[Sync( SyncFlags.FromHost )] public int ShotsFired { get; private set; }
	[Sync( SyncFlags.FromHost )] public int StationsVisited { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool UsedSails { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool UsedHelm { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool UsedCannon { get; private set; }
	/// <summary>Local-only diagnostic counter: clients must never advance the captain's decisions.</summary>
	public int DecisionTicks { get; private set; }
	private ShipStation destination;
	private float desiredDeployment;
	private bool sailing;
	private float blockedTime;
	private Vector3 lastPosition;

	private static float Wrap( float angle ) => (angle + 540) % 360 - 180;
	private static float Heading( Vector3 direction ) => MathF.Atan2( direction.y, direction.x ) * 180 / MathF.PI;

	protected override void OnFixedUpdate()
	{
		if ( !GameplayAuthority.CanMutate || IsProxy ) return;
		DecisionTicks++;
		if ( !Sailor.IsValid() || !Sailor.Controller.IsValid() ) return;
		var player = Sailor.Controller;
		var ship = Sailor.Ship;
		if ( !ship.IsValid() || !ship.Helm.IsValid() || !ship.Sails.IsValid() )
		{
			Sailor.CurrentStation?.Release( player );
			player.WishVelocity = Vector3.Zero;
			return;
		}
		if ( ship.IsSinking )
		{
			Behaviour = "Sinking";
			Activity = "Ship sinking";
			Sailor.CurrentStation?.Release( player );
			player.WishVelocity = Vector3.Zero;
			return;
		}
		if ( !hasPatrolCenter )
		{
			PatrolCenter = ship.WorldPosition.WithZ( 0 );
			patrolGoal = PatrolCenter;
			hasPatrolCenter = true;
		}
		if ( Time.Now >= nextIslandScan )
		{
			route.ReadIslands( Scene, Math.Max( 350, IslandClearance ) );
			nextIslandScan = Time.Now + 2;
		}
		UpdateTarget( ship );
		bool engaging = Target.IsValid();
		if ( !engaging ) UpdatePatrol( ship );
		var goal = engaging ? Target.WorldPosition : patrolGoal;
		var offset = (goal - ship.WorldPosition).WithZ( 0 );
		float distance = offset.Length;
		if ( !engaging ) sailing = distance > 260;
		else
		{
			if ( distance > EngagementRange + 400 ) sailing = true;
			if ( distance < EngagementRange && route.Clear( ship.WorldPosition, goal ) ) sailing = false;
			if ( !route.Clear( ship.WorldPosition, goal ) ) sailing = true;
			Behaviour = sailing ? "Pursuing" : "Attacking";
		}
		float yaw = ship.WorldRotation.Angles().yaw;
		float bearing = Heading( offset );
		// Choose the closer broadside so a solo sailor need not circle the target.
		float broadside = Math.Abs( Wrap( bearing - 90 - yaw ) ) < Math.Abs( Wrap( bearing + 90 - yaw ) ) ? bearing - 90 : bearing + 90;
		var rig = ship.Sails;
		bool navigable = true;
		float desiredHeading = sailing ? Navigate( ship, goal, out navigable ) : broadside;
		float headingError = Wrap( desiredHeading - yaw );
		if ( Math.Abs( headingError ) > 60 ) turning = true;
		if ( Math.Abs( headingError ) < 8 ) turning = false;
		bool windAvailable = rig.Wind.IsValid() && rig.Wind.Enabled && rig.Wind.CurrentStrength > 0.05f;
		desiredDeployment = sailing && navigable && windAvailable && !turning ? Math.Clamp( CruisingDeployment, 0.2f, 0.75f ) : 0;
		float trim = rig.Wind.IsValid() ? Wrap( rig.Wind.Heading - desiredHeading - rig.SailAngle ) : 0;
		bool needsSails = Math.Abs( rig.Deployment - desiredDeployment ) > 0.005f || (sailing && navigable && Math.Abs( trim ) > (Sailor.IsAtMast ? 5 : 25));
		var cannon = Scene.GetAllComponents<ShipCannon>().Where( x => x.Ship == ship && x.Enabled && (!x.Occupied || x.Occupant == player) )
			.OrderBy( x => Math.Abs( Wrap( bearing - x.WorldRotation.Angles().yaw ) ) ).FirstOrDefault();
		if ( Sailor.CurrentStation is ShipCannon occupied && Math.Abs( Wrap( bearing - occupied.WorldRotation.Angles().yaw ) ) < 30 ) cannon = occupied;

		// Complete a station operation before reconsidering the next walk.
		if ( destination.IsValid() && (!destination.Enabled || (destination.Occupied && destination.Occupant != player)) ) SetDestination( null );
		if ( !destination.IsValid() || Sailor.CurrentStation == destination )
		{
			if ( needsSails ) SetDestination( rig );
			else if ( !engaging || sailing || Math.Abs( headingError ) > (Sailor.CurrentStation is ShipCannon ? 25 : 14) || !cannon.IsValid() ) SetDestination( ship.Helm );
			else SetDestination( cannon );
		}
		if ( !destination.IsValid() )
		{
			Activity = "Waiting for an available station";
			player.WishVelocity = Vector3.Zero;
			return;
		}
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
			Activity = !windAvailable ? "Waiting for wind" : sailing ? NavigationStatus : engaging ? "Turning broadside" : "Patrol holding";
			ship.SetHelmTarget( ship.Helm, navigable && (engaging || sailing) ? Math.Clamp( headingError / 25, -1, 1 ) : 0 );
		}
		else if ( destination is ShipCannon gun )
		{
			UsedCannon = true;
			Activity = "Aiming cannon";
			AimAndFire( gun );
		}
	}

	private void UpdateTarget( ArcadeShip ship )
	{
		var targets = Scene.GetAllComponents<ShipPlayer>()
			.Where( x => !x.IsNpc && x.Ship.IsValid() && x.Ship.Enabled && !x.Ship.IsSinking && x.Ship != ship )
			.Select( x => x.Ship ).Distinct().ToArray();
		if ( Target.IsValid() && (!targets.Contains( Target )
			|| (Target.WorldPosition - ship.WorldPosition).WithZ( 0 ).Length > Math.Max( DetectionRange, DisengageRange )
			|| (ship.WorldPosition - PatrolCenter).WithZ( 0 ).Length > Math.Max( PatrolRadius + 500, HomeLeash )
			|| Time.Now - pursuitStarted >= Math.Max( 1, MaximumPursuitSeconds )) )
		{
			Target = null;
			reacquireAt = Time.Now + Math.Max( 1, ReengageDelay );
			patrolGoal = PatrolCenter;
			ResetCourse();
		}
		if ( Target.IsValid() || Time.Now < reacquireAt ) return;
		var replacement = targets.Where( x => (x.WorldPosition - ship.WorldPosition).WithZ( 0 ).Length <= Math.Max( 0, DetectionRange )
			&& (x.WorldPosition - PatrolCenter).WithZ( 0 ).Length <= Math.Max( PatrolRadius + 500, HomeLeash ) )
			.OrderBy( x => (x.WorldPosition - ship.WorldPosition).Length ).FirstOrDefault();
		if ( !replacement.IsValid() ) return;
		Target = replacement;
		pursuitStarted = Time.Now;
		ResetCourse();
	}

	private void ResetCourse()
	{
		SetDestination( null );
		route.Points.Clear();
		nextPlan = 0;
		hasCourse = false;
		sailing = true;
	}

	private void UpdatePatrol( ArcadeShip ship )
	{
		bool returning = Time.Now < reacquireAt || (ship.WorldPosition - PatrolCenter).WithZ( 0 ).Length > Math.Max( PatrolRadius + 500, HomeLeash );
		Behaviour = returning ? "Disengaging" : "Patrolling";
		if ( returning )
		{
			patrolGoal = PatrolCenter;
			return;
		}
		if ( Time.Now < nextPatrolGoal ) return;
		if ( (ship.WorldPosition - patrolGoal).WithZ( 0 ).Length > 300 && route.Clear( patrolGoal, patrolGoal ) ) return;
		// Try a bounded set of destinations. Unsafe shorelines produce a hold and later retry.
		float wind = ship.Sails.Wind.IsValid() ? ship.Sails.Wind.Heading : 0;
		for ( int i = 0; i < 8; i++ )
		{
			var candidate = PatrolCenter + Rotation.FromYaw( patrolLeg++ * 45 ).Forward * Math.Max( 400, PatrolRadius );
			if ( !route.Plan( ship.WorldPosition, candidate, wind ) ) continue;
			patrolGoal = candidate;
			plannedGoal = candidate;
			plannedWind = wind;
			nextPlan = Time.Now + 2;
			return;
		}
		patrolGoal = ship.WorldPosition.WithZ( 0 );
		nextPatrolGoal = Time.Now + 5;
	}

	private float Navigate( ArcadeShip ship, Vector3 goal, out bool navigable )
	{
		float wind = ship.Sails.Wind.IsValid() ? ship.Sails.Wind.Heading : 0;
		var position = ship.WorldPosition.WithZ( 0 );
		goal = goal.WithZ( 0 );
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
		NavigationStatus = Math.Abs( Wrap( course - wind ) ) > 100 ? "Sailing upwind tack" : route.Points.Count > 1 ? "Routing around island" : Target.IsValid() ? "Closing on target" : "Sailing patrol";
		return course;
	}

	private void SetDestination( ShipStation station )
	{
		if ( station.IsValid() && (!station.Enabled || (station.Occupied && station.Occupant != Sailor.Controller)) ) station = null;
		if ( destination == station ) return;
		if ( Sailor.IsAtHelm ) Sailor.Ship.SetHelmTarget( Sailor.Helm, 0 );
		if ( Sailor.CurrentStation is ShipCannon previousGun ) previousGun.SetLoading( Sailor.Controller, false );
		Sailor.CurrentStation?.Release( Sailor.Controller );
		destination = station;
		blockedTime = 0;
		alternateAisle = false;
	}

	private void WalkToStation()
	{
		var player = Sailor.Controller;
		if ( Time.Now < recoveryUntil )
		{
			Activity = "Recovering footing";
			player.WishVelocity = Vector3.Zero;
			return;
		}
		if ( !destination.SeatPosition.IsValid() )
		{
			SetDestination( null );
			player.WishVelocity = Vector3.Zero;
			return;
		}
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
		float aisle = alternateAisle ? 85 : local.x < 45 || seat.x < 45 ? -85 : 0;
		var waypoint = Math.Abs( local.x - seat.x ) > 35 ? new Vector3( seat.x, aisle, seat.z ) : seat;
		if ( Math.Abs( local.y - aisle ) > 18 && Math.Abs( local.x - seat.x ) > 35 ) waypoint = new Vector3( local.x, aisle, seat.z );
		var direction = (ship.WorldPosition + ship.WorldRotation * waypoint - WorldPosition).WithZ( 0 );
		player.WishVelocity = direction.Normal * Math.Min( 120, direction.Length * 4 );
		if ( direction.Length > 5 ) player.EyeAngles = new Angles( 0, Heading( direction ), 0 );
		blockedTime = (local - lastPosition).WithZ( 0 ).Length < 0.25f ? blockedTime + Time.Delta : 0;
		lastPosition = local;
		if ( blockedTime > 3 ) alternateAisle = true;
		if ( blockedTime > 8 )
		{
			Activity = "Recovering from blocked station";
			SetDestination( null );
			Sailor.ReturnToDeck();
			Recoveries++;
			recoveryUntil = Time.Now + 2;
		}
	}

	private void AimAndFire( ShipCannon gun )
	{
		if ( !gun.Muzzle.IsValid() || !Target.IsValid() || !Target.Body.IsValid() )
		{
			gun.SetLoading( Sailor.Controller, false );
			return;
		}
		gun.SetLoading( Sailor.Controller, !gun.IsLoaded );
		if ( !gun.IsLoaded ) Activity = $"Loading cannon {gun.LoadProgress * 100:0}%";
		var start = gun.Muzzle.WorldPosition;
		var aim = Target.WorldPosition + Vector3.Up * TargetAimHeight;
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
		if ( elevation < gun.MinimumElevation || elevation > 45 )
		{
			Activity = "Target outside cannon elevation";
			return;
		}
		gun.Aim( Sailor.Controller, Math.Clamp( (turn - gun.Yaw) / 5, -1, 1 ), Math.Clamp( (elevation - gun.Elevation) / 5, -1, 1 ), Time.Delta );
		if ( Math.Abs( turn - gun.Yaw ) > 1.5f || Math.Abs( elevation - gun.Elevation ) > 1.5f ) return;
		var trace = Scene.Trace.Ray( start, aim ).IgnoreGameObjectHierarchy( Sailor.Ship.GameObject ).IgnoreGameObjectHierarchy( GameObject ).Run();
		if ( trace.Hit && trace.GameObject != Target.GameObject && !Target.GameObject.IsDescendant( trace.GameObject ) )
		{
			Activity = "Shot obstructed";
			return;
		}
		if ( !gun.IsLoaded ) return;
		if ( gun.Fire( Sailor.Controller ) )
		{
			ShotsFired++;
			Activity = "Firing broadside";
		}
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
		if ( !GameplayAuthority.CanMutate || IsProxy || !Sailor.IsValid() ) return;
		if ( Sailor.CurrentStation is ShipCannon gun ) gun.SetLoading( Sailor.Controller, false );
		Sailor.CurrentStation?.Release( Sailor.Controller );
		if ( Sailor.Controller.IsValid() ) Sailor.Controller.WishVelocity = Vector3.Zero;
	}
}
