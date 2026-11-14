HEADER
{
	Description = "Painted timber with localized cannonball perforations";
	Version = 1;
}
FEATURES
{
	#include "common/features.hlsl"
}
MODES
{
	Forward();
	Depth();
}
COMMON
{
	#include "common/shared.hlsl"
}
struct VertexInput
{
	#include "common/vertexinput.hlsl"
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
};
VS
{
	#include "common/vertex.hlsl"
	PixelInput MainVs( VertexInput i )
	{
		return FinalizeVertex( ProcessVertex( i ) );
	}
}
PS
{
	#include "common/pixel.hlsl"
	float4 g_vHullColor < UiType( Color ); Default4( 0.73, 0.72, 0.57, 1 ); >;
	float4 g_vBreachMouth0 < Attribute( "BreachMouth0" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachAxis0 < Attribute( "BreachAxis0" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachMouth1 < Attribute( "BreachMouth1" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachAxis1 < Attribute( "BreachAxis1" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachMouth2 < Attribute( "BreachMouth2" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachAxis2 < Attribute( "BreachAxis2" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachMouth3 < Attribute( "BreachMouth3" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachAxis3 < Attribute( "BreachAxis3" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachMouth4 < Attribute( "BreachMouth4" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachAxis4 < Attribute( "BreachAxis4" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachMouth5 < Attribute( "BreachMouth5" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachAxis5 < Attribute( "BreachAxis5" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachMouth6 < Attribute( "BreachMouth6" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachAxis6 < Attribute( "BreachAxis6" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachMouth7 < Attribute( "BreachMouth7" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachAxis7 < Attribute( "BreachAxis7" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachMouth8 < Attribute( "BreachMouth8" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachAxis8 < Attribute( "BreachAxis8" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachMouth9 < Attribute( "BreachMouth9" ); Default4( 0, 0, 0, 0 ); >;
	float4 g_vBreachAxis9 < Attribute( "BreachAxis9" ); Default4( 0, 0, 0, 0 ); >;
	float CutTimber( float3 p, float4 mouth, float4 axis )
	{
		if ( mouth.w <= 0 ) return 0;
		float3 offset = p - mouth.xyz;
		float along = dot( offset, axis.xyz );
		if ( along > 5 || along < -axis.w - 5 ) return 0;
		float distance = length( offset - along * axis.xyz );
		// A finite cut reaches the inner lining, never the opposite side of the ship.
		clip( distance - mouth.w );
		return 1 - smoothstep( mouth.w, mouth.w + 5, distance );
	}
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i );
		float exposed = 0;
		exposed = max( exposed, CutTimber( m.WorldPosition, g_vBreachMouth0, g_vBreachAxis0 ) );
		exposed = max( exposed, CutTimber( m.WorldPosition, g_vBreachMouth1, g_vBreachAxis1 ) );
		exposed = max( exposed, CutTimber( m.WorldPosition, g_vBreachMouth2, g_vBreachAxis2 ) );
		exposed = max( exposed, CutTimber( m.WorldPosition, g_vBreachMouth3, g_vBreachAxis3 ) );
		exposed = max( exposed, CutTimber( m.WorldPosition, g_vBreachMouth4, g_vBreachAxis4 ) );
		exposed = max( exposed, CutTimber( m.WorldPosition, g_vBreachMouth5, g_vBreachAxis5 ) );
		exposed = max( exposed, CutTimber( m.WorldPosition, g_vBreachMouth6, g_vBreachAxis6 ) );
		exposed = max( exposed, CutTimber( m.WorldPosition, g_vBreachMouth7, g_vBreachAxis7 ) );
		exposed = max( exposed, CutTimber( m.WorldPosition, g_vBreachMouth8, g_vBreachAxis8 ) );
		exposed = max( exposed, CutTimber( m.WorldPosition, g_vBreachMouth9, g_vBreachAxis9 ) );
		m.Albedo = lerp( g_vHullColor.rgb, float3( 0.30, 0.15, 0.055 ), exposed );
		m.Roughness = 0.9;
		return ShadingModelStandard::Shade( i, m );
	}
}
