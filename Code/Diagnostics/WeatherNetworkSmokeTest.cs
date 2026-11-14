using System;
using System.Threading.Tasks;

namespace NavalCombat;

/// <summary>Checks the weather actually rendered and sampled by a playing host and client.</summary>
public static class WeatherNetworkSmokeTest
{
	public struct View
	{
		public SeaWeather Preset { get; set; }
		public bool Cycling { get; set; }
		public float StartHour { get; set; }
		public float Minutes { get; set; }
		public float Hour { get; set; }
		public float SceneSeconds { get; set; }
		public float WaterSeconds { get; set; }
		public float SkySeconds { get; set; }
		public Vector3 SkyPixels { get; set; }
		public Vector3 WaterPixels { get; set; }
		public Vector3 Heights { get; set; }
		public float Amplitude { get; set; }
		public float Foam { get; set; }
		public float Storm { get; set; }
		public float WaterDay { get; set; }
		public float SkyDay { get; set; }
		public float Cover { get; set; }
		public Vector3 Sun { get; set; }
	}

	private static bool running;
	private static string probeId;
	private static Guid clientId;
	private static readonly Dictionary<Guid, (View View, string Error, float Received)> reports = new();

	[ConCmd( "naval_test_weather_network" )]
	public static async Task Run( bool waitForJoin = false )
	{
		if ( running || !Game.IsEditor || !Networking.IsActive || !Networking.IsHost ) return;
		var weather = Game.ActiveScene.GetAllComponents<NavalWeather>().SingleOrDefault();
		if ( !weather.IsValid() || !weather.Ocean.IsValid() || !weather.Network.Active )
		{
			Log.Error( "WEATHER NETWORK TEST needs the active networked Weather in a sailing scene." );
			return;
		}

		var original = (weather.Preset, weather.StartHour, weather.CycleEnabled, weather.DayLengthMinutes);
		running = true;
		try
		{
			var existing = Connection.All.Select( peer => peer.Id ).ToArray();
			if ( waitForJoin )
			{
				weather.Preset = SeaWeather.Stormy;
				weather.StartHour = 0;
				weather.CycleEnabled = false;
				weather.DayLengthMinutes = 2;
				Log.Info( "WEATHER NETWORK TEST staged stormy midnight. Join with a new client within 180 seconds." );
			}
			Connection FindClient() => Connection.All.FirstOrDefault( peer => peer.IsActive && peer != Connection.Local
				&& (!waitForJoin || !existing.Contains( peer.Id )) );
			var client = FindClient();
			for ( int i = 0; waitForJoin && client is null && i < 360; i++ )
			{
				await GameTask.DelaySeconds( 0.5f );
				client = FindClient();
			}
			if ( client is null ) throw new InvalidOperationException( "No active test client. Use true to stage weather before joining." );
			clientId = client.Id;
			await Verify( weather, waitForJoin ? "late join, before any host edit" : "initial host state" );

			weather.CycleEnabled = false;
			weather.DayLengthMinutes = 2;
			var ranges = new Dictionary<SeaWeather, float>();
			foreach ( var preset in new[] { SeaWeather.StillWaters, SeaWeather.MediumChoppyDay, SeaWeather.Stormy } )
			{
				weather.Preset = preset;
				View noon = default;
				foreach ( float hour in new[] { 12f, 0f } )
				{
					weather.StartHour = hour;
					var sample = await Verify( weather, $"{preset} at {hour:00}:00", true );
					var h = sample.Heights;
					ranges[preset] = Math.Max( h.x, Math.Max( h.y, h.z ) ) - Math.Min( h.x, Math.Min( h.y, h.z ) );
					if ( hour == 12 ) noon = sample;
					else if ( !(noon.SkyPixels.Length - sample.SkyPixels.Length > 0.12f
						&& noon.WaterPixels.Length - sample.WaterPixels.Length > 0.12f) )
						throw new InvalidOperationException( $"{preset} failed to darken both rendered sky and water at night." );
				}
			}
			if ( Math.Abs( weather.Ocean.WaveHeight ) > 0.01f && !(ranges[SeaWeather.StillWaters] < ranges[SeaWeather.MediumChoppyDay]
				&& ranges[SeaWeather.MediumChoppyDay] < ranges[SeaWeather.Stormy]) )
				throw new InvalidOperationException( "Fixed-position water samples did not grow from still to choppy to stormy." );

			// Sample the cycle while dawn changes both lighting shaders.
			float elapsed = Time.Now * 24 / (weather.DayLengthMinutes * 60);
			weather.StartHour = (6 - elapsed % 24 + 24) % 24;
			weather.CycleEnabled = true;
			var first = await Verify( weather, "running clock" );
			await GameTask.DelaySeconds( 1.5f );
			var last = await Verify( weather, "running clock again" );
			float advance = HourDifference( last.Hour, first.Hour );
			if ( !(advance > 0.1f && advance < 6) ) throw new InvalidOperationException( $"Client clock advanced {advance:F3} h." );
			if ( !(last.WaterDay - first.WaterDay > 0.01f && last.SkyDay - first.SkyDay > 0.01f) )
				throw new InvalidOperationException( $"Client dawn did not brighten both renderers. Sky {first.SkyDay:F3} to {last.SkyDay:F3}, water {first.WaterDay:F3} to {last.WaterDay:F3}." );
			Log.Info( $"WEATHER NETWORK TEST PASSED client={clientId} presets=6/6 lateJoin={waitForJoin} clockAdvance={advance:F3}h " +
				$"sampleRanges={ranges[SeaWeather.StillWaters]:F2}/{ranges[SeaWeather.MediumChoppyDay]:F2}/{ranges[SeaWeather.Stormy]:F2} units." );
		}
		catch ( Exception e )
		{
			Log.Error( $"WEATHER NETWORK TEST FAILED client={clientId} {e.Message}" );
		}
		finally
		{
			if ( weather.IsValid() )
			{
				(weather.Preset, weather.StartHour, weather.CycleEnabled, weather.DayLengthMinutes) = original;
			}
			probeId = null;
			clientId = default;
			reports.Clear();
			running = false;
		}
	}

	private static async Task<View> Verify( NavalWeather weather, string stage, bool capture = false )
	{
		string reason = "No peer report";
		for ( int attempt = 0; attempt < 5; attempt++ )
		{
			await GameTask.DelaySeconds( 0.4f );
			probeId = Guid.NewGuid().ToString();
			reports.Clear();
			float sent = Time.Now;
			Probe( probeId, clientId, capture );
			for ( int i = 0; i < 25 && reports.Count < 2; i++ ) await GameTask.DelaySeconds( 0.1f );
			if ( !reports.TryGetValue( Connection.Local.Id, out var host ) || !reports.TryGetValue( clientId, out var client ) ) continue;
			reason = host.Error ?? client.Error ?? Validate( weather, host.View ) ?? Validate( weather, client.View );
			if ( reason is not null ) continue;
			float deltaZ = (host.View.Heights - client.View.Heights).Length;
			float deltaClouds = MathF.Abs( host.View.Cover - client.View.Cover );
			float deltaFoam = MathF.Abs( host.View.Foam - client.View.Foam );
			float deltaHour = HourDifference( client.View.Hour, host.View.Hour );
			// The remote capture occurs between sending the request and receiving its reply.
			float seconds = client.Received - sent;
			float window = seconds * (weather.CycleEnabled ? 24 / (weather.DayLengthMinutes * 60) : 0);
			float waterTimeDelta = client.View.WaterSeconds - host.View.WaterSeconds;
			float skyTimeDelta = client.View.SkySeconds - host.View.SkySeconds;
			const float frameSlack = 0.35f;
			if ( !(deltaZ <= 0.02f && deltaHour >= -0.06f && deltaHour <= window + 0.06f
				&& waterTimeDelta >= -frameSlack && waterTimeDelta <= seconds + frameSlack
				&& skyTimeDelta >= -frameSlack && skyTimeDelta <= seconds + frameSlack) )
			{
				reason = $"wave delta={deltaZ:F3}, clock delta={deltaHour:F3} h, shader deltas={skyTimeDelta:F3}/{waterTimeDelta:F3} s, RTT={seconds:F3} s";
				continue;
			}
			float skyPixels = (client.View.SkyPixels - host.View.SkyPixels).Length;
			float waterPixels = (client.View.WaterPixels - host.View.WaterPixels).Length;
			if ( capture && !(skyPixels < 0.18f && waterPixels < 0.18f && client.View.SkyPixels.Length > 0.02f && client.View.WaterPixels.Length > 0.02f) )
			{
				reason = $"rendered colors disagree sky={host.View.SkyPixels}/{client.View.SkyPixels} sea={host.View.WaterPixels}/{client.View.WaterPixels}";
				continue;
			}
			Log.Info( $"WEATHER NETWORK TEST OK {stage} client={clientId} waveDelta={deltaZ:F4} clockDelta={deltaHour:F4}h " +
				$"shaderTimeDeltas={skyTimeDelta:F3}/{waterTimeDelta:F3}s RTT={seconds:F3}s" +
				(capture ? $" skyRGB={client.View.SkyPixels} waterRGB={client.View.WaterPixels} pixelDeltas={skyPixels:F3}/{waterPixels:F3}" : "") + " " +
				$"cover={client.View.Cover:F2} foam={client.View.Foam:F2} day={client.View.SkyDay:F2}/{client.View.WaterDay:F2} " +
				$"coverDelta={deltaClouds:F3} foamDelta={deltaFoam:F3}" );
			return client.View;
		}
		throw new InvalidOperationException( $"{stage} did not converge. {reason}" );
	}

	private static string Validate( NavalWeather expected, View view )
	{
		if ( !(MathF.Abs( view.WaterSeconds - view.SceneSeconds ) < 0.35f && MathF.Abs( view.SkySeconds - view.SceneSeconds ) < 0.35f
			&& MathF.Abs( view.SkySeconds - view.WaterSeconds ) < 0.35f) )
			return $"stale animation time scene={view.SceneSeconds:F3} water={view.WaterSeconds:F3} sky={view.SkySeconds:F3}";
		if ( view.Preset != expected.Preset || view.Cycling != expected.CycleEnabled || view.Minutes != expected.DayLengthMinutes || view.StartHour != expected.StartHour )
			return $"replicated settings are {view.Preset}, cycling={view.Cycling}, start={view.StartHour}, minutes={view.Minutes}";
		if ( !expected.CycleEnabled && !(MathF.Abs( HourDifference( view.Hour, expected.StartHour ) ) < 0.001f) ) return $"held clock reads {view.Hour}";
		var (amplitude, foam, cover, storm) = expected.Preset switch
		{
			SeaWeather.StillWaters => (0.045f, 0.04f, 0.06f, 0f),
			SeaWeather.Stormy => (1.9f, 1.65f, 1f, 1f),
			_ => (1f, 1f, 0.38f, 0f)
		};
		float day = NavalWeather.DaylightAt( view.Hour );
		if ( !(MathF.Abs( view.Cover - cover ) < 0.005f && MathF.Abs( view.Storm - storm ) < 0.005f
			&& MathF.Abs( view.Amplitude - expected.Ocean.WaveHeight * amplitude ) < 0.01f
			&& MathF.Abs( view.Foam - expected.Ocean.FoamStrength * foam ) < 0.01f) )
			return $"wrong surface attributes cover={view.Cover} storm={view.Storm} wave={view.Amplitude} foam={view.Foam}";
		// The ocean renderer and sky may have been drawn one frame before the clock was sampled.
		float dayTolerance = expected.CycleEnabled ? 0.08f : 0.001f;
		if ( !(MathF.Abs( view.WaterDay - day ) < dayTolerance && MathF.Abs( view.SkyDay - day ) < dayTolerance
			&& MathF.Abs( view.Sun.Length - 1 ) < 0.01f) ) return $"wrong lighting hour={view.Hour} sky={view.SkyDay} water={view.WaterDay} sun={view.Sun}";
		float elevation = MathF.Sin( (view.Hour - 6) * MathF.PI / 12 ) * 0.8660254f;
		if ( !(MathF.Abs( view.Sun.z - elevation ) < dayTolerance) ) return $"wrong solar elevation {view.Sun.z} at {view.Hour}";
		return null;
	}

	private static float HourDifference( float newer, float older ) => (newer - older + 36) % 24 - 12;

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void Probe( string id, Guid target, bool capture )
	{
		if ( !Networking.IsHost && Connection.Local.Id != target ) return;
		var scene = Game.ActiveScene;
		var weather = scene?.GetAllComponents<NavalWeather>().FirstOrDefault();
		var ocean = weather?.Ocean;
		var surface = ocean.IsValid() ? ocean.GetComponent<ModelRenderer>() : null;
		View view = default;
		string error = null;
		if ( !surface.IsValid() || !surface.SceneObject.IsValid() ) error = "Ocean renderer is unavailable";
		else
		{
			var seaMaterial = surface.Model?.Materials.FirstOrDefault();
			var skyMaterial = scene.GetAllComponents<SkyBox2D>().FirstOrDefault( x => x.Active )?.SkyMaterial;
			if ( !HasMaterial( seaMaterial, "materials/ocean/naval_ocean.vmat", "naval_ocean" )
				|| !HasMaterial( skyMaterial, "materials/sky/naval_weather_sky.vmat", "naval_weather_sky" ) )
				error = $"Unexpected materials water={seaMaterial?.ResourcePath} ({seaMaterial?.ShaderName}), sky={skyMaterial?.ResourcePath} ({skyMaterial?.ShaderName})";
			var water = surface.SceneObject.Attributes;
			var sky = scene.RenderAttributes;
			if ( water.GetFloat( "OceanHasBreakers", -1 ) != 1 ) error = "Exterior water mesh is missing its breaking-front data";
			if ( error is null && weather.Preset == SeaWeather.Stormy && water.GetFloat( "OceanWaveHeight" ) > 20
				&& water.GetFloat( "OceanFoamStrength" ) >= 0.07f && ocean.CrestSteepness >= 0.5f )
			{
				// A lull may hide this local renderer between bursts. Its loaded material survives.
				var spray = ocean.GetComponentsInChildren<ModelRenderer>( true, true ).FirstOrDefault( x => x.GameObject.Name == "Breaking-wave spray" );
				var sprayMaterial = spray?.Model?.Materials.FirstOrDefault();
				if ( !HasMaterial( sprayMaterial, "materials/ocean/naval_break_spray.vmat", "naval_break_spray" ) )
					error = $"Waiting for spray material on this peer. Current material={sprayMaterial?.ResourcePath} ({sprayMaterial?.ShaderName})";
			}
			view = new View
			{
				Preset = weather.Preset, StartHour = weather.StartHour, Minutes = weather.DayLengthMinutes, Cycling = weather.CycleEnabled, Hour = weather.CurrentHour,
				SceneSeconds = Time.Now, WaterSeconds = water.GetFloat( "OceanTime", -9999 ), SkySeconds = sky.GetFloat( "NavalSkyTime", -9999 ),
				Heights = new Vector3( ocean.HeightAt( new Vector3( 1625, -810, 0 ), 37.25f ), ocean.HeightAt( new Vector3( -2570, 675, 0 ), 37.25f ),
					ocean.HeightAt( new Vector3( 120, 3150, 0 ), 39.5f ) ),
				Amplitude = water.GetFloat( "OceanWaveHeight", -9999 ), Foam = water.GetFloat( "OceanFoamStrength", -9999 ),
				Storm = water.GetFloat( "OceanStorminess", -9999 ), WaterDay = water.GetFloat( "OceanDaylight", -9999 ),
				Cover = sky.GetFloat( "NavalSkyCover", -9999 ), SkyDay = sky.GetFloat( "NavalSkyDaylight", -9999 ), Sun = sky.GetVector( "NavalSkySun" )
			};
			if ( capture && error is null )
			{
				try { (view.SkyPixels, view.WaterPixels) = CaptureHorizon( scene, ocean.SeaLevel ); }
				catch ( Exception e ) { error = $"Unable to render the test horizon. {e.Message}"; }
			}
		}
		if ( Networking.IsHost ) reports[Connection.Local.Id] = (view, error, Time.Now);
		else Report( id, view, error );
	}

	private static (Vector3 Sky, Vector3 Water) CaptureHorizon( Scene scene, float seaLevel )
	{
		using var scope = scene.Push();
		var viewpoint = new GameObject( false, "Weather diagnostic camera" );
		try
		{
			viewpoint.NetworkMode = NetworkMode.Never;
			viewpoint.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.Hidden;
			var camera = viewpoint.AddComponent<CameraComponent>();
			camera.IsMainCamera = false;
			camera.FieldOfView = 80;
			camera.ZNear = 4;
			camera.ZFar = ArcadeOcean.ViewDistance;
			camera.BackgroundColor = Color.Magenta;
			camera.WorldPosition = new Vector3( 1200, -2200, seaLevel + 225 );
			camera.WorldRotation = Rotation.From( new Angles( 1.5f, 45, 0 ) );
			viewpoint.Enabled = true;
			using var bitmap = new Bitmap( 96, 54 );
			camera.RenderToBitmap( bitmap, false );
			Vector3 Band( int top )
			{
				var sum = Vector3.Zero;
				int count = 0;
				for ( int y = top; y < top + 8; y += 2 )
				for ( int x = 40; x < 84; x += 2 )
				{
					var pixel = bitmap.GetPixel( x, y );
					sum += new Vector3( pixel.r, pixel.g, pixel.b );
					count++;
				}
				return sum / count;
			}
			return (Band( 5 ), Band( 40 ));
		}
		finally
		{
			viewpoint.Destroy();
		}
	}

	private static bool HasMaterial( Material material, string path, string shader ) =>
		material.IsValid() && string.Equals( material.ResourcePath, path, StringComparison.OrdinalIgnoreCase )
		&& material.ShaderName.Contains( shader, StringComparison.OrdinalIgnoreCase );

	[Rpc.Host]
	private static void Report( string id, View view, string error )
	{
		if ( !Game.IsEditor || !running || id != probeId || Rpc.Caller?.Id != clientId ) return;
		reports[clientId] = (view, error, Time.Now);
	}
}
