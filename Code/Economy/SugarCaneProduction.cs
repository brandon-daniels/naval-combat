using System;
using System.Collections.Generic;

namespace NavalCombat;

/// <summary>Move/rotate this scene object to place the crop. Preview never spends money or spawns cargo.</summary>
public sealed class SugarCaneProduction : Component, Component.ExecuteInEditor
{
	[Property] public Vector2 FieldSize { get; set; } = new( 360, 260 );
	[Property, Range( 2, 12 )] public int Rows { get; set; } = 5;
	[Property, Range( 2, 12 )] public int Columns { get; set; } = 7;
	[Property] public float CaneHeight { get; set; } = 135;
	[Property] public float ScytheRotationsPerSecond { get; set; } = 2;
	[Property, Range( 0, 1 )] public float PreviewProgress { get; set; } = 0.5f;
	[Property] public bool AnimatePreview { get; set; }
	private GameObject visuals;
	private GameObject packingBarrel;
	private readonly List<GameObject> stalks = new();
	private readonly List<Vector3> bases = new();
	private readonly List<GameObject> scythes = new();
	private int layoutHash;
	private float previewClock;
	public string Phase( float progress ) => progress < 2f / 3 ? "Growing sugar cane" : progress < 52f / 60 ? "Harvesting with scythes" : "Packing the barrel";

	protected override void OnUpdate()
	{
		int hash = HashCode.Combine( FieldSize, Rows, Columns, CaneHeight );
		if ( !visuals.IsValid() || hash != layoutHash ) { Build(); layoutHash = hash; }
		var port = Scene.GetAllComponents<VoyagePort>().FirstOrDefault( x => x.CaneField == this );
		var production = port.IsValid() ? Scene.GetAllComponents<ProductionSite>().FirstOrDefault( x => x.SourcePort == port && x.IsProducing ) : null;
		previewClock += Time.Delta;
		float p = Scene.IsEditor ? (AnimatePreview ? previewClock % 60 / 60 : PreviewProgress) : production.IsValid() ? production.Progress : 0;
		p = Math.Clamp( p, 0, 1 );
		float growth = Math.Clamp( p * 1.5f, 0, 1 );
		float harvest = Math.Clamp( (p * 60 - 40) / 12, 0, 1 );
		float packing = Math.Clamp( (p * 60 - 52) / 8, 0, 1 );
		var target = port.IsValid() ? port.BarrelPosition : WorldPosition + Vector3.Up * 100;
		for ( int i = 0; i < stalks.Count; i++ )
		{
			var stalk = stalks[i];
			float cut = Math.Clamp( (harvest - (float)i / stalks.Count) * stalks.Count, 0, 1 );
			float transfer = Math.Clamp( packing * 1.6f - (float)i / stalks.Count * 0.6f, 0, 1 );
			stalk.Enabled = p > 0 && transfer < 1;
			stalk.LocalPosition = bases[i] + Vector3.Up * cut * 8;
			stalk.LocalRotation = Rotation.From( 0, i * 37, cut * 85 );
			stalk.LocalScale = new Vector3( 1, 1, Math.Max( 0.01f, growth ) );
			if ( transfer > 0 )
			{
				var start = WorldTransform.PointToWorld( bases[i] );
				stalk.WorldPosition = Vector3.Lerp( start, target + Vector3.Up * 20, transfer ) + Vector3.Up * MathF.Sin( transfer * MathF.PI ) * 160;
				stalk.LocalScale *= Math.Max( 0.02f, 1 - transfer );
			}
		}
		for ( int i = 0; i < scythes.Count; i++ )
		{
			var scythe = scythes[i];
			scythe.Enabled = p >= 2f / 3 && p < 52f / 60;
			scythe.LocalPosition = new Vector3( (harvest - 0.5f) * Math.Abs( FieldSize.x ), (i == 0 ? -0.25f : 0.25f) * Math.Abs( FieldSize.y ), 30 );
			scythe.LocalRotation = Rotation.FromYaw( p * 60 * ScytheRotationsPerSecond * 360 + i * 180 );
		}
		packingBarrel.Enabled = p >= 52f / 60 && p < 1;
		packingBarrel.WorldPosition = target;
		packingBarrel.WorldRotation = port.IsValid() && port.BarrelSpawn.IsValid() ? port.BarrelSpawn.WorldRotation : Rotation.Identity;
	}

	private void Build()
	{
		if ( visuals.IsValid() ) visuals.Destroy();
		stalks.Clear(); bases.Clear(); scythes.Clear();
		visuals = LocalObject( GameObject, "Sugar cane preview and animation" );
		float width = Math.Max( 32, Math.Abs( FieldSize.x ) );
		float depth = Math.Max( 32, Math.Abs( FieldSize.y ) );
		Box( visuals, "Growing bed", new Vector3( 0, 0, -4 ), new Vector3( width, depth, 8 ), new Color( 0.25f, 0.15f, 0.07f ) );
		int rows = Math.Clamp( Rows, 2, 12 ), columns = Math.Clamp( Columns, 2, 12 );
		float height = Math.Clamp( CaneHeight, 20, 400 );
		for ( int x = 0; x < columns; x++ )
		for ( int y = 0; y < rows; y++ )
		{
			var cane = LocalObject( visuals, "Sugar cane" );
			stalks.Add( cane );
			bases.Add( new Vector3( ((x + 0.5f) / columns - 0.5f) * width, ((y + 0.5f) / rows - 0.5f) * depth, 0 ) );
			for ( int segment = 0; segment < 4; segment++ )
			{
				Box( cane, "Cane segment", Vector3.Up * ((segment + 0.5f) * height / 4), new Vector3( 5, 5, height / 4 ), new Color( 0.4f, 0.66f, 0.12f ) );
				Box( cane, "Cane joint", Vector3.Up * ((segment + 1) * height / 4), new Vector3( 6, 6, 2 ), new Color( 0.69f, 0.8f, 0.28f ) );
			}
			for ( int leaf = 0; leaf < 4; leaf++ )
			{
				var blade = Box( cane, "Cane leaf", new Vector3( leaf % 2 == 0 ? 15 : -15, 0, height * (0.65f + leaf * 0.09f) ), new Vector3( 42, 6, 1.5f ), new Color( 0.23f, 0.52f, 0.08f ) );
				blade.LocalRotation = Rotation.From( leaf % 2 == 0 ? -30 : 30, leaf * 70, 0 );
			}
		}
		for ( int i = 0; i < 2; i++ )
		{
			var scythe = LocalObject( visuals, "Spinning scythe" );
			scythes.Add( scythe );
			Box( scythe, "Wooden handle", Vector3.Zero, new Vector3( 100, 6, 6 ), new Color( 0.42f, 0.25f, 0.1f ) );
			for ( int n = 0; n < 9; n++ )
			{
				float a = n * 12 * MathF.PI / 180;
				var blade = Box( scythe, "Curved steel blade", new Vector3( 20 + MathF.Cos( a ) * 32, MathF.Sin( a ) * 48, 0 ), new Vector3( 14 - n, 12, 3 ), new Color( 0.8f, 0.87f, 0.9f ) );
				blade.LocalRotation = Rotation.FromYaw( n * 12 );
			}
		}
		packingBarrel = LocalObject( visuals, "Barrel being packed" );
		packingBarrel.AddComponent<ModelRenderer>().Model = Model.Load( GoodsBarrel.ModelPath );
	}

	private static GameObject LocalObject( GameObject parent, string name )
	{
		var obj = new GameObject( parent, true, name );
		obj.NetworkMode = NetworkMode.Never;
		obj.Flags |= GameObjectFlags.NotSaved;
		return obj;
	}

	private static GameObject Box( GameObject parent, string name, Vector3 position, Vector3 size, Color color )
	{
		var obj = LocalObject( parent, name );
		obj.LocalPosition = position;
		obj.LocalScale = size / 50;
		var renderer = obj.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load( "models/dev/box.vmdl" );
		renderer.Tint = color;
		return obj;
	}

	protected override void DrawGizmos()
	{
		Gizmo.Draw.Color = Color.Green;
		Gizmo.Draw.LineBBox( new BBox( new Vector3( -Math.Abs( FieldSize.x ) / 2, -Math.Abs( FieldSize.y ) / 2, 0 ), new Vector3( Math.Abs( FieldSize.x ) / 2, Math.Abs( FieldSize.y ) / 2, CaneHeight ) ) );
	}

	protected override void OnDisabled() { if ( visuals.IsValid() ) visuals.Destroy(); }
	protected override void OnDestroy() { if ( visuals.IsValid() ) visuals.Destroy(); }
}
