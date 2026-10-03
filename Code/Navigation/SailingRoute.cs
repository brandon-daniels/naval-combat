using System;
using System.Collections.Generic;

namespace NavalCombat;

/// <summary>Wind-aware visibility graph around conservative shoreline circles.</summary>
public sealed class SailingRoute
{
	public readonly record struct Obstacle( Vector3 Center, float Radius );
	public List<Vector3> Points { get; } = new();
	public List<Obstacle> Islands { get; } = new();
	public const float TackAngle = 120; // 60 degrees off the source of the wind; margin above the no-go zone.

	public void ReadIslands( Scene scene, float clearance )
	{
		Islands.Clear();
		foreach ( var surface in scene.GetAllComponents<IslandSurface>().Where( x => x.Active ) )
		{
			var scale = surface.WorldScale;
			float radius = Math.Abs( surface.Radius ) * Math.Max( Math.Abs( surface.OuterScale ), Math.Abs( surface.InnerScale ) ) * 1.16f
				* Math.Max( Math.Abs( scale.x ), Math.Abs( scale.y ) ) + clearance;
			var circle = new Obstacle( surface.WorldPosition.WithZ( 0 ), radius );
			if ( Islands.Any( x => (x.Center - circle.Center).Length + radius <= x.Radius + 1 ) ) continue;
			Islands.RemoveAll( x => (x.Center - circle.Center).Length + x.Radius <= radius );
			Islands.Add( circle );
		}
	}

	public bool Clear( Vector3 start, Vector3 end )
	{
		start = start.WithZ( 0 );
		end = end.WithZ( 0 );
		var delta = end - start;
		float lengthSquared = Vector3.Dot( delta, delta );
		foreach ( var island in Islands )
		{
			float t = lengthSquared > 0.01f ? Math.Clamp( Vector3.Dot( island.Center - start, delta ) / lengthSquared, 0, 1 ) : 0;
			if ( (start + delta * t - island.Center).Length < island.Radius ) return false;
		}
		return true;
	}

	public bool Plan( Vector3 start, Vector3 goal, float windHeading )
	{
		Points.Clear();
		start = start.WithZ( 0 );
		goal = goal.WithZ( 0 );
		if ( !Clear( start, start ) || !Clear( goal, goal ) ) return false;
		var nodes = new List<Vector3> { start, goal };
		foreach ( var island in Islands )
		{
			for ( int i = 0; i < 16; i++ )
			{
				var point = island.Center + Rotation.FromYaw( i * 22.5f ).Forward * (island.Radius + 350);
				if ( Clear( point, point ) ) nodes.Add( point );
			}
		}
		var cost = new float[nodes.Count];
		var previous = new int[nodes.Count];
		var via = new Vector3?[nodes.Count];
		var visited = new bool[nodes.Count];
		for ( int i = 0; i < nodes.Count; i++ ) { cost[i] = float.PositiveInfinity; previous[i] = -1; }
		cost[0] = 0;
		for ( int step = 0; step < nodes.Count; step++ )
		{
			int current = -1;
			for ( int i = 0; i < nodes.Count; i++ )
				if ( !visited[i] && (current < 0 || cost[i] < cost[current]) ) current = i;
			if ( current < 0 || float.IsPositiveInfinity( cost[current] ) ) break;
			if ( current == 1 ) break;
			visited[current] = true;
			for ( int next = 0; next < nodes.Count; next++ )
			{
				if ( visited[next] || next == current ) continue;
				float edge = Connection( nodes[current], nodes[next], windHeading, out var tack );
				if ( cost[current] + edge >= cost[next] ) continue;
				cost[next] = cost[current] + edge;
				previous[next] = current;
				via[next] = tack;
			}
		}
		if ( previous[1] < 0 ) return false;
		int index = 1;
		while ( index != 0 )
		{
			Points.Add( nodes[index] );
			if ( via[index].HasValue ) Points.Add( via[index].Value );
			index = previous[index];
		}
		Points.Reverse();
		return true;
	}

	private float Connection( Vector3 start, Vector3 end, float wind, out Vector3? tack )
	{
		tack = null;
		var delta = end - start;
		float alignment = Vector3.Dot( delta.Normal, Rotation.FromYaw( wind ).Forward );
		if ( alignment >= -0.5f - 0.001f )
			return Clear( start, end ) ? delta.Length / Math.Max( 0.1f, SailRig.HeadingPower( alignment ) ) : float.PositiveInfinity;
		float best = float.PositiveInfinity;
		for ( int side = -1; side <= 1; side += 2 )
		{
			var a = Rotation.FromYaw( wind + side * TackAngle ).Forward;
			var b = Rotation.FromYaw( wind - side * TackAngle ).Forward;
			float cross = a.x * b.y - a.y * b.x;
			float first = (delta.x * b.y - delta.y * b.x) / cross;
			float second = (a.x * delta.y - a.y * delta.x) / cross;
			if ( first < 0 || second < 0 ) continue;
			var middle = start + a * first;
			if ( !Clear( start, middle ) || !Clear( middle, end ) ) continue;
			float value = (first + second) / SailRig.HeadingPower( -0.5f ) + 600; // Cost of a crewed tack.
			if ( value >= best ) continue;
			best = value;
			tack = middle;
		}
		return best;
	}
}
