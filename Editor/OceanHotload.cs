using Editor;
using Sandbox;

namespace NavalCombat;

internal static class OceanHotload
{
	// A fast code hotload can change the wave constructor while leaving existing bands alive.
	[EditorEvent.Hotload]
	private static void InvalidateWaveFields()
	{
		// Game and editor assemblies can reload independently. Look up the active game type.
		var type = Game.TypeLibrary.GetType<Component>( "NavalCombat.ArcadeOcean" );
		var invalidate = type?.GetMethod( "InvalidateWaveField" );
		if ( invalidate is null ) return;
		foreach ( var scene in Scene.All )
		{
			foreach ( var ocean in scene.GetAllComponents( type.TargetType ) )
			{
				invalidate.Invoke( ocean );
			}
		}
	}
}
