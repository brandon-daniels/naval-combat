"""Build an original faceted coaster. Blender --background --python this_file.

Model coordinates: +Y bow, Z up, deck 3.34. Gameplay uses scale 48.8,
yaw -90 and Z -90. No vendor mesh or texture is used.
"""
import bpy
import bmesh
import math
import os
from mathutils import Vector, Matrix

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'Assets', 'models', 'reference_ship')
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
render, collision, sails = [], [], []
colors = {'ivory': (0.73, .72, .57, 1), 'trim': (.88, .86, .69, 1),
          'deck': (.56, .51, .36, 1), 'dark': (.24, .23, .17, 1),
          'rope': (.38, .36, .25, 1), 'canvas': (.85, .81, .64, 1)}
mats = {}
for name, color in colors.items():
    m = bpy.data.materials.new(name)
    m.diffuse_color = color
    m.use_nodes = True
    m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = color
    m.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .85
    mats[name] = m
    with open(os.path.join(OUT, name + '.vmat'), 'w') as f:
        f.write('"Layer0"\n{\n "shader" "shaders/complex.shader"\n "g_flModelTintAmount" "1"\n'
                ' "TextureColor" "materials/default/default_color.tga"\n'
                ' "TextureNormal" "materials/default/default_normal.tga"\n'
                ' "TextureRoughness" "materials/default/default_rough.tga"\n'
                f' "g_vColorTint" "[{color[0]} {color[1]} {color[2]} 0]"\n}}\n')

def mesh(name, verts, faces, mat='ivory', solid=False, group=None):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.update()
    bm = bmesh.new(); bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(data); bm.free()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mats[mat])
    (render if group is None else group).append(obj)
    if solid:
        copy = obj.copy(); copy.data = obj.data.copy(); copy.name = 'UCX_' + name
        bpy.context.collection.objects.link(copy); collision.append(copy)
    return obj

def box(name, center, size, mat='ivory', solid=False):
    x,y,z = center; a,b,c = [s/2 for s in size]
    return mesh(name, [(x+i*a,y+j*b,z+k*c) for i,j,k in
                [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]],
                [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],mat,solid)

def beam(name, a, b, radius, mat='trim', solid=False, sides=8):
    a,b=Vector(a),Vector(b); delta=b-a
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=radius, depth=delta.length, location=(a+b)/2)
    obj=bpy.context.object; obj.name=name
    obj.rotation_euler=delta.to_track_quat('Z','Y').to_euler()
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.data.materials.append(mats[mat]); render.append(obj)
    if solid:
        copy=obj.copy();copy.data=obj.data.copy();copy.name='UCX_'+name
        bpy.context.collection.objects.link(copy);collision.append(copy)
    return obj

# Each wall section is a separate convex slab: the hull is hollow, never a
# single convex enclosure. Broad flat facets retain the reference silhouette.
stations=[(-9,1.95),(-7.5,2.75),(-5,3.05),(0,3.2),(4,2.8),(7,1.7),(8.7,.12)]
for i,((y0,w0),(y1,w1)) in enumerate(zip(stations,stations[1:])):
    for side in [-1,1]:
        for level,(z0,z1,f0,f1) in enumerate([(0,.95,.55,.80),(.95,2.25,.80,1),(2.25,3.34,1,1)]):
            p=[(side*w0*f0,y0,z0),(side*w1*f0,y1,z0),(side*w1*f1,y1,z1),(side*w0*f1,y0,z1)]
            q=[(x-side*.16,y,z) for x,y,z in p]
            mesh(f'Hull_{i}_{side}_{level}',p+q,[(0,1,2),(0,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],solid=True)
        beam(f'Gunwale_{i}_{side}',(side*w0,y0,3.87),(side*w1,y1,3.87),.085,solid=True)
        beam(f'Sheer_{i}_{side}',(side*w0,y0,3.34),(side*w1,y1,3.34),.09)
        for y,w in [(y0,w0),((y0+y1)/2,(w0+w1)/2)]:
            beam('Rail stanchion',(side*w,y,3.34),(side*w,y,3.87),.045)
    # Tapered solid deck strips. Central opening remains clear for stair/headroom.
    intervals=[(y0,y1)]
    cuts=sorted(set([y0,y1]+[p for p in [-5.7,.5] if y0<p<y1]))
    for lo,hi in zip(cuts,cuts[1:]):
        wl=w0+(w1-w0)*(lo-y0)/(y1-y0); wh=w0+(w1-w0)*(hi-y0)/(y1-y0)
        gap=.95 if lo>=-5.7 and hi<=.5 else 0
        for side in [-1,1]:
            xy=[(side*gap,lo),(side*wl,lo),(side*wh,hi),(side*gap,hi)]
            verts=[(x,y,z) for z in [3.18,3.34] for x,y in xy]
            mesh('Deck',verts,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],'deck',True)
for end,(y,w) in enumerate([stations[0],stations[-1]]):
    for z0,z1,f0,f1 in [(0,.95,.55,.80),(.95,2.25,.80,1),(2.25,3.34,1,1)]:
        p=[(-w*f0,y,z0),(w*f0,y,z0),(w*f1,y,z1),(-w*f1,y,z1)]
        mesh('End wall',p+[(x,y+.12*(1 if end==0 else -1),z) for x,y,z in p],
             [(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],solid=True)
for (y0,w0),(y1,w1) in zip(stations,stations[1:]):
    mesh('Keel floor',[(x,y,z) for z in [0,.12] for x,y in [(-w0*.55,y0),(w0*.55,y0),(w1*.55,y1),(-w1*.55,y1)]],
         [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],solid=True)
box('Hold floor',(0,-1.15,.82),(3.5,12.4,.20),'deck',True)
for side in [-1,1]:
    box('Hold lining',(side*1.83,-1.15,2.05),(.16,12.4,2.55),'ivory',True)
    for y in [-6.8,-4,-1,2,4.8]:
        box('Interior rib',(side*1.72,y,2.0),(.12,.16,2.2),'trim')
box('Hold aft bulkhead',(0,-7.35,2.05),(3.5,.15,2.55),'ivory',True)
box('Hold fore bulkhead',(0,5.05,2.05),(3.5,.15,2.55),'ivory',True)
# 12 shallow treads, 1.9m wide, 4.8m run. Matching smooth convex ramp avoids
# character-controller snagging while retaining visible stair nosings.
for i in range(12):
    top=3.34-(i+1)*(3.34-.92)/12
    box(f'Cargo step {i+1:02}',(0,-5.7+(i+.5)*.4,top-.07),(1.88,.4,.14),'deck')
    beam('Step nosing',(-.93,-5.7+i*.4,top),(.93,-5.7+i*.4,top),.025)
ramp=mesh('Cargo ramp',[(-.94,-5.7,.72),(.94,-5.7,.72),(.94,-.9,.72),(-.94,-.9,.72),
                       (-.94,-5.7,3.34),(.94,-5.7,3.34),(.94,-.9,.92),(-.94,-.9,.92)],
          [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],group=collision)
for side in [-1,1]:
    beam('Stair handrail',(side*.88,-5.7,4.05),(side*.88,-.9,1.63),.038)
    beam('Hatch coaming',(side*1.01,-5.65,3.4),(side*1.01,.5,3.4),.07)
# Raised stern house, broad roof and small slatted windows from the reference.
box('Stern house',(0,-7.65,4.0),(4.1,2.5,1.32),'ivory',True)
box('Stern roof',(0,-7.65,4.72),(4.42,2.75,.16),'trim',True)
for side in [-1,1]:
    for y in [-8.35,-7.85,-7.35,-6.85]:
        box('Stern window',(side*2.061,y,4.08),(.025,.32,.52),'dark')
        box('Window mullion',(side*2.08,y,4.08),(.025,.035,.52),'trim')
    beam('Quarterdeck rail',(side*2.12,-8.95,5.18),(side*2.12,-6.35,5.18),.055)
    for y in [-8.95,-7.65,-6.35]: beam('Quarterdeck post',(side*2.12,y,4.8),(side*2.12,y,5.18),.045)
beam('Stern rail',(-2.12,-8.95,5.18),(2.12,-8.95,5.18),.055)
beam('Mast',(0,1.65,.95),(0,1.65,14.0),.11,solid=True)
beam('Mast collar',(0,1.65,3.34),(0,1.65,3.62),.22)
beam('Upper yard',(-1.35,1.65,13.55),(1.35,1.65,13.55),.06)
beam('Main yard',(-3.4,1.65,9.2),(3.4,1.65,9.2),.07)
beam('Bowsprit',(0,7.3,3.6),(0,12.0,4.25),.065)
beam('Forestay',(0,1.65,13.85),(0,12,4.25),.018,'rope',sides=5)
for side in [-1,1]:
    beam('Shroud',(0,1.65,12.8),(side*2.9,-2,3.85),.018,'rope',sides=5)
# A visible wheel at the accessible main-deck helm, ahead of the stern house.
beam('Helm pedestal',(1.65,-5.95,3.34),(1.65,-5.95,4.2),.10,'dark')
for i in range(10):
    a=i*math.tau/10;b=(i+1)*math.tau/10
    p=(1.65+math.cos(a)*.42,-5.95,4.32+math.sin(a)*.42)
    q=(1.65+math.cos(b)*.42,-5.95,4.32+math.sin(b)*.42)
    beam('Wheel rim',p,q,.045,'trim')
    beam('Wheel spoke',(1.65,-5.95,4.32),p,.025,'dark')
# Cloth is independently exported and furled/trimmed by existing SailRig.
verts=[]
for z in range(7):
    for x in range(9):
        u=x/8;v=z/6
        verts.append(((u-.5)*6.4,1.65+.45*math.sin(math.pi*u)*math.sin(math.pi*v),9.1-v*4.5))
faces=[]
for z in range(6):
    for x in range(8):
        a=z*9+x;faces.extend([(a,a+1,a+10,a+9),(a+9,a+10,a+1,a)])
mesh('Square sail',verts,faces,'canvas',group=sails)

def export(name,objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects: obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,name+'.fbx'),use_selection=True,
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y',
        add_leaf_bones=False,bake_anim=False,path_mode='AUTO')

# FBX's Source coordinate conversion rotates Blender +Y to engine -X.
# Pre-rotate the export only so ModelDoc stays +Y-forward as documented.
for obj in collision:
    obj.data.materials.clear()
    obj.data.materials.append(mats['ivory'])
rotation=Matrix.Rotation(-math.pi/2,4,'Z')
for obj in render+collision+sails: obj.data.transform(rotation)
# Use stable material paths; avoid Blender dotted mesh names becoming materials.
for obj in render+collision+sails:
    obj.name=obj.name.replace(' ', '_').replace('.', '_')
    obj.data.name=obj.name
export('reference_ship',render);export('reference_ship_collision',collision);export('reference_ship_sails',sails)
for obj in render+collision+sails: obj.data.transform(rotation.inverted())
header='<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->\n'
remaps=', '.join('{ from = "'+n+'" to = "models/reference_ship/'+n+'.vmat" }' for n in mats)
for name in ['reference_ship','reference_ship_sails']:
    phys='{ _class = "PhysicsShapeList" children = [{ _class = "PhysicsHullFile" filename = "models/reference_ship/reference_ship_collision.fbx" import_scale = 1.0 parent_bone = "" surface_prop = "wood" collision_tags = "solid" faceMergeAngle = 20.0 maxHullVertices = 32 import_mode = "HullPerElement" }] },' if name=='reference_ship' else ''
    with open(os.path.join(OUT,name+'.vmdl'),'w') as f:
        f.write(header+'{ rootNode = { _class = "RootNode" children = [ '
                '{ _class = "MaterialGroupList" children = [{ _class = "DefaultMaterialGroup" remaps = ['+remaps+'] use_global_default = false }] },'+phys+
                '{ _class = "RenderMeshList" children = [{ _class = "RenderMeshFile" filename = "models/reference_ship/'+name+'.fbx" import_scale = 1.0 import_translation = [0,0,0] import_rotation = [0,0,0] }] } ] } }')
for obj in collision+sails: obj.hide_render=True;obj.hide_set(True)
bpy.ops.object.select_all(action='DESELECT')
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'reference_ship.blend'))

# Reproducible beauty and hatch previews, kept outside the shipped asset folder.
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=1400;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('Studio');scene.world.color=(.035,.045,.055)
scene.view_settings.view_transform='AgX'
bpy.ops.object.camera_add(location=(22,28,20));camera=bpy.context.object;scene.camera=camera
camera.data.type='ORTHO';camera.data.ortho_scale=28
def aim(obj,target):obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
aim(camera,(0,1,6))
bpy.ops.object.light_add(type='AREA',location=(3,6,20));bpy.context.object.data.energy=2500;bpy.context.object.data.shape='DISK';bpy.context.object.data.size=12
bpy.ops.object.light_add(type='SUN',location=(-5,-4,16));bpy.context.object.data.energy=2;aim(bpy.context.object,(0,0,0))
scene.render.filepath=os.path.join(ROOT,'docs','reference_ship_preview.png');bpy.ops.render.render(write_still=True)
camera.location=(7,-10,11);aim(camera,(0,-2,2));camera.data.ortho_scale=12
scene.render.filepath=os.path.join(ROOT,'docs','reference_ship_hold.png');bpy.ops.render.render(write_still=True)
print(f'REFERENCE SHIP: {len(render)} visual elements, {len(collision)} convex collision pieces. Hold clear height 2.26m; stairs 1.88m wide.')
