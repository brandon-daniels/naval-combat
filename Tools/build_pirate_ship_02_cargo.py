import bpy
import bmesh
import os
import sys
from mathutils import Matrix, Vector


source_path, output_dir = sys.argv[sys.argv.index("--") + 1:]
os.makedirs(output_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=source_path)

target = bpy.data.objects.get("ship.002")
if target is None:
	raise RuntimeError("ship.002 was not found in boats.fbx")

# Break the monolithic source mesh into its authored disconnected pieces. This
# lets the cloth and closed door be removed without touching the shared FBX.
bpy.ops.object.select_all(action="DESELECT")
target.select_set(True)
bpy.context.view_layer.objects.active = target
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.separate(type="LOOSE")
bpy.ops.object.mode_set(mode="OBJECT")

parts = list(bpy.context.selected_objects)


def world_bounds(obj):
	corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
	return (
		Vector(tuple(min(corner[axis] for corner in corners) for axis in range(3))),
		Vector(tuple(max(corner[axis] for corner in corners) for axis in range(3))),
	)


sail_parts = []
door_parts = []
hull_parts = []
for part in parts:
	minimum, maximum = world_bounds(part)
	span = maximum - minimum
	is_sail = minimum.z > 5.0 and maximum.z > 13.0 and (
		(span.x > 8.0 and span.y < 3.5) or
		(span.x < 2.0 and span.y > 8.0)
	)
	is_door = (
		minimum.y > 61.8 and maximum.y < 62.2 and
		minimum.x > -18.5 and maximum.x < -17.5 and
		minimum.z > 3.2 and maximum.z < 4.7
	)
	if is_sail:
		sail_parts.append(part)
	elif is_door:
		door_parts.append(part)
	else:
		hull_parts.append(part)

if len(sail_parts) != 3:
	raise RuntimeError(f"Expected three sail islands, found {len(sail_parts)}")

for part in door_parts:
	bpy.data.objects.remove(part, do_unlink=True)


def join_objects(objects, name):
	bpy.ops.object.select_all(action="DESELECT")
	for obj in objects:
		obj.select_set(True)
	bpy.context.view_layer.objects.active = objects[0]
	bpy.ops.object.join()
	objects[0].name = name
	return objects[0]


hull = join_objects(hull_parts, "Ship_Hull_And_Rigging")
sails = join_objects(sail_parts, "Ship_Sails")

# Keep the same practical pivot as the filtered ModelDoc resource: centered in
# X/Y and resting at Z=0. Blender exports metres; S&box imports them as metres.
source_origin = Vector((-17.3435, 56.6725, -0.629))
for obj in (hull, sails):
	obj.data.transform(obj.matrix_world)
	obj.matrix_world.identity()
	obj.data.transform(Matrix.Translation(-source_origin))

# Open the cabin-front doorway. In the source asset the door leaf is welded to
# the cabin shell, so remove only the forward-facing polygons inside the frame.
mesh_edit = bmesh.new()
mesh_edit.from_mesh(hull.data)
door_faces = []
for face in mesh_edit.faces:
	center = face.calc_center_median()
	if (
		abs(center.x) < 0.92 and 5.12 < center.y < 5.52 and
		3.02 < center.z < 5.18 and face.normal.y < -0.45
	):
		door_faces.append(face)
bmesh.ops.delete(mesh_edit, geom=door_faces, context="FACES")
mesh_edit.to_mesh(hull.data)
mesh_edit.free()
hull.data.update()

# Reuse a UV sample from the original deck so added architecture stays on the
# source palette material rather than introducing another material dependency.
uv_sample = Vector((0.5, 0.5))
if hull.data.uv_layers.active:
	uv_layer = hull.data.uv_layers.active.data
	candidates = []
	for polygon in hull.data.polygons:
		center = polygon.center
		if polygon.normal.z > 0.65 and 2.0 < center.z < 6.5:
			for loop_index in polygon.loop_indices:
				candidates.append(uv_layer[loop_index].uv.copy())
	if candidates:
		uv_sample = candidates[len(candidates) // 2]


def add_box(name, location, dimensions, collection, material, uv):
	bpy.ops.mesh.primitive_cube_add(location=location)
	obj = bpy.context.object
	obj.name = name
	obj.dimensions = dimensions
	bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
	if material:
		obj.data.materials.append(material)
	uv_layer = obj.data.uv_layers.new(name="UVMap")
	for datum in uv_layer.data:
		datum.uv = uv
	for existing in tuple(obj.users_collection):
		existing.objects.unlink(obj)
	collection.objects.link(obj)
	return obj


def add_stair_ramp(name, x_half, y_top, y_bottom, z_top, z_bottom, z_base, collection):
	vertices = [
		(-x_half, y_top, z_base), (x_half, y_top, z_base),
		(-x_half, y_bottom, z_base), (x_half, y_bottom, z_base),
		(-x_half, y_top, z_top), (x_half, y_top, z_top),
		(-x_half, y_bottom, z_bottom), (x_half, y_bottom, z_bottom),
	]
	faces = [
		(0, 2, 3, 1), (4, 5, 7, 6),
		(0, 1, 5, 4), (2, 6, 7, 3),
		(0, 4, 6, 2), (1, 3, 7, 5),
	]
	mesh = bpy.data.meshes.new(name + "Mesh")
	mesh.from_pydata(vertices, [], faces)
	mesh.update()
	obj = bpy.data.objects.new(name, mesh)
	collection.objects.link(obj)
	return obj


def add_deck_to_stair_ramp(name, x_half, y_start, y_slope, y_bottom, z_top, z_bottom, z_base, collection):
	"""One convex center lane: flat weather deck followed by the stair slope."""
	vertices = [
		(-x_half, y_start, z_base), (x_half, y_start, z_base),
		(-x_half, y_bottom, z_base), (x_half, y_bottom, z_base),
		(-x_half, y_start, z_top), (x_half, y_start, z_top),
		(-x_half, y_slope, z_top), (x_half, y_slope, z_top),
		(-x_half, y_bottom, z_bottom), (x_half, y_bottom, z_bottom),
	]
	faces = [
		(0, 2, 3, 1), (4, 5, 7, 6), (6, 7, 9, 8),
		(0, 1, 5, 4), (2, 8, 9, 3),
		(0, 4, 6, 8, 2), (1, 3, 9, 7, 5),
	]
	mesh = bpy.data.meshes.new(name + "Mesh")
	mesh.from_pydata(vertices, [], faces)
	mesh.update()
	obj = bpy.data.objects.new(name, mesh)
	collection.objects.link(obj)
	return obj


render_collection = bpy.data.collections.new("Cargo Interior Render")
bpy.context.scene.collection.children.link(render_collection)
collision_collection = bpy.data.collections.new("Cargo Interior Collision")
bpy.context.scene.collection.children.link(collision_collection)

material = hull.data.materials[0] if hull.data.materials else None
render_parts = [hull]
collision_parts = []

# Below-deck cargo room: a narrow room safely inside the curved hull. The player
# walks aft through the cabin door and down the stairs, then turns around the
# stairwell into the hold beneath the main deck.
boxes = [
	("CargoFloor", (0.0, 2.4, 1.79), (2.8, 10.8, 0.14)),
	("PortInnerWall", (-1.48, 2.4, 2.485), (0.14, 10.8, 1.53)),
	("StarboardInnerWall", (1.48, 2.4, 2.485), (0.14, 10.8, 1.53)),
	("ForeBulkhead", (0.0, -3.05, 2.485), (3.10, 0.14, 1.53)),
	("AftBulkhead", (0.0, 7.85, 2.485), (3.10, 0.14, 1.53)),
]

step_count = 9
visual_steps = []
for index in range(step_count):
	progress = index / (step_count - 1)
	y = 4.00 + progress * 3.50
	top = 3.39 - progress * 1.47
	height = top - 1.73
	visual_steps.append((f"Stair_{index + 1:02d}", (0.0, y, 1.73 + height * 0.5), (1.42, 0.48, height)))

# Door frame and low threshold make the entrance legible while leaving a clear
# 1.65 m passage through the original cabin front.
boxes.extend([
	# The landing overlaps the weather-deck collision and the stair ramp. This
	# removes the narrow collision seam that could catch the player at the door.
	("DoorFramePort", (-0.98, 5.31, 4.25), (0.18, 0.22, 2.45)),
	("DoorFrameStarboard", (0.98, 5.31, 4.25), (0.18, 0.22, 2.45)),
	("DoorFrameTop", (0.0, 5.31, 5.47), (2.15, 0.22, 0.18)),
])

# Hold walls visually meet the underside of the deck, but their collision caps
# sit lower. The clearance prevents a deck-walking capsule from catching an
# otherwise invisible upper edge while retaining player-height walls below.
hold_wall_collision = {
	"PortInnerWall": ((-1.48, 2.4, 2.235), (0.14, 10.8, 1.03)),
	"StarboardInnerWall": ((1.48, 2.4, 2.235), (0.14, 10.8, 1.03)),
	"ForeBulkhead": ((0.0, -3.05, 2.02), (3.10, 0.14, 0.60)),
	"AftBulkhead": ((0.0, 7.85, 2.02), (3.10, 0.14, 0.60)),
}

for name, location, dimensions in boxes:
	render_obj = add_box(name, location, dimensions, render_collection, material, uv_sample)
	render_parts.append(render_obj)
	if name == "DoorLanding" or name.startswith("DoorFrame"):
		continue
	collision_location, collision_dimensions = hold_wall_collision.get(name, (location, dimensions))
	collision_obj = add_box(f"UCX_{name}", collision_location, collision_dimensions, collision_collection, None, uv_sample)
	collision_parts.append(collision_obj)

for name, location, dimensions in visual_steps:
	render_parts.append(add_box(name, location, dimensions, render_collection, material, uv_sample))

collision_parts.append(add_deck_to_stair_ramp(
	"UCX_DeckToCargoRamp", 0.72, -5.80, 3.85, 7.72, 3.43, 1.86, 1.72, collision_collection
))

# A compound walkable collision shell. The exterior deck is split around the
# cabin opening; the room pieces above add the interior floor, walls and steps.
collision_shell = [
	("UCX_DeckForePort", (-1.77, -2.7, 3.34), (2.06, 6.2, 0.18)),
	("UCX_DeckForeStarboard", (1.77, -2.7, 3.34), (2.06, 6.2, 0.18)),
	("UCX_DeckMidPort", (-1.77, 2.8, 3.34), (2.06, 4.8, 0.18)),
	("UCX_DeckMidStarboard", (1.77, 2.8, 3.34), (2.06, 4.8, 0.18)),
	("UCX_DeckAftPort", (-1.85, 3.7, 3.34), (1.9, 6.6, 0.18)),
	("UCX_DeckAftStarboard", (1.85, 3.7, 3.34), (1.9, 6.6, 0.18)),
	("UCX_BowWall", (0.0, -6.0, 2.15), (4.5, 0.22, 2.4)),
]
for name, location, dimensions in collision_shell:
	collision_parts.append(add_box(name, location, dimensions, collision_collection, None, uv_sample))

# Export render hull, optional sails, and explicit compound collision as three
# assets. Keep the .blend source alongside them for future artistic iteration.
def export_fbx(path, objects):
	bpy.ops.object.select_all(action="DESELECT")
	for obj in objects:
		obj.select_set(True)
	bpy.context.view_layer.objects.active = objects[0]
	bpy.ops.export_scene.fbx(
		filepath=path,
		use_selection=True,
		apply_unit_scale=True,
		apply_scale_options="FBX_SCALE_ALL",
		axis_forward="-Z",
		axis_up="Y",
		add_leaf_bones=False,
		bake_anim=False,
		path_mode="AUTO",
	)


export_fbx(os.path.join(output_dir, "pirate_ship_02_cargo_hull.fbx"), render_parts)
export_fbx(os.path.join(output_dir, "pirate_ship_02_sails.fbx"), [sails])
try:
	export_fbx(os.path.join(output_dir, "pirate_ship_02_cargo_collision.fbx"), collision_parts)
except RuntimeError as error:
	if "Permission denied" not in str(error):
		raise
	print("Collision FBX is locked by the editor; retained the existing identical collision export")

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(output_dir, "pirate_ship_02_cargo.blend"))
print(f"Built cargo ship with {len(render_parts)} render elements and {len(collision_parts)} collision hulls")
