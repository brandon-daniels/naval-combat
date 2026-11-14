using System;
using System.Runtime.CompilerServices;

namespace NavalCombat;

/// <summary>Directional, short-crested swell and wind sea. Each train fans out across the
/// water: neighbouring headings interfere, bend the face and pinch off the ends of its crest.</summary>
internal sealed class OceanWaveField
{
	internal struct Sample
	{
		public float Height, Dx, Dy;
		public Vector2 Breaker;
	}

	private readonly record struct Band( Vector2 Direction, Vector2 K, float Speed, float Phase,
		Vector2 GroupK, float GroupSpeed, float GroupPhase, float Amplitude,
		Vector2 FanK, float FanSpeed, float FanPhase, float Sideband, float WaveNumber );
	private readonly Band[] bands;
	private float[] crossingFronts;
	private Vector2[] crossingDirections;
	private Vector2[] fanColumns;
	public Vector2 TravelDirection => bands[0].Direction;
	public float MaximumAmplitude { get; }

	public OceanWaveField( int seed, float directionalSpread = 1f )
	{
		var random = new Random( seed );
		float bearing = (float)random.NextDouble() * MathF.Tau;
		float spread = Math.Clamp( directionalSpread, 0.5f, 1.5f );
		Band CreateBand( float length, float headingOffset, float amplitude, int index )
		{
			float heading = bearing + (headingOffset + ((float)random.NextDouble() - 0.5f) * 0.23f) * spread;
			var direction = new Vector2( MathF.Cos( heading ), MathF.Sin( heading ) );
			float k = MathF.Tau / (length * (0.92f + 0.16f * (float)random.NextDouble()));
			float speed = MathF.Sqrt( 490f * k );
			// A deep-water packet moves forward at half its carrier's phase speed.
			// The cross-train projection interrupts long ridgelines at different places.
			float across = (0.30f + (float)random.NextDouble() * 0.50f) * (random.Next( 2 ) == 0 ? -1 : 1);
			float along = 0.25f + (float)random.NextDouble() * 0.24f;
			var groupK = (direction * along + new Vector2( -direction.y, direction.x ) * across) * k;
			float phase = (float)random.NextDouble() * MathF.Tau;
			float groupPhase = (float)random.NextDouble() * MathF.Tau;
			// Lateral sidebands flank the central wavenumber. Their interference makes
			// the crest wander across its nominal bearing, with several hundred units
			// between the nose and either wing on the ocean swell.
			float fanSpread = Math.Clamp( spread, 0.65f, 1.3f );
			var transverse = new Vector2( -direction.y, direction.x );
			float fanSign = index % 2 == 0 ? 1 : -1;
			var fanK = (direction * (0.13f + index * 0.025f) + transverse * (fanSign * 1.34f * fanSpread)) * k;
			float sideband = index < 2 ? 0.87f : 0.56f;
			return new Band( direction, direction * k, speed, phase, groupK, speed * along * 0.5f,
				groupPhase, amplitude, fanK, speed * (0.13f + 0.05f * index), phase * 0.63f - groupPhase * 1.27f, sideband, k );
		}

		bands = [
			// Long and cross swells span roughly six and four 864-unit hulls.
			CreateBand( 5290, 0, 1.13f, 0 ), CreateBand( 3510, -1.17f, 0.69f, 1 ),
			CreateBand( 1640, 1.56f, 0.42f, 2 ), CreateBand( 930, -2.22f, 0.22f, 3 ),
			CreateBand( 490, 0.55f, 0.090f, 4 )
		];
		// At full steepness the absolute Fourier coefficients sum to less than 2.0.
		foreach ( var band in bands ) MaximumAmplitude += band.Amplitude * 2f;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static float Profile( float sin, float cos, float sharpness, out float derivative )
	{
		// Bound harmonics peak progressively farther forward in the direction of travel.
		// Their phases are offset by (harmonic - 1) * 1.28 radians. This gives one
		// narrow forward lip, a steep descending face, and a long climb up the back.
		float sin2 = 2 * sin * cos;
		float cos2 = cos * cos - sin * sin;
		float sin3 = sin2 * cos + cos2 * sin;
		float cos3 = cos2 * cos - sin2 * sin;
		float sin4 = 2 * sin2 * cos2;
		float cos4 = cos2 * cos2 - sin2 * sin2;
		float harmonics = 0.181f * cos2 + 0.604f * sin2 - 0.201f * cos3 + 0.132f * sin3
			- 0.077f * cos4 - 0.064f * sin4;
		derivative = -sin + sharpness * (2 * (0.604f * cos2 - 0.181f * sin2)
			+ 3 * (0.132f * cos3 + 0.201f * sin3) + 4 * (0.077f * sin4 - 0.064f * cos4));
		return cos + sharpness * harmonics;
	}

	private readonly record struct Carrier( float Sin, float Cos, float Envelope,
		Vector2 PhaseGradient, Vector2 EnvelopeGradient );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static Carrier ShapeCarrier( in Band band, float sin, float cos, float groupSin, float groupCos,
		float fanSin, float fanCos )
	{
		// In complex form the directional pair adds a quadrature sideband, i*q, to the
		// central carrier. Normalizing (1, q) changes its phase by atan(q), which bends
		// the crest without moving vertices horizontally or creating an inverse-map seam.
		float q = band.Sideband * fanSin + 0.62f * groupSin;
		float inverseSquare = 1f / (1f + q * q);
		float inverse = MathF.Sqrt( inverseSquare );
		var phaseGradient = band.K + (band.FanK * (band.Sideband * fanCos) + band.GroupK * (0.62f * groupCos)) * inverseSquare;
		float group = 0.46f + 0.135f * (1 + groupCos) * (1 + groupCos);
		// Shorter crest islands develop along a broad swell, with independent travel
		// times for the packet envelope and transverse interference pattern.
		float cap = 0.78f + 0.22f * fanCos;
		float envelope = group * cap;
		var envelopeGradient = band.GroupK * (-0.27f * (1 + groupCos) * groupSin * cap)
			+ band.FanK * (-0.22f * fanSin * group);
		return new Carrier( (sin + cos * q) * inverse, (cos - sin * q) * inverse,
			envelope, phaseGradient, envelopeGradient );
	}

	private static Carrier EvaluateCarrier( in Band band, Vector3 position, float time )
	{
		float phase = position.x * band.K.x + position.y * band.K.y + band.Phase - time * band.Speed;
		float group = position.x * band.GroupK.x + position.y * band.GroupK.y + band.GroupPhase - time * band.GroupSpeed;
		float fan = position.x * band.FanK.x + position.y * band.FanK.y + band.FanPhase - time * band.FanSpeed;
		var (sin, cos) = MathF.SinCos( phase );
		var (groupSin, groupCos) = MathF.SinCos( group );
		var (fanSin, fanCos) = MathF.SinCos( fan );
		return ShapeCarrier( band, sin, cos, groupSin, groupCos, fanSin, fanCos );
	}

	private static float FrontStrength( float profile, float gradient, float envelope )
	{
		float lip = Math.Clamp( (profile - 0.58f) * 1.55f, 0f, 1f );
		float front = Math.Clamp( 0.85f - 0.55f * gradient, 0f, 1f );
		return lip * front * Math.Clamp( (envelope - 0.54f) * 2.6f, 0f, 1f );
	}

	// Passband taper avoids distant grid cells sampling a crest narrower than the cell.
	// Only rendering is filtered. Buoyancy always queries the continuous physical surface.
	private static float Resolved( float cellPhase, float pass, float stop )
	{
		float fade = Math.Clamp( (cellPhase - pass) / (stop - pass), 0f, 1f );
		return 1 - fade * fade * (3 - 2 * fade);
	}

	public float CrossingAt( Vector3 position, float time, float sharpness, out Vector2 travelDirection )
	{
		travelDirection = Vector2.Zero;
		if ( sharpness < 0.1f ) return 0;
		crossingFronts ??= new float[bands.Length];
		crossingDirections ??= new Vector2[bands.Length];
		float best = 0;
		for ( int i = 0; i < bands.Length; i++ )
		{
			var band = bands[i];
			crossingFronts[i] = 0;
			var carrier = EvaluateCarrier( band, position, time );
			float envelope = carrier.Envelope;
			if ( envelope <= 0.54f ) continue;
			float profile = Profile( carrier.Sin, carrier.Cos, sharpness * envelope, out float gradient );
			float front = FrontStrength( profile, gradient, envelope ) * MathF.Min( 1f, band.Amplitude * 2.1f );
			crossingFronts[i] = front;
			if ( front <= 0 ) continue;
			var direction = carrier.PhaseGradient.Normal;
			crossingDirections[i] = direction;
			for ( int j = 0; j < i; j++ )
			{
				// Only fronts moving in substantially different directions can collide.
				// Head-on fronts throw a fan along the crest line when their travel cancels.
				var other = crossingDirections[j];
				float alignment = other.x * direction.x + other.y * direction.y;
				float overlap = MathF.Min( front, crossingFronts[j] ) * Math.Clamp( (0.90f - alignment) * 3f, 0f, 1f );
				if ( overlap <= best ) continue;
				best = overlap;
				var resultant = direction * front + other * crossingFronts[j];
				travelDirection = resultant.LengthSquared > 0.01f * best * best
					? resultant.Normal : new Vector2( -other.y, other.x );
			}
		}
		return best * sharpness;
	}

	public float Height( Vector3 position, float time, float sharpness )
	{
		float sum = 0;
		foreach ( var band in bands )
		{
			var carrier = EvaluateCarrier( band, position, time );
			sum += band.Amplitude * carrier.Envelope * Profile( carrier.Sin, carrier.Cos, sharpness * carrier.Envelope, out _ );
		}
		return sum;
	}

	// Angle addition evaluates the carrier and its two modulations with trigonometry only along each axis.
	// All per-vertex work is arithmetic, including the analytic normal (product and chain rules).
	public void SampleGrid( float[] coordinates, Vector3 center, float time, float sharpness, Sample[] samples, Vector4[] columnPhases,
		float[] cellSpacings = null )
	{
		Array.Clear( samples );
		int width = coordinates.Length;
		if ( fanColumns is null || fanColumns.Length != width ) fanColumns = new Vector2[width];
		foreach ( var band in bands )
		{
			float phase = center.x * band.K.x + center.y * band.K.y + band.Phase - time * band.Speed;
			float group = center.x * band.GroupK.x + center.y * band.GroupK.y + band.GroupPhase - time * band.GroupSpeed;
			float fanPhase = center.x * band.FanK.x + center.y * band.FanK.y + band.FanPhase - time * band.FanSpeed;
			for ( int x = 0; x < width; x++ )
			{
				var (sin, cos) = MathF.SinCos( coordinates[x] * band.K.x + phase );
				var (groupSin, groupCos) = MathF.SinCos( coordinates[x] * band.GroupK.x + group );
				columnPhases[x] = new Vector4( sin, cos, groupSin, groupCos );
				var (fanSin, fanCos) = MathF.SinCos( coordinates[x] * band.FanK.x + fanPhase );
				fanColumns[x] = new Vector2( fanSin, fanCos );
			}
			for ( int y = 0; y < width; y++ )
			{
				// At horizon-scale cells even this train's longest wave is unresolved.
				// Skip whole rows where possible, and individual outer columns otherwise.
				float farSpacing = 6f / band.WaveNumber;
				if ( cellSpacings is not null && cellSpacings[y] >= farSpacing ) continue;
				var (rowSin, rowCos) = MathF.SinCos( coordinates[y] * band.K.y );
				var (groupRowSin, groupRowCos) = MathF.SinCos( coordinates[y] * band.GroupK.y );
				var (fanRowSin, fanRowCos) = MathF.SinCos( coordinates[y] * band.FanK.y );
				int row = y * width;
				for ( int x = 0; x < width; x++ )
				{
					if ( cellSpacings is not null && cellSpacings[x] >= farSpacing ) continue;
					var column = columnPhases[x];
					var fan = fanColumns[x];
					var carrier = ShapeCarrier( band, column.x * rowCos + column.y * rowSin, column.y * rowCos - column.x * rowSin,
						column.z * groupRowCos + column.w * groupRowSin, column.w * groupRowCos - column.z * groupRowSin,
						fan.x * fanRowCos + fan.y * fanRowSin, fan.y * fanRowCos - fan.x * fanRowSin );
					// Filter against the actual refracted wavenumber, including narrow lips.
					float resolvedBand = 1f;
					float resolvedLip = 1f;
					if ( cellSpacings is not null )
					{
						// Keep a conservative isotropic cutoff to suppress under-resolved
						// crossing crests even when they run along a stretched mesh strip.
						float cellPhase = Math.Max( 0.4f * band.WaveNumber * Math.Max( cellSpacings[x], cellSpacings[y] ),
							Math.Max( MathF.Abs( carrier.PhaseGradient.x ) * cellSpacings[x], MathF.Abs( carrier.PhaseGradient.y ) * cellSpacings[y] ) );
						resolvedBand = Resolved( cellPhase, 0.8f, 2.4f );
						resolvedLip = Resolved( cellPhase, 0.32f, 0.91f );
					}
					float envelope = carrier.Envelope;
					float profile = Profile( carrier.Sin, carrier.Cos, sharpness * envelope * resolvedLip, out float gradient );
					float amplitude = band.Amplitude * resolvedBand;
					float carrierSlope = amplitude * envelope * gradient;
					float groupSlope = amplitude * (2 * profile - carrier.Cos);
					ref var sample = ref samples[row + x];
					sample.Height += amplitude * envelope * profile;
					sample.Dx += carrierSlope * carrier.PhaseGradient.x + groupSlope * carrier.EnvelopeGradient.x;
					sample.Dy += carrierSlope * carrier.PhaseGradient.y + groupSlope * carrier.EnvelopeGradient.y;

					// High, locally pitching faces break only while the packet can support them.
					// Directional contributions carry across crossings without angle-wrap seams.
					float strength = FrontStrength( profile, gradient, envelope );
					// Foam lingers just astern of a passing lip. The wave phase decreases
					// at a fixed point, so the back is slightly behind its positive-phase peak.
					float tail = Math.Clamp( (carrier.Cos - 0.7f) * 3f, 0f, 1f ) * Math.Clamp( -carrier.Sin * 1.7f, 0f, 1f );
					strength += 0.24f * tail * Math.Clamp( (envelope - 0.58f) * 2.5f, 0f, 1f );
					if ( strength > 0f )
					{
						sample.Breaker += carrier.PhaseGradient.Normal * (strength * sharpness * resolvedLip * MathF.Min( 1f, band.Amplitude * 2.1f ));
					}
				}
			}
		}
		for ( int i = 0; i < samples.Length; i++ )
		{
			ref var breaker = ref samples[i].Breaker;
			if ( breaker.LengthSquared > 1f ) breaker = breaker.Normal;
		}
	}
}
