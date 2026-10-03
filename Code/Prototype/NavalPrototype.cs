using System;
using Sandbox.Movement;
using Sandbox.UI;

namespace NavalCombat;

/// <summary>Starts the player and effects around the saved sailing world.</summary>
public sealed class NavalPrototype : Component
{
	[Property] public ArcadeShip Ship { get; set; }
	[Property] public ArcadeOcean Ocean { get; set; }
	[Property] public SailingWind Wind { get; set; }
	[Property] public GameObject SpawnPoint { get; set; }
	[Property] public bool SpawnNpcShip { get; set; } = true;
	[Property] public Vector3 NpcShipOffset { get; set; } = new( -2400, 1100, 0 );
	private GameObject content;
	private GameObject playerObject;
	private GameObject npcObject;
	private GameObject npcShip;

	protected override void OnStart()
	{
		// This scene is deliberately local until player/ship authority is implemented.
		if ( Networking.IsActive )
		{
			Log.Warning( "Naval prototype is a local handling test. Stop networking before playing this scene." );
			return;
		}

		if ( !Ship.IsValid() || !Ocean.IsValid() || !Wind.IsValid() || !SpawnPoint.IsValid() )
		{
			Log.Error( "Naval scene is missing its saved ship, ocean, wind or spawn references." );
			return;
		}
		content = new GameObject( GameObject, true, "Runtime player and effects" );
		content.NetworkMode = NetworkMode.Never;
		var controller = Ship;
		var ship = Ship.GameObject;
		var ocean = Ocean;
		var wind = Wind;
		var helm = Ship.Helm;
		var spawn = SpawnPoint;

		var windStreaks = new GameObject( content, true, "Airborne wind streaks" ).AddComponent<WindStreaks>();
		windStreaks.Wind = wind;
		windStreaks.Ocean = ocean;
		windStreaks.FollowTarget = ship;

		var cameraObject = new GameObject( content, true, "Player camera" );
		var camera = cameraObject.AddComponent<CameraComponent>();
		camera.IsMainCamera = true;
		camera.FieldOfView = 75;
		camera.ZFar = 16000;
		camera.ZNear = 4;
		camera.BackgroundColor = new Color( 0.34f, 0.58f, 0.7f );
		CreatePlayer( controller, helm, spawn );
		if ( SpawnNpcShip ) CreateNpc();
		Log.Info( "Naval prototype: E at mast or helm. Mast: A/D rotate, W raise, S lower sails. Helm: A/D steer. Wind arrow shows where wind blows. R returns to deck." );
	}

	private void CreateNpc()
	{
		npcShip = Ship.GameObject.Clone( Ship.WorldPosition + NpcShipOffset, Rotation.Identity );
		npcShip.Name = "NPC ship";
		var ship = npcShip.GetComponent<ArcadeShip>();
		ship.Ocean = Ocean;
		ship.Sails.Wind = Wind;
		var spawn = new GameObject( npcShip, true, "NPC deck spawn" );
		spawn.LocalPosition = new Vector3( -65, 0, 42 );
		var human = playerObject;
		CreatePlayer( ship, ship.Helm, spawn, true );
		npcObject = playerObject;
		playerObject = human;
		var captain = npcObject.AddComponent<ShipNpc>();
		captain.Sailor = npcObject.GetComponent<ShipPlayer>();
		captain.Target = Ship;
	}

	private void CreatePlayer( ArcadeShip ship, ShipHelm helm, GameObject spawn, bool npc = false )
	{
		// A walking player must have its own physics body, never be a child of the moving ship.
		playerObject = new GameObject( content, false, "Sailor" );
		playerObject.Tags.Add( "player" );
		playerObject.WorldPosition = spawn.WorldPosition;
		playerObject.WorldRotation = Rotation.FromYaw( ship.WorldRotation.Angles().yaw );
		var modelObject = new GameObject( playerObject, true, "Citizen body" );
		var renderer = modelObject.AddComponent<SkinnedModelRenderer>();
		renderer.Model = Model.Load( "models/citizen/citizen.vmdl" );
		var dresser = playerObject.AddComponent<Dresser>();
		dresser.BodyTarget = renderer;
		dresser.Source = Dresser.ClothingSource.LocalUser;
		dresser.ApplyHeightScale = false;
		playerObject.AddComponent<DeckWalkMode>();
		playerObject.AddComponent<OceanSwimMode>().Ocean = Ocean;
		playerObject.AddComponent<SitMoveMode>();
		var player = playerObject.AddComponent<PlayerController>();
		player.Renderer = renderer;
		player.BodyMass = 80;
		player.WalkSpeed = 120;
		player.RunSpeed = 220;
		player.ThirdPerson = true;
		player.CameraOffset = new Vector3( 160, 24, 64 );
		player.EyeAngles = new Angles( 15, ship.WorldRotation.Angles().yaw, 0 );
		player.ToggleCameraModeButton = "View";
		player.EnablePressing = false; // ShipPlayer owns the E toggle, including release while looking at the wheel.
		player.UseInputControls = !npc;
		player.UseLookControls = !npc;
		player.UseCameraControls = !npc;
		if ( npc ) renderer.Tint = new Color( 0.85f, 0.25f, 0.18f );
		player.RotateWithGround = true;
		var sailor = playerObject.AddComponent<ShipPlayer>();
		sailor.Controller = player;
		sailor.Helm = helm;
		sailor.Sails = ship.Sails;
		sailor.Ship = ship;
		sailor.SpawnPoint = spawn;
		sailor.IsNpc = npc;
		playerObject.Enabled = true;
		if ( npc ) return;
		var hud = new GameObject( content, true, "Sailing HUD" );
		hud.AddComponent<ScreenPanel>();
		hud.AddComponent<SailingHud>().Player = sailor;
	}


	protected override void OnDestroy()
	{
		// The player can have been reparented by the mounted movement mode.
		if ( playerObject.IsValid() ) playerObject.Destroy();
		if ( npcObject.IsValid() ) npcObject.Destroy();
		if ( npcShip.IsValid() ) npcShip.Destroy();
		if ( content.IsValid() ) content.Destroy();
	}
}
