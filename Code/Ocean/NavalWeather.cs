using System;

namespace NavalCombat;

public enum SeaWeather
{
	MediumChoppyDay,
	StillWaters,
	Stormy
}

/// <summary>Controls the sea state and the sky in the editor and during a voyage.</summary>
public sealed partial class NavalWeather : Component, Component.ExecuteInEditor
{
	[Property, Sync( SyncFlags.FromHost )] public SeaWeather Preset { get; set; } = SeaWeather.MediumChoppyDay;

	/// <summary>Hour at the start of Play. Scrub this in the editor to preview the sky and water.</summary>
	[Property, Sync( SyncFlags.FromHost ), Range( 0, 24 )] public float StartHour { get; set; } = 12;

	/// <summary>Advance time in Play. Turn this off to hold the scene at Start Hour.</summary>
	[Property, Sync( SyncFlags.FromHost )] public bool CycleEnabled { get; set; } = true;

	/// <summary>Real minutes for one complete 24-hour cycle at normal game speed.</summary>
	[Property, Sync( SyncFlags.FromHost ), Range( 1, 240 )] public float DayLengthMinutes { get; set; } = 24;

	[Property] public ArcadeOcean Ocean { get; set; }
	private ArcadeOcean appliedOcean;

	public float CurrentHour
	{
		get
		{
			double elapsed = CycleEnabled && !Scene.IsEditor ? Time.NowDouble : 0;
			double hours = StartHour + elapsed * 24 / (Math.Max( 1, DayLengthMinutes ) * 60);
			return (float)(hours - 24 * Math.Floor( hours / 24 ));
		}
	}

	protected override void OnEnabled() => Apply();
	protected override void OnUpdate() => Apply();

	private void Apply()
	{
		float hour = CurrentHour;
		var (waves, foam, clouds, storm) = Preset switch
		{
			SeaWeather.StillWaters => (0.045f, 0.04f, 0.06f, 0f),
			SeaWeather.Stormy => (1.9f, 1.65f, 1f, 1f),
			_ => (1f, 1f, 0.38f, 0f)
		};

		UpdateSky( hour, clouds );
		if ( appliedOcean != Ocean )
		{
			ResetOcean();
			appliedOcean = Ocean;
		}
		if ( appliedOcean.IsValid() )
		{
			appliedOcean.ApplyWeather( waves, foam, DaylightAt( hour ), storm, LightDirectionAt( hour ) );
		}
	}

	private void ResetOcean()
	{
		if ( appliedOcean.IsValid() )
		{
			appliedOcean.ApplyWeather( 1, 1, 1, 0 );
		}
		appliedOcean = null;
	}

	protected override void OnDisabled()
	{
		ResetOcean();
		RestoreSky();
	}
}
