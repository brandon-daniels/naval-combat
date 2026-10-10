import bpy
import math
import os
import sys
from mathutils import Vector


blend_path, output_path = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.open_mainfile(filepath=blend_path)

collision_collection = bpy.data.collections.get("Cargo Interior Collision")
if collision_collection:
	collision_collection.hide_render = True

sails = bpy.data.objects.get("Ship_Sails")
if sails:
	sails.hide_render = True

# Include the optional sail mesh in the overview but leave the doorway and
# stairs readable from the port-front quarter.
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 1280
scene.render.resolution_y = 720
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.filepath = output_path
if scene.world is None:
	scene.world = bpy.data.worlds.new("Preview World")
scene.world.color = (0.035, 0.05, 0.08)

bpy.ops.object.camera_add(location=(8.5, -0.5, 7.0))
camera = bpy.context.object
scene.camera = camera


def point_at(obj, target):
	direction = Vector(target) - obj.location
	obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


point_at(camera, (0.0, 5.7, 2.8))
camera.data.lens = 45

bpy.ops.object.light_add(type="SUN", location=(4.0, -6.0, 16.0))
sun = bpy.context.object
sun.rotation_euler = (math.radians(25), math.radians(-20), math.radians(-35))
sun.data.energy = 3.0

bpy.ops.object.light_add(type="AREA", location=(-4.0, 2.0, 10.0))
area = bpy.context.object
area.data.energy = 1000
area.data.shape = "DISK"
area.data.size = 8.0
point_at(area, (0.0, 3.5, 2.0))

bpy.ops.render.render(write_still=True)
print(f"Rendered {output_path}")

# A second cutaway isolates the room so stair direction, floor extent, and
# bulkhead clearance can be checked without the exterior shell occluding it.
hull = bpy.data.objects.get("Ship_Hull_And_Rigging")
if hull:
	hull.hide_render = True
for cutaway_name in ("StarboardInnerWall", "AftBulkhead"):
	cutaway = bpy.data.objects.get(cutaway_name)
	if cutaway:
		cutaway.hide_render = True
camera.location = (7.5, -7.5, 6.5)
point_at(camera, (0.0, 1.2, 1.7))
root, extension = os.path.splitext(output_path)
scene.render.filepath = root + "_interior" + extension
bpy.ops.render.render(write_still=True)
print(f"Rendered {scene.render.filepath}")
