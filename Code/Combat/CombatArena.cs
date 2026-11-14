using Sandbox.Movement;
using Sandbox.UI;

namespace NavalCombat;

/// <summary>Standalone sword arena. Movement is owner simulated; combat is decided by the host.</summary>
public sealed class CombatArena : Component, Component.INetworkListener
{
	[Property] public Vector3 NpcSpawn { get; set; } = new( 180, 0, 4 );
	private int spawnIndex;

	protected override void OnStart()
	{
		var cameraObject = new GameObject( Scene, true, "Combat camera" );
		cameraObject.NetworkMode = NetworkMode.Never;
		var camera = cameraObject.AddComponent<CameraComponent>();
		camera.IsMainCamera = true;
		camera.ZNear = 2;
		camera.ZFar = 6000;
		var hud = new GameObject( Scene, true, "Combat HUD" );
		hud.NetworkMode = NetworkMode.Never;
		hud.AddComponent<ScreenPanel>();
		hud.AddComponent<CombatHud>();
		if ( Networking.IsActive && !Networking.IsHost ) return;
		if ( !Scene.GetAllComponents<SwordFighter>().Any( x => x.IsNpc ) ) CreateFighter( null, true );
		if ( !Networking.IsActive ) CreateFighter( null, false );
	}

	public void OnActive( Connection connection )
	{
		// Starting hosting while already playing promotes the existing local actors.
		foreach ( var fighter in Scene.GetAllComponents<SwordFighter>().ToArray() )
		{
			if ( fighter.GameObject.Network.Active ) continue;
			fighter.GameObject.NetworkSpawn( fighter.IsNpc ? null : Connection.Local );
		}
		if ( Scene.GetAllComponents<SwordFighter>().Any( x => !x.IsNpc && x.Network.Owner == connection ) ) return;
		CreateFighter( connection, false );
	}

	public void OnDisconnected( Connection connection )
	{
		foreach ( var fighter in Scene.GetAllComponents<SwordFighter>().Where( x => !x.IsNpc && x.Network.Owner == connection ).ToArray() )
			fighter.GameObject.Destroy();
	}

	private void CreateFighter( Connection owner, bool npc )
	{
		var go = new GameObject( Scene, false, npc ? "Sparring NPC" : "Swordsman" );
		go.WorldPosition = npc ? NpcSpawn : new Vector3( -180, (spawnIndex++ % 4) * 100 - 100, 4 );
		var body = new GameObject( go, true, "Citizen" ).AddComponent<SkinnedModelRenderer>();
		body.Model = Model.Load( "models/citizen/citizen.vmdl" );
		body.Tint = npc ? new Color( 0.9f, 0.3f, 0.22f ) : new Color( 0.25f, 0.65f, 0.95f );
		go.AddComponent<MoveModeWalk>();
		var controller = go.AddComponent<PlayerController>();
		controller.Renderer = body;
		controller.WalkSpeed = 160;
		controller.RunSpeed = 240;
		controller.ThirdPerson = true;
		controller.CameraOffset = new Vector3( 150, 24, 64 );
		controller.ToggleCameraModeButton = "View";
		controller.EnablePressing = false;
		controller.UseInputControls = !npc;
		controller.UseLookControls = !npc;
		controller.UseCameraControls = !npc;
		var fighter = go.AddComponent<SwordFighter>();
		fighter.Controller = controller;
		fighter.IsNpc = npc;
		fighter.SpawnPosition = go.WorldPosition;
		go.Enabled = true;
		if ( Networking.IsActive ) go.NetworkSpawn( owner );
	}
}
