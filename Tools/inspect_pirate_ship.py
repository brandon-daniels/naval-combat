import bpy
import sys
from mathutils import Vector
from collections import deque


fbx_path = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx_path)

for obj in sorted(bpy.context.scene.objects, key=lambda item: item.name):
	if obj.type != "MESH":
		continue
	world_corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
	mins = tuple(min(corner[index] for corner in world_corners) for index in range(3))
	maxs = tuple(max(corner[index] for corner in world_corners) for index in range(3))
	print(
		f"OBJECT {obj.name!r} verts={len(obj.data.vertices)} "
		f"min={tuple(round(value, 3) for value in mins)} "
		f"max={tuple(round(value, 3) for value in maxs)} "
		f"materials={[slot.material.name if slot.material else None for slot in obj.material_slots]}"
	)
	if obj.name == "ship.002":
		neighbors = [set() for _ in obj.data.vertices]
		for edge in obj.data.edges:
			a, b = edge.vertices
			neighbors[a].add(b)
			neighbors[b].add(a)
		remaining = set(range(len(obj.data.vertices)))
		parts = []
		while remaining:
			seed = remaining.pop()
			component = {seed}
			queue = deque([seed])
			while queue:
				current = queue.popleft()
				for neighbor in neighbors[current]:
					if neighbor in remaining:
						remaining.remove(neighbor)
						component.add(neighbor)
						queue.append(neighbor)
			coords = [obj.matrix_world @ obj.data.vertices[index].co for index in component]
			part_min = tuple(min(co[index] for co in coords) for index in range(3))
			part_max = tuple(max(co[index] for co in coords) for index in range(3))
			parts.append((len(component), part_min, part_max))
		for number, (count, part_min, part_max) in enumerate(sorted(parts, reverse=True), 1):
			print(f"PART {number:02d} verts={count} min={tuple(round(v,3) for v in part_min)} max={tuple(round(v,3) for v in part_max)}")
