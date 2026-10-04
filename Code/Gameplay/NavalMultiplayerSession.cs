using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.UI;

namespace NavalCombat;

/// <summary>Spawns one owner-simulated ship and sailor for every active connection.</summary>
public sealed class NavalMultiplayerSession : Component, Component.INetworkListener
{
	[Property] public ArcadeShip ShipTemplate { get; set; }
	[Property] public GameObject ShipSpawn { get; set; }
	[Property] public GameObject NpcShipTemplate { get; set; }
	[Property] public float SpawnSpacing { get; set; } = 900;
	[Property] public int MaximumPlayers { get; set; } = 8;

	private readonly Dictionary<Guid, PlayerSlot> slots = new();
	private GameObject presentation;
	private SailingHud hud;
	private WindStreaks windStreaks;
	private bool localPreviewSpawned;
	private int spawnSequence;

	private sealed class PlayerSlot
	{
		public GameObject Ship { get; init; }
		public GameObject Player { get; init; }
	}

	protected override void OnStart()
	{
		if ( !ShipTemplate.IsValid() )
		{
			Log.Error( "Naval multiplayer session requires an authored ship template." );
			return;
		}

		ShipTemplate.GameObject.Enabled = false;
		if ( NpcShipTemplate.IsValid() ) NpcShipTemplate.Enabled = false;
		CreateLocalPresentation();

		if ( !Networking.IsActive )
		{
			SpawnPlayer( null );
			localPreviewSpawned = true;
		}
	}

	protected override void OnUpdate()
	{
		if ( !hud.IsValid() || hud.Player.IsValid() ) return;
		hud.Player = Scene.GetAllComponents<ShipPlayer>().FirstOrDefault( x => !x.IsNpc && !x.IsProxy );
		if ( !hud.Player.IsValid() || !hud.Player.Ship.IsValid() ) return;
		var ship = hud.Player.Ship;
		if ( ship.Ocean.IsValid() ) ship.Ocean.FollowTarget = ship.GameObject;
		if ( windStreaks.IsValid() )
		{
			windStreaks.Wind = ship.Sails?.Wind;
			windStreaks.Ocean = ship.Ocean;
			windStreaks.FollowTarget = ship.GameObject;
		}
	}

	public bool AcceptConnection( Connection connection, ref string reason )
	{
		if ( Connection.All.Count >= Math.Max( 1, MaximumPlayers ) )
		{
			reason = "This voyage is full.";
			return false;
		}
		return true;
	}

	public void OnActive( Connection connection )
	{
		connection.CanSpawnObjects = false;
		connection.CanDestroyObjects = false;
		connection.CanRefreshObjects = false;

		if ( connection == Connection.Local && localPreviewSpawned )
		{
			var preview = Scene.GetAllComponents<ShipPlayer>().FirstOrDefault( x => !x.IsNpc && !x.GameObject.Network.Active );
			if ( preview.IsValid() && preview.Ship.IsValid() )
			{
				preview.Ship.GameObject.NetworkSpawn( connection );
				preview.GameObject.NetworkSpawn( connection );
				slots[connection.Id] = new PlayerSlot { Ship = preview.Ship.GameObject, Player = preview.GameObject };
				localPreviewSpawned = false;
				return;
			}
		}

		if ( slots.ContainsKey( connection.Id ) ) return;
		if ( Scene.GetAllComponents<ShipPlayer>().Any( x => !x.IsNpc && x.Network.Owner == connection ) ) return;
		SpawnPlayer( connection );
	}

	public void OnDisconnected( Connection connection )
	{
		if ( slots.Remove( connection.Id, out var slot ) )
		{
			if ( slot.Player.IsValid() ) slot.Player.Destroy();
			if ( slot.Ship.IsValid() ) slot.Ship.Destroy();
		}

		foreach ( var player in Scene.GetAllComponents<ShipPlayer>().Where( x => !x.IsNpc && x.Network.Owner == connection ).ToArray() )
		{
			var ship = player.Ship;
			player.GameObject.Destroy();
			if ( ship.IsValid() ) ship.GameObject.Destroy();
		}
	}

	public void OnBecameHost( Connection previousHost )
	{
		RebuildSlots();
	}

	private void SpawnPlayer( Connection owner )
	{
		if ( !ShipTemplate.IsValid() ) return;
		var index = spawnSequence++;
		var origin = ShipSpawn.IsValid() ? ShipSpawn.WorldTransform : ShipTemplate.WorldTransform;
		var lateral = origin.Rotation.Right * (index * SpawnSpacing);
		var shipObject = ShipTemplate.GameObject.Clone( origin.Position + lateral, origin.Rotation );
		shipObject.Name = owner is null ? "Local preview ship" : $"{owner.DisplayName}'s ship";
		shipObject.NetworkMode = Networking.IsActive ? NetworkMode.Object : NetworkMode.Never;
		shipObject.Enabled = true;
		var ship = shipObject.GetComponent<ArcadeShip>();
		if ( !ship.IsValid() )
		{
			shipObject.Destroy();
			return;
		}

		var spawn = shipObject.GetAllObjects( true ).FirstOrDefault( x => x.Name == "Deck spawn" );
		if ( !spawn.IsValid() ) spawn = shipObject;
		var sailor = NavalPrototype.CreateSailor( GameObject, ship, spawn, false );
		var wallet = sailor.GameObject.AddComponent<PlayerWallet>();
		var production = sailor.GameObject.AddComponent<ProductionSite>();
		production.OutputGoods = ResourceLibrary.Get<GoodsDefinition>( "definitions/goods/trade_goods.ngoods" );
		var voyage = sailor.GameObject.AddComponent<PlayerVoyage>();
		voyage.Sailor = sailor;
		voyage.Wallet = wallet;
		voyage.Production = production;
		sailor.GameObject.Name = owner is null ? "Local sailor" : $"{owner.DisplayName}'s sailor";
		sailor.GameObject.NetworkMode = Networking.IsActive ? NetworkMode.Object : NetworkMode.Never;

		if ( Networking.IsActive )
		{
			shipObject.NetworkSpawn( owner );
			sailor.GameObject.NetworkSpawn( owner );
		}

		if ( owner is not null ) slots[owner.Id] = new PlayerSlot { Ship = shipObject, Player = sailor.GameObject };
	}

	private void RebuildSlots()
	{
		slots.Clear();
		foreach ( var player in Scene.GetAllComponents<ShipPlayer>().Where( x => !x.IsNpc && x.Network.Owner is not null ) )
		{
			var owner = player.Network.Owner;
			slots[owner.Id] = new PlayerSlot { Ship = player.Ship?.GameObject, Player = player.GameObject };
		}
	}

	private void CreateLocalPresentation()
	{
		presentation = new GameObject( GameObject, true, "Local sailing presentation" );
		presentation.NetworkMode = NetworkMode.Never;
		var camera = new GameObject( presentation, true, "Player camera" ).AddComponent<CameraComponent>();
		camera.IsMainCamera = true;
		camera.FieldOfView = 75;
		camera.ZFar = 16000;
		camera.ZNear = 4;
		camera.BackgroundColor = new Color( 0.34f, 0.58f, 0.7f );
		var hudObject = new GameObject( presentation, true, "Sailing HUD" );
		hudObject.AddComponent<ScreenPanel>();
		hud = hudObject.AddComponent<SailingHud>();
		windStreaks = new GameObject( presentation, true, "Airborne wind streaks" ).AddComponent<WindStreaks>();
	}

	protected override void OnDestroy()
	{
		if ( presentation.IsValid() ) presentation.Destroy();
	}
}
