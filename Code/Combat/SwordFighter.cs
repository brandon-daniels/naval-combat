using System;

namespace NavalCombat;

public sealed partial class SwordFighter : Component
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
	private int seenHealth = 100;
	private bool lockedControls;
	private bool savedInput;
	private bool savedLook;
	private bool posingHand;
	private GameObject localSword;
	private ModelRenderer heldTool;

	public bool IsLocalPlayer => !IsNpc && !IsProxy;
	private bool Authority => GameplayAuthority.CanMutate;
	private ShipPlayer Sailor => GetComponent<ShipPlayer>();
	public bool CanDuel => CanHoldItem && Sailor is not { IsRepairing: true } && Sailor is not { IsUsingBucket: true };

	protected override void OnUpdate()
	{
		UpdateDuel();
		if ( !Controller.IsValid() ) return;
		UpdateCombatControls();
		if ( IsLocalPlayer && !InDuel && Controller.UseInputControls )
		{
			ReadEquipmentInput();
			if ( EquippedItem == HeldItem.Sword && Input.Pressed( "Attack1" ) ) Swing();
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
			// Submerged sailors use the ocean movement mode. The arena has a kill plane.
			if ( !Sailor.IsValid() && WorldPosition.z < -200 && Health > 0 ) TakeHit( 100 );
			if ( IsNpc && NpcAttacks && Health > 0 ) UpdateSparringOpponent();
		}
		UpdateHeldItem();
	}

	private void UpdateCombatControls()
	{
		if ( !IsLocalPlayer ) return;
		if ( !InDuel && Health > 0 )
		{
			ReleaseControls();
			return;
		}

		if ( !lockedControls )
		{
			GetComponent<PlayerVoyage>()?.CloseInvestment();
			savedInput = Controller.UseInputControls;
			savedLook = Controller.UseLookControls;
			lockedControls = true;
		}
		Controller.UseInputControls = false;
		Controller.UseLookControls = false;
		Controller.WishVelocity = Vector3.Zero;
		if ( Opponent.IsValid() )
		{
			var direction = (Opponent.WorldPosition - WorldPosition).WithZ( 0 );
			if ( direction.LengthSquared > 1 ) Controller.EyeAngles = Rotation.LookAt( direction.Normal ).Angles();
		}
	}

	private void ReleaseControls()
	{
		if ( !lockedControls ) return;
		if ( Controller.IsValid() )
		{
			Controller.UseInputControls = savedInput;
			Controller.UseLookControls = savedLook;
		}
		lockedControls = false;
	}

	private void UpdateSparringOpponent()
	{
		var target = Scene.GetAllComponents<SwordFighter>().Where( x => !x.IsNpc && !x.InDuel && x.Health > 0 )
			.OrderBy( x => x.WorldPosition.Distance( WorldPosition ) ).FirstOrDefault();
		if ( !target.IsValid() ) return;
		var offset = target.WorldPosition - WorldPosition;
		if ( offset.Length >= 220 || offset.WithZ( 0 ).LengthSquared < 1 ) return;
		Controller.EyeAngles = Rotation.LookAt( offset.WithZ( 0 ).Normal ).Angles();
		if ( offset.Length < Reach - 10 ) TrySwing();
	}

	public void Swing()
	{
		if ( !IsLocalPlayer || EquippedItem != HeldItem.Sword || !CanDuel || InDuel ) return;
		if ( Networking.IsActive ) RequestSwing();
		else TrySwing();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RequestSwing()
	{
		if ( !IsNpc ) TrySwing();
	}

	/// <summary>The first solid hit determines the target. Only the host can start a duel or apply damage.</summary>
	public bool TrySwing()
	{
		if ( !Authority || EquippedItem != HeldItem.Sword || InDuel || Time.Now < nextAttack || !CanDuel ) return false;
		var direction = Rotation.FromYaw( Controller.EyeAngles.yaw ).Forward;
		var start = WorldPosition + Vector3.Up * Math.Min( 42, Controller.CurrentHeight * 0.65f );
		var hit = Scene.Trace.Ray( start, start + direction * Math.Clamp( Reach, 1, 160 ) ).Radius( 14 )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( ShipDebris.Tag ).Run();
		var victim = hit.GameObject.IsValid() ? hit.GameObject.GetComponentInParent<SwordFighter>() : null;

		nextAttack = Time.Now + Math.Max( 0.2f, SwingCooldown ) * (IsNpc ? 2 : 1);
		SwingNumber++;
		if ( !victim.IsValid() || victim == this )
		{
			Feedback = "Miss";
			return true;
		}
		if ( victim.InDuel || !victim.CanDuel )
		{
			Feedback = "Opponent unavailable";
			return true;
		}

		if ( !IsNpc && !victim.IsNpc )
		{
			if ( TryStartDuel( victim ) )
			{
				// The owner simulates its body, including the short collision-checked step into reach.
				StepIntoDuel( victim );
			}
			else Feedback = "Opponent unavailable";
			return true;
		}
		victim.TakeHit( Math.Clamp( Damage, 1, 100 ) );
		Feedback = victim.Health <= 0 ? "Defeated opponent" : $"Hit - {victim.Health} HP remaining";
		return true;
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	private void StepIntoDuel( SwordFighter defender )
	{
		if ( !Controller.IsValid() || !defender.IsValid() ) return;
		var offset = (defender.WorldPosition - WorldPosition).WithZ( 0 );
		float step = Math.Clamp( offset.Length - 52, 0, 42 );
		if ( step <= 0 ) return;
		var lift = Vector3.Up * 2;
		var trace = Controller.TraceBody( WorldPosition + lift, WorldPosition + lift + offset.Normal * step );
		if ( !trace.StartedSolid )
			WorldPosition += offset.Normal * Math.Max( 0, step * trace.Fraction - 1 );
	}

	private void TakeHit( int damage )
	{
		if ( !Authority || Health <= 0 ) return;
		Health = Math.Max( 0, Health - damage );
		if ( Health > 0 ) return;
		respawnAt = Time.Now + 3;
		Feedback = "Defeated - respawning in 3 seconds";
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	private void ResetPosition()
	{
		if ( Sailor is { } sailor && sailor.SpawnPoint.IsValid() && sailor.Ship.IsValid() && !sailor.Ship.IsSinking )
		{
			sailor.ReturnToDeck();
			return;
		}
		WorldPosition = SpawnPosition;
		Controller.WishVelocity = Vector3.Zero;
		if ( Controller.Body.IsValid() ) Controller.Body.Velocity = Vector3.Zero;
		Transform.ClearInterpolation();
	}

	private void UpdateHeldItem()
	{
		var body = Controller.Renderer;
		if ( body.IsValid() && seenHealth > Health )
		{
			body.Set( "hit_strength", 1f );
			body.Set( "hit_direction", Vector3.Backward );
			body.Set( "hit", true );
		}
		seenHealth = Health;
		if ( SwingNumber != seenSwing )
		{
			seenSwing = SwingNumber;
			swingAt = Time.Now;
			if ( body.IsValid() ) body.Set( "b_attack", true );
		}
		var held = SelectHeldVisual();
		if ( !held.IsValid() )
		{
			ClearSwordPose();
			return;
		}
		float phase = Math.Clamp( (Time.Now - swingAt) / 0.42f, 0, 1 );
		float thrust = EquippedItem == HeldItem.Sword ? MathF.Sin( phase * MathF.PI ) : 0;
		var aim = Rotation.FromYaw( Controller.EyeAngles.yaw );
		bool firstPerson = IsLocalPlayer && Controller.UseCameraControls && !Controller.ThirdPerson;
		if ( firstPerson ) aim = Controller.EyeTransform.Rotation;
		else if ( InDuel && Opponent.IsValid() )
		{
			var facing = (Opponent.WorldPosition - WorldPosition).WithZ( 0 );
			if ( facing.LengthSquared > 1 ) aim = Rotation.LookAt( facing.Normal );
		}
		float gripHeight = Math.Min( 45, Controller.CurrentHeight * 0.66f );
		held.WorldPosition = firstPerson
			? Controller.EyePosition + aim.Forward * (30 + 8 * thrust) + aim.Right * 11 + aim.Up * (EquippedItem == HeldItem.Bucket ? 4 : -9)
			: WorldPosition + Vector3.Up * gripHeight + aim.Forward * (13 + 19 * thrust) + aim.Right * 14;
		held.WorldRotation = EquippedItem switch
		{
			HeldItem.Sword => aim * Rotation.FromYaw( -14 + 14 * thrust ) * Rotation.FromPitch( -42 + 42 * thrust ),
			HeldItem.Planks => aim * Rotation.FromYaw( 8 ) * Rotation.FromPitch( -12 ) * Rotation.FromRoll( 18 ),
			_ => aim
		};
		if ( !body.IsValid() ) return;
		body.SetIk( "hand_right", new Transform( held.WorldPosition, held.WorldRotation ) );
		posingHand = true;
	}

	private GameObject SelectHeldVisual()
	{
		bool visible = CanHoldItem;
		bool showSword = visible && EquippedItem == HeldItem.Sword;
		bool showTool = visible && EquippedItem is HeldItem.Bucket or HeldItem.Planks;
		var sword = Sword.IsValid() ? Sword : localSword;
		if ( showSword && !sword.IsValid() ) sword = localSword = CreateSword();
		if ( sword.IsValid() ) sword.Enabled = showSword;

		if ( showTool )
		{
			if ( !heldTool.IsValid() )
			{
				var tool = new GameObject( GameObject, true, "Held ship tool" );
				tool.NetworkMode = NetworkMode.Never;
				tool.Flags |= GameObjectFlags.NotSaved;
				heldTool = tool.AddComponent<ModelRenderer>();
			}
			var model = EquippedItem == HeldItem.Planks ? ShipToolModels.Planks : ShipToolModels.Bucket( Sailor is { BucketFull: true } );
			if ( heldTool.Model != model ) heldTool.Model = model;
		}
		if ( heldTool.IsValid() ) heldTool.GameObject.Enabled = showTool;
		return showTool ? heldTool.GameObject : showSword ? sword : null;
	}

	private GameObject CreateSword()
	{
		var model = Model.Load( "lowpoly pirates/models/sword.vmdl" );
		if ( model is null || model.IsError ) return null;
		var sword = new GameObject( GameObject, true, "Sword" );
		sword.NetworkMode = NetworkMode.Never;
		sword.Flags |= GameObjectFlags.NotSaved;
		var blade = new GameObject( sword, true, "Steel and brass sword" );
		var bounds = model.Bounds;
		float scale = 48 / Math.Max( 1, bounds.Size.z );
		blade.LocalRotation = Rotation.FromPitch( 90 );
		blade.LocalScale = Vector3.One * scale;
		var grip = bounds.Center.WithZ( bounds.Mins.z + bounds.Size.z * 0.13f );
		blade.LocalPosition = -(blade.LocalRotation * (grip * scale));
		blade.AddComponent<ModelRenderer>().Model = model;
		return sword;
	}

	private void ClearSwordPose()
	{
		if ( posingHand && Controller.IsValid() && Controller.Renderer.IsValid() )
			Controller.Renderer.ClearIk( "hand_right" );
		posingHand = false;
	}

	protected override void OnDisabled()
	{
		CancelDuel();
		ReleaseControls();
		ClearSwordPose();
		if ( Sword.IsValid() ) Sword.Enabled = false;
		if ( localSword.IsValid() )
		{
			localSword.Destroy();
			localSword = null;
		}
		if ( heldTool.IsValid() )
		{
			heldTool.GameObject.Destroy();
			heldTool = null;
		}
	}
}
