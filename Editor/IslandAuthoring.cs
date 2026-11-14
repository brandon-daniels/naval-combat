using System;

namespace NavalCombat;

public static class IslandAuthoring
{
	[Menu( "Editor", "Scene/Islands/Palm Cove", "beach_access" )]
	public static void CreatePalmCove() => Create( IslandPreset.PalmCove, "Palm Cove" );

	[Menu( "Editor", "Scene/Islands/Farmer's Bay", "agriculture" )]
	public static void CreateFarmersBay() => Create( IslandPreset.FarmersBay, "Farmer's Bay" );

	[Menu( "Editor", "Scene/Islands/Granite Headland", "landscape" )]
	public static void CreateGraniteHeadland() => Create( IslandPreset.GraniteHeadland, "Granite Headland" );

	[Menu( "Editor", "Scene/Islands/Smuggler's Hollow", "explore" )]
	public static void CreateSmugglersHollow() => Create( IslandPreset.SmugglersHollow, "Smuggler's Hollow" );

	private static void Create( IslandPreset preset, string name )
	{
		var session = SceneEditorSession.Active;
		if ( session is null || session.IsPlaying || !session.Scene.IsEditor )
		{
			Log.Info( "Open a scene in edit mode to place an island." );
			return;
		}

		using var scope = session.Scene.Push();
		var seaLevel = session.Scene.GetAllComponents<ArcadeOcean>().FirstOrDefault()?.SeaLevel ?? 0f;
		using var undo = session.UndoScope( $"Create {name}" ).WithGameObjectCreations().Push();
		var root = new GameObject( false, name );
		root.MakeNameUnique();
		// Joining sailors receive the authored settings and build the same static geometry locally.
		root.NetworkMode = NetworkMode.Snapshot;
		root.WorldPosition = GetPlacement( seaLevel );
		var island = root.AddComponent<ProceduralIsland>();
		island.Preset = preset;
		island.Seed = Random.Shared.Next( 1, 1_000_000 );
		root.Enabled = true;
		session.Selection.Clear();
		session.Selection.Add( root );
	}

	private static Vector3 GetPlacement( float seaLevel )
	{
		var viewport = SceneViewWidget.Current?.LastSelectedViewportWidget;
		if ( !viewport.IsValid() ) return new Vector3( 0, 0, seaLevel );

		var eye = viewport.State.CameraPosition;
		var forward = viewport.State.CameraRotation.Forward;
		// Intersect the camera's center ray with the sea. A level or skyward view
		// places the island a few hull lengths ahead, within reach of the gizmo.
		float distance = MathF.Abs( forward.z ) > 0.001f ? (seaLevel - eye.z) / forward.z : 0f;
		if ( distance <= 0f ) distance = 3500f;
		return (eye + forward * MathF.Min( distance, 16000f )).WithZ( seaLevel );
	}
}

[CustomEditor( typeof( int ), NamedEditor = "naval-island-seed" )]
public sealed class IslandSeedControl : ControlWidget
{
	public override bool SupportsMultiEdit => true;

	public IslandSeedControl( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		Layout.Spacing = 4;
		Layout.Add( new IntegerControlWidget( property ), 1 );
		var roll = Layout.Add( new Button( "Reroll", "casino" ) );
		roll.ToolTip = "Generate a different island with the same preset and feature settings.";
		roll.Clicked = () =>
		{
			int previous = property.GetValue<int>();
			int next = Random.Shared.Next( 1, 1_000_000 );
			property.Parent.NoteStartEdit( property );
			property.SetValue( next == previous ? next + 1 : next );
			property.Parent.NoteFinishEdit( property );
		};
	}

	protected override void OnPaint() { }
}
