using Sandbox;
using Editor;
namespace NavalCombat;
public static class CannonSizeInspection
{
	[ConCmd( "naval_cannon_bounds" )]
	public static void Run() => Log.Info( $"CANNON BOUNDS: {Model.Load( "lowpoly pirates/models/cannon.vmdl" ).Bounds}" );
}
