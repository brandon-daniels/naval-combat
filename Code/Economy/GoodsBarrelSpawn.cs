namespace NavalCombat;

/// <summary>Editor marker: move and rotate this object, then assign it to a VoyagePort's Barrel Spawn.</summary>
public sealed class GoodsBarrelSpawn : Component, Component.ExecuteInEditor
{
	private ModelRenderer preview;
	protected override void OnUpdate()
	{
		if ( !Scene.IsEditor )
		{
			if ( preview.IsValid() ) preview.Destroy();
			return;
		}
		if ( preview.IsValid() ) return;
		preview = GameObject.AddComponent<ModelRenderer>();
		preview.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked;
		preview.Model = Model.Load( GoodsBarrel.ModelPath );
		preview.Tint = new Color( 1, 0.8f, 0.35f );
	}
	protected override void OnDisabled() { if ( preview.IsValid() ) preview.Destroy(); }
	protected override void OnDestroy() { if ( preview.IsValid() ) preview.Destroy(); }

	protected override void DrawGizmos()
	{
		Gizmo.Draw.Color = Color.Yellow;
		Gizmo.Draw.LineSphere( Vector3.Zero, 24 );
		Gizmo.Draw.Line( Vector3.Zero, Vector3.Up * 70 );
		Gizmo.Draw.Line( Vector3.Zero, Vector3.Forward * 50 );
	}
}
