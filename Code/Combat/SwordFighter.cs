using System;

namespace NavalCombat;

public sealed class SwordFighter : Component
{
	[Property] public PlayerController Controller { get; set; }
	[Property] public GameObject Sword { get; set; }
	[Property] public bool IsNpc { get; set; }
	[Property] public bool NpcAttacks { get; set; } = true;
	[Property] public Vector3 SpawnPosition { get; set; }
	[Property] public float Reach { get; set; } = 100;
	[Property] public int Damage { get; set; } = 25;
	[Property] public float SwingCooldown { get; set; } = 0.65f;
	[Sync( SyncFlags.FromHost )] public int Health { get; set; } = 100;
	[Sync( SyncFlags.FromHost )] public int SwingNumber { get; set; }
	[Sync( SyncFlags.FromHost )] public string Feedback { get; set; } = "Ready";
	private float nextAttack;
	private float respawnAt;
	private float swingAt = -10;
	private int seenSwing;
	public bool IsLocalPlayer => !IsNpc && !IsProxy;
	private bool Authority => !Networking.IsActive || Networking.IsHost;

	protected override void OnUpdate()
	{
		if ( !Controller.IsValid() ) return;
		if ( IsLocalPlayer && Health > 0 && Input.Pressed( "Attack1" ) ) Swing();
		if ( !IsProxy )
		{
			Controller.UseInputControls = !IsNpc && Health > 0;
			if ( Health <= 0 ) Controller.WishVelocity = Vector3.Zero;
		}
		if ( Authority )
		{
			if ( Health <= 0 && Time.Now >= respawnAt )
			{
				Health = 100;
				nextAttack = Time.Now + 1;
				ResetPosition();
				Feedback = "Respawned";
			}
			if ( WorldPosition.z < -200 && Health > 0 ) TakeHit( 100 );
			if ( IsNpc && NpcAttacks && Health > 0 )
			{
				var target = Scene.GetAllComponents<SwordFighter>().Where( x => !x.IsNpc && x.Health > 0 )
					.OrderBy( x => (x.WorldPosition - WorldPosition).Length ).FirstOrDefault();
				if ( target.IsValid() && (target.WorldPosition - WorldPosition).Length < 220 )
				{
					Controller.EyeAngles = Rotation.LookAt( (target.WorldPosition - WorldPosition).WithZ( 0 ).Normal ).Angles();
					if ( (target.WorldPosition - WorldPosition).Length < Reach - 10 ) TrySwing();
				}
			}
		}
		if ( seenSwing != SwingNumber ) { seenSwing = SwingNumber; swingAt = Time.Now; }
		if ( Sword.IsValid() )
		{
			Sword.Enabled = Health > 0;
			float phase = Math.Clamp( (Time.Now - swingAt) / 0.35f, 0, 1 );
			var aim = Rotation.FromYaw( Controller.EyeAngles.yaw );
			Sword.WorldPosition = WorldPosition + Vector3.Up * 43 + aim.Forward * 18 + aim.Right * 15;
			Sword.WorldRotation = aim * Rotation.FromYaw( phase < 1 ? -65 + phase * 130 : 5 ) * Rotation.FromPitch( -15 );
			Controller.Renderer.SetIk( "hand_right", new Transform( Sword.WorldPosition - Sword.WorldRotation.Forward * 6, Sword.WorldRotation ) );
		}
	}

	public void Swing()
	{
		if ( IsLocalPlayer ) RequestSwing();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestSwing()
	{
		if ( IsNpc ) return;
		TrySwing();
	}

	/// <summary>Host traces the first obstruction; clients never supply a victim or damage amount.</summary>
	public bool TrySwing()
	{
		if ( !Authority || Health <= 0 || Time.Now < nextAttack || !Controller.IsValid() ) return false;
		nextAttack = Time.Now + Math.Max( 0.2f, SwingCooldown ) * (IsNpc ? 2 : 1);
		SwingNumber++;
		var start = WorldPosition + Vector3.Up * 42;
		var direction = Rotation.FromYaw( Controller.EyeAngles.yaw ).Forward;
		var hit = Scene.Trace.Ray( start, start + direction * Reach ).Radius( 14 ).IgnoreGameObjectHierarchy( GameObject ).Run();
		var victim = hit.GameObject.IsValid() ? hit.GameObject.Components.Get<SwordFighter>( FindMode.EverythingInSelfAndAncestors ) : null;
		if ( victim.IsValid() && victim != this && victim.Health > 0 )
		{
			victim.TakeHit( Math.Clamp( Damage, 1, 100 ) );
			Feedback = victim.Health <= 0 ? "Defeated opponent" : $"Hit - {victim.Health} HP remaining";
		}
		else Feedback = "Miss";
		return true;
	}

	private void TakeHit( int damage )
	{
		if ( !Authority || Health <= 0 ) return;
		Health = Math.Max( 0, Health - damage );
		if ( Health == 0 ) { respawnAt = Time.Now + 3; Feedback = "Defeated - respawning in 3 seconds"; }
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	private void ResetPosition()
	{
		WorldPosition = SpawnPosition;
		Controller.WishVelocity = Vector3.Zero;
		if ( Controller.Body.IsValid() ) Controller.Body.Velocity = Vector3.Zero;
	}
}
