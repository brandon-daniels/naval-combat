using Sandbox;
using Editor;

namespace NavalCombat;

public static class NpcShipAuthoring
{
	[ConCmd( "naval_author_npc" )]
	public static void Build()
	{
		var session = SceneEditorSession.Active;
		if ( session is null || !session.Scene.IsEditor ) return;
		if ( session.Scene.GetAllComponents<NpcShipCrew>().Any() ) return;
		var setup = session.Scene.GetAllComponents<NavalPrototype>().FirstOrDefault();
		if ( !setup.IsValid() || !setup.Ship.IsValid() ) return;
		using var scope = SceneEditorSession.Scope();
		var root = setup.Ship.GameObject.Clone( setup.Ship.WorldPosition + new Vector3( -2400, 1100, 0 ), Rotation.Identity );
		root.Name = "NPC ship";
		var ship = root.GetComponent<ArcadeShip>();
		ship.Ocean = setup.Ocean;
		ship.Sails.Wind = setup.Wind;
		var spawn = new GameObject( root, true, "NPC captain spawn" );
		spawn.LocalPosition = new Vector3( -65, 0, 42 );
		var crew = root.AddComponent<NpcShipCrew>();
		crew.Ship = ship;
		crew.SpawnPoint = spawn;
		crew.Target = setup.Ship;
		Log.Info( "Authored NPC ship and configurable NpcShipCrew. Save the scene." );
	}
}
