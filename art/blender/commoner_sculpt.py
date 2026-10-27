"""Builds the commoner's head, hair and clothes from the sculpted turnaround sheet
(art/blender/source/commoner_sheet.glb, a 3D lift of art/current/00_commoner_base.png:
four statues of him in one mesh) and puts them on the Warrior rig in place of the
procedural ones from build_armor.py.

  Commoner_Head.fbx    Commoner_Head (face, ears, neck, a scalp under the hair), Commoner_Hair and
                       Commoner_Collar (padding round the neck, shown under armour)
  Commoner_Body.fbx    tunic, team tabard, belt, trousers, boots, arms and hands
  Textures/Commoner.png    their shared colour atlas; alpha marks the tabard, which takes the team colour

Run after build_armor.py (it opens armor_sets.blend, swaps the Commoner_* objects in it
and saves it again, so menu_pose.py sees the new ones):
  /Applications/Blender.app/Contents/MacOS/Blender -b --python art/blender/commoner_sculpt.py [-- --no-render]

Steps: take the front statue, scale it to the rig and cut it into body / head / hair by
colour; decimate each to a game budget and bake the statue's colours onto the low meshes;
weight them with bone heat on a voxel copy, using a copy of the rig moved onto the
statue's joints (J); map every vertex into the rig's rest pose through the inverse of its
blended bone maps, which stretches the statue's shorter arms and squeezes its longer
torso to the rig's bone lengths; then close the right hand round the weapon grip.
"""
import bpy, bmesh, math, os, sys, time
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from mathutils.interpolate import poly_3d_calc

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.abspath(os.path.join(HERE, "..", ".."))
MODELS = os.path.join(GAME, "Prototype", "SwarmPrototype", "Resources", "SwarmModels")
TEXTURE = os.path.join(MODELS, "Textures", "Commoner.png")
SOURCE = os.path.join(HERE, "source", "commoner_sheet.glb")
BLEND = os.path.join(HERE, "armor_sets.blend")
PREVIEWS = os.path.join(HERE, "previews")
RENDER = "--no-render" not in sys.argv

HEIGHT = 2.75           # the statue's height on the rig (the procedural commoner's, hair included)
Y_SHIFT = 0.06          # statue back onto the rig's centre line
BUDGET = (11000, 3500, 1800)        # body, head, hair triangles after decimation (a scalp copies the hair)
TEX_RES = 2048
DENSITY = {'Body': 1.0, 'HeadHair': 1.8}        # texels per area against the body's

# Joints on the statue, left side (the right mirrors x), in rig units after scaling.
J = {'hip': (0.175, 0.03, 1.16), 'knee': (0.25, 0.07, 0.68), 'ankle': (0.33, 0.10, 0.022), 'toe': (0.35, -0.26, 0.022),
     'clav': (0.14, 0.04, 1.80), 'shoulder': (0.40, 0.10, 1.97), 'elbow': (0.485, 0.09, 1.66), 'wrist': (0.562, 0.014, 1.43),
     'knuckle': (0.555, -0.005, 1.25), 'fmid': (0.548, -0.02, 1.16), 'tip': (0.54, -0.03, 1.075),
     'thumb_mid': (0.48, -0.09, 1.30), 'thumb_tip': (0.47, -0.12, 1.20)}
SIDE = {'L': 1, 'R': -1}
NECK = ((0, 0.03, 2.17), (0, 0.01, 2.33))      # neck base and chin on the statue
HEAD_TOP = 2.75
SPINE_K = 0.83          # the statue's torso is longer: rig heights above the hips map up by 1/SPINE_K
HAND_SIZE = 1.2         # hands this much bigger on the rig than on the statue
HEAD_SIZE = 1.15        # a little bigger than the sheet's, as the armour sheets draw it; helmets fit it (build_armor HEAD_M)
ARM_THICK = 1.12        # arms get this much thicker as they are stretched to the rig's length
GRIP_R = 0.085          # weapon grip axis to the curled fingers' midline


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def mesh_array(me, attr, n=3, domain='vertices'):
    seq = getattr(me, domain)
    a = np.empty(len(seq) * n, np.float32)
    seq.foreach_get(attr, a)
    return a.reshape(-1, n)


def select_only(*objs):
    for o in bpy.context.scene.objects:
        o.select_set(o in objs)
    bpy.context.view_layer.objects.active = objs[0]


# ---------------------------------------------------------------- the statue

def load_statue():
    """Imports the sheet, keeps its front statue (the leftmost) scaled onto the rig."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=SOURCE)
    new = [o for o in bpy.data.objects if o not in before]
    ob = next(o for o in new if o.type == 'MESH')
    mw = ob.matrix_world.copy()
    ob.parent = None
    ob.data.transform(mw)
    ob.matrix_world.identity()
    for o in new:
        if o is not ob:
            bpy.data.objects.remove(o)
    me = ob.data
    fc = mesh_array(me, 'center', domain='polygons')
    xs = np.sort(fc[:, 0])
    gap = int(np.argmax(np.diff(xs[xs < np.percentile(xs, 35)])))      # first gap in the row of statues
    cut = (xs[gap] + xs[gap + 1]) / 2
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_center_median().x > cut], context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-7)     # glTF splits vertices at every UV seam
    bm.to_mesh(me)
    bm.free()
    co = mesh_array(me, 'co')
    z0, h = co[:, 2].min(), np.ptp(co[:, 2])
    cx = (co[:, 0].min() + co[:, 0].max()) / 2
    me.transform(Matrix.Translation((0, Y_SHIFT, 0)) @ Matrix.Scale(HEIGHT / h, 4) @ Matrix.Translation((-cx, 0, -z0)))
    ob.name = me.name = 'Statue'
    print("statue faces", len(me.polygons))
    return ob


def face_colours(ob):
    """Base colour at each face's centre UV, from the statue's texture at 2k."""
    me = ob.data
    img = next(n.image for n in me.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE'
               and n.image.colorspace_settings.name == 'sRGB')
    img.scale(2048, 2048)
    px = np.array(img.pixels[:], np.float32).reshape(2048, 2048, 4)
    uv = np.empty(len(me.loops) * 2, np.float32)
    me.uv_layers[0].data.foreach_get('uv', uv)
    uv = uv.reshape(-1, 2)
    start = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get('loop_start', start)
    fuv = (uv[start] + uv[start + 1] + uv[start + 2]) / 3
    ix = np.clip((fuv % 1) * 2048, 0, 2047).astype(int)
    return px[ix[:, 1], ix[:, 0], :3]


def classify(ob):
    """0 body, 1 head (everything above the collar that isn't cloth), 2 hair (the head's
    biggest patch of brown; the brows are small ones)."""
    col = face_colours(ob)
    r, g, b = col.T
    cloth = ((b - r) > 0.15) | (((r - g) < 0.12) & (r > 0.55))
    brown = (r < 0.62) & (g < 0.36) & (b < 0.24) & ((r - b) > 0.18)
    fc = mesh_array(ob.data, 'center', domain='polygons')
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.faces.ensure_lookup_table()

    def flood(seeds, allow):
        label = np.full(len(bm.faces), -1)
        sizes = []
        for s in seeds:
            if label[s] >= 0 or not allow[s]:
                continue
            k = len(sizes)
            label[s] = k
            stack, n = [s], 0
            while stack:
                f = bm.faces[stack.pop()]
                n += 1
                for v in f.verts:
                    for nf in v.link_faces:
                        i = nf.index
                        if label[i] < 0 and allow[i]:
                            label[i] = k
                            stack.append(i)
            sizes.append(n)
        return label, sizes

    head, _ = flood([int(np.argmax(fc[:, 2]))], ~cloth & (fc[:, 2] > 1.9))
    head = head == 0
    patches, sizes = flood(np.where(head & brown)[0], head & brown)
    cls = np.zeros(len(fc), np.int32)
    cls[head] = 1
    cls[patches == int(np.argmax(sizes))] = 2
    bm.free()
    # the eyes: the dark ovals on the front of the face
    eyes = []
    dark = head & (col.sum(1) < 0.4) & (fc[:, 1] < -0.1) & (fc[:, 2] > 2.35) & (fc[:, 2] < 2.6)
    for side in (1, -1):
        e = fc[dark & (fc[:, 0] * side > 0.03)]
        eyes.append(((e.min(0) + e.max(0)) / 2, (e.max(0) - e.min(0)) / 2))
    print("head faces", int(head.sum()), "hair faces", int((cls == 2).sum()),
          "eyes", [(c.round(3).tolist(), h.round(3).tolist()) for c, h in eyes])
    return cls, eyes


def split(statue, cls):
    """Decimates a copy of the whole statue in one piece (so the cuts between body, head
    and hair don't become dense borders), the head kept finer, then cuts it into Body_Lo
    and HeadHair_Lo (material 0 head, 1 hair)."""
    src = statue.data.materials[0]
    lo = bpy.data.objects.new('Statue_Lo', statue.data.copy())
    bpy.context.scene.collection.objects.link(lo)
    me = lo.data
    for _ in range(2):
        me.materials.append(src)
    me.polygons.foreach_set('material_index', cls)
    select_only(lo)
    # one class at a time: a decimate limited to a vertex group only collapses those
    # vertices, toward a face count for the whole mesh
    for k in range(3):
        counts = np.bincount([p.material_index for p in me.polygons], minlength=3)
        group = lo.vertex_groups.new(name='Collapse')
        group.add(sorted({v for p in me.polygons if p.material_index == k for v in p.vertices}), 1.0, 'REPLACE')
        mod = lo.modifiers.new('Decimate', 'DECIMATE')
        mod.ratio = (counts.sum() - counts[k] + BUDGET[k]) / counts.sum()
        mod.use_collapse_triangulate = True
        mod.vertex_group = 'Collapse'
        bpy.ops.object.modifier_apply(modifier='Decimate')
        lo.vertex_groups.clear()
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(60))
    parts = {}
    for name, keep in (('Body', (0,)), ('HeadHair', (1, 2))):
        part = bpy.data.objects.new(name + '_Lo', me.copy())
        part.data.name = name + '_Lo'
        bpy.context.scene.collection.objects.link(part)
        bm = bmesh.new()
        bm.from_mesh(part.data)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index not in keep], context='FACES')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
        for f in bm.faces:
            f.material_index = max(0, f.material_index - keep[0])
        bm.to_mesh(part.data)
        bm.free()
        parts[name] = part
        print(name, "faces", len(part.data.polygons))
    bpy.data.objects.remove(lo)
    return parts


def bake(statue, parts):
    """Repacks the statue's own UV islands (inherited through decimation) into one atlas,
    the head denser, and bakes the statue's base colour onto it."""
    lows = [parts[n] for n in ('Body', 'HeadHair')]
    select_only(*lows)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.average_islands_scale()
    bpy.ops.object.mode_set(mode='OBJECT')
    for name, lo in zip(('Body', 'HeadHair'), lows):
        uv = np.empty(len(lo.data.loops) * 2, np.float32)
        lo.data.uv_layers[0].data.foreach_get('uv', uv)
        lo.data.uv_layers[0].data.foreach_set('uv', uv * math.sqrt(DENSITY[name]))
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, margin=0.006)
    bpy.ops.object.mode_set(mode='OBJECT')
    img = bpy.data.images.new('Commoner', TEX_RES, TEX_RES, alpha=True)
    mat = bpy.data.materials.new('Commoner_Tex')
    mat.use_nodes = True
    nt = mat.node_tree
    tex = nt.nodes.new('ShaderNodeTexImage')
    tex.image = img
    nt.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.75
    nt.nodes.active = tex
    for lo in lows:
        for i in range(len(lo.data.materials)):
            lo.data.materials[i] = mat
    scene = bpy.context.scene
    engine = scene.render.engine
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 4
    bk = scene.render.bake
    bk.use_selected_to_active = True
    bk.cage_extrusion = 0.06
    bk.max_ray_distance = 0.2
    bk.margin = 16
    bk.margin_type = 'EXTEND'
    bk.use_clear = False
    for n in statue.data.materials[0].node_tree.nodes:
        if n.type == 'TEX_IMAGE':
            n.interpolation = 'Closest'          # blending across the sheet's island borders draws dark seams
    for lo in lows:
        select_only(lo, statue)
        bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'})
    scene.render.engine = engine
    return img, mat


def team_mask(img):
    """Alpha = how much of each texel is tabard blue; there the colour becomes a grey that
    UnitGear's TeamTint shader multiplies by the team's colour (blue comes out as it was)."""
    px = np.array(img.pixels[:], np.float32).reshape(TEX_RES, TEX_RES, 4)
    rgb = px[..., :3]
    hi, lo = rgb.max(-1), rgb.min(-1)
    sat = (hi - lo) / np.maximum(hi, 1e-4)
    hue = (4 + (rgb[..., 0] - rgb[..., 1]) / np.maximum(hi - lo, 1e-4)) / 6      # where blue is the largest channel
    blue = smoothstep(0.15, 0.3, sat) * smoothstep(0.5, 0.55, hue) * smoothstep(0.8, 0.74, hue) * (rgb[..., 2] >= hi)
    lum = rgb @ np.array([0.299, 0.587, 0.114])
    ref = np.median(lum[blue > 0.9])
    grey = np.clip(lum / ref * 0.72, 0, 1)[..., None]
    px[..., :3] = rgb * (1 - blue[..., None]) + grey * blue[..., None]
    px[..., 3] = blue
    out = bpy.data.images.new('Commoner_Team', TEX_RES, TEX_RES, alpha=True)
    out.pixels = px.ravel()
    out.filepath_raw = TEXTURE
    out.file_format = 'PNG'
    out.save()
    print("saved", TEXTURE, "tabard texels", int((blue > 0.5).sum()))


# ---------------------------------------------------------------- onto the rig

def jp(name, side):
    x, y, z = J[name]
    return Vector((x * SIDE[side], y, z))


def statue_bones(arm):
    """bone -> (head, tail on the statue, how it maps): 'limb' stretches along the bone,
    'arm' also thickens, the rest stay rigid at their own size."""
    rest = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in arm.data.bones}

    def spine(p):
        z = p.z + 0.002 if p.z < 1.158 else 1.16 + (p.z - 1.158) / SPINE_K
        return Vector((0, p.y, z))

    tri = {n: (spine(rest[n][0]), spine(rest[n][1]), 'limb') for n in ('Body', 'Hips', 'Abdomen', 'Torso')}
    tri['Neck'] = (Vector(NECK[0]), Vector(NECK[1]), 'limb')
    tri['Head'] = (Vector(NECK[1]), Vector((0, NECK[1][1], HEAD_TOP)), 'head')
    for s in 'LR':
        tri['UpperLeg.' + s] = (jp('hip', s), jp('knee', s), 'limb')
        tri['LowerLeg.' + s] = (jp('knee', s), jp('ankle', s), 'limb')
        tri['Foot.' + s] = (jp('ankle', s), jp('toe', s), 'foot')
        tri['Shoulder.' + s] = (jp('clav', s), jp('shoulder', s), 'limb')
        tri['UpperArm.' + s] = (jp('shoulder', s), jp('elbow', s), 'arm')
        tri['LowerArm.' + s] = (jp('elbow', s), jp('wrist', s), 'arm')
        tri['Fist.' + s] = (jp('wrist', s), jp('knuckle', s), 'hand')
        tri['Fist1.' + s] = (jp('knuckle', s), jp('fmid', s), 'hand')
        tri['Fist2.' + s] = (jp('fmid', s), jp('tip', s), 'hand')
        tri['Thumb1.' + s] = (jp('wrist', s), jp('thumb_mid', s), 'hand')
        tri['Thumb2.' + s] = (jp('thumb_mid', s), jp('thumb_tip', s), 'hand')
    return rest, tri


def bone_map(rest, tri, name):
    """Rig rest space -> statue space for one bone."""
    hr, tr = rest[name]
    ht, tt, mode = tri[name]
    dr = (tr - hr).normalized()
    if mode in ('limb', 'arm'):
        along = (tt - ht).length / (tr - hr).length
        across = 1 / ARM_THICK if mode == 'arm' else 1.0
        d = np.array(dr)
        S = Matrix((np.eye(3) * across + (along - across) * np.outer(d, d)).tolist()).to_4x4()
    else:
        S = Matrix.Scale({'head': 1 / HEAD_SIZE, 'hand': 1 / HAND_SIZE, 'foot': 1.0}[mode], 4)
    turn = dr.rotation_difference((tt - ht).normalized()).to_matrix().to_4x4()
    return Matrix.Translation(ht) @ turn @ S @ Matrix.Translation(-hr)


def heat_weights(arm, statue, tri, bones):
    """Bone heat on a voxel remesh of the statue (one closed surface, so the solve can't
    fail on loose pieces), with a copy of the rig moved onto the statue's joints. Returns
    the proxy's BVH, triangles, vertex positions and per-vertex weights over `bones`."""
    scene = bpy.context.scene
    me = statue.data.copy()
    proxy = bpy.data.objects.new('Proxy', me)
    scene.collection.objects.link(proxy)
    me.remesh_voxel_size = 0.02
    select_only(proxy)
    bpy.ops.object.voxel_remesh()
    rig = arm.copy()
    rig.data = arm.data.copy()
    rig.animation_data_clear()
    scene.collection.objects.link(rig)
    select_only(rig)
    bpy.ops.object.mode_set(mode='EDIT')
    for eb in rig.data.edit_bones:
        eb.use_connect = False
    for eb in rig.data.edit_bones:
        eb.use_deform = eb.name in tri
        if eb.use_deform:
            h, t, mode = tri[eb.name]
            lift = Vector((0, 0, 0.08)) if mode == 'foot' else Vector()     # inside the boot for the solve
            eb.head, eb.tail = h + lift, t + lift
    bpy.ops.object.mode_set(mode='OBJECT')
    select_only(rig, proxy)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    index = {n: i for i, n in enumerate(bones)}
    names = {g.index: g.name for g in proxy.vertex_groups}
    W = np.zeros((len(me.vertices), len(bones)), np.float32)
    for v in me.vertices:
        for g in v.groups:
            if names[g.group] in index:
                W[v.index, index[names[g.group]]] = g.weight
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    tris = [tuple(v.index for v in f.verts) for f in bm.faces]
    cos = [v.co.copy() for v in bm.verts]
    bm.free()
    bpy.data.objects.remove(proxy)
    bpy.data.objects.remove(rig)
    print("heat: proxy verts", len(cos), "unweighted", int((W.sum(1) < 1e-4).sum()))
    return BVHTree.FromPolygons(cos, tris), tris, cos, W


def sample(tree, tris, cos, W, co):
    out = np.zeros((len(co), W.shape[1]), np.float32)
    for i, p in enumerate(co):
        loc, _, fi, _ = tree.find_nearest(Vector(p))
        tri = tris[fi]
        for vid, b in zip(tri, poly_3d_calc([cos[j] for j in tri], loc)):
            out[i] += b * W[vid]
    return out


def texel_colours(me, img):
    px = np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], 4)
    uv = np.empty(len(me.loops) * 2, np.float32)
    me.uv_layers[0].data.foreach_get('uv', uv)
    ix = np.clip((uv.reshape(-1, 2) * img.size).astype(int), 0, np.array(img.size) - 1)
    vi = np.empty(len(me.loops), np.int32)
    me.loops.foreach_get('vertex_index', vi)
    acc = np.zeros((len(me.vertices), 3))
    n = np.zeros(len(me.vertices))
    np.add.at(acc, vi, px[ix[:, 1], ix[:, 0], :3])
    np.add.at(n, vi, 1)
    return acc / np.maximum(n, 1)[:, None]


def body_overrides(co, W, colours, bones):
    """Hands rigid on their Fist bone; the tunic skirt and tabard below the belt on the
    torso, handing up to 35% to the thighs toward the hem (as build_armor's hem_weights)."""
    index = {n: i for i, n in enumerate(bones)}
    for s in 'LR':
        w = np.array(jp('wrist', s))
        reach = jp('tip', s) - jp('wrist', s)
        d = np.array(reach.normalized())
        along = (co - w) @ d
        off = np.linalg.norm(co - w - np.outer(along, d), axis=1)
        near = (off < 0.2) & (co[:, 0] * SIDE[s] > 0.4) & (along < reach.length + 0.05)
        f = smoothstep(-0.04, 0.03, along) * near
        W *= (1 - f)[:, None]
        W[:, index['Fist.' + s]] += f
    skirt = (colours[:, 2] > 0.42) & (co[:, 2] < 1.5) & (np.abs(co[:, 0]) < 0.42)
    torso = [index[n] for n in ('Body', 'Hips', 'Abdomen')]
    T = np.zeros_like(W)
    T[:, torso] = W[:, torso]
    T[:, index['Hips']] += 1e-3
    T /= T.sum(1, keepdims=True)
    share = 0.35 * smoothstep(1.45, 1.0, co[:, 2])
    left = smoothstep(-0.4, 0.4, co[:, 0])
    T *= (1 - share)[:, None]
    T[:, index['UpperLeg.L']] += share * left
    T[:, index['UpperLeg.R']] += share * (1 - left)
    W[skirt] = T[skirt]


def top4(W):
    W = W.copy()
    np.put_along_axis(W, np.argsort(-W, 1)[:, 4:], 0, 1)
    return W / np.maximum(W.sum(1, keepdims=True), 1e-6)


def fit(ob, W, maps, bones, arm):
    """Moves each vertex through the inverse of its blended bone maps into the rig's rest
    pose and skins it there."""
    co = np.c_[mesh_array(ob.data, 'co'), np.ones(len(ob.data.vertices))]
    B = np.einsum('vb,bij->vij', W, maps)
    rest = np.einsum('vij,vj->vi', np.linalg.inv(B), co)[:, :3]
    ob.data.vertices.foreach_set('co', rest.ravel().astype(np.float32))
    ob.data.update()
    ob.vertex_groups.clear()
    groups = {}
    for i, row in enumerate(W):
        for b in np.nonzero(row > 0.01)[0]:
            name = bones[b]
            g = groups.get(name) or groups.setdefault(name, ob.vertex_groups.new(name=name))
            g.add([i], float(row[b]), 'REPLACE')
    ob.parent = arm
    ob.modifiers.new('Armature', 'ARMATURE').object = arm


def weapon_grip(arm):
    """The weapon grip's centre and axis in the rest pose, in the right hand's frame as the
    Idle_Weapon clip holds it (build_armor's fist is built around the same)."""
    sw = bpy.data.objects['Warrior_Sword']
    arm.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    loc, rot, _ = sw.matrix_world.decompose()
    weapon = Matrix.Translation(loc) @ rot.to_matrix().to_4x4()
    arm.data.pose_position = 'POSE'
    ad = arm.animation_data or arm.animation_data_create()
    act = next(a for a in bpy.data.actions if a.name.endswith('|Idle_Weapon'))
    ad.action = act
    if hasattr(ad, 'action_slot') and ad.action_slot is None and len(act.slots):
        ad.action_slot = act.slots[0]
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()
    pose = {pb.name: pb.matrix @ pb.bone.matrix_local.inverted() for pb in arm.pose.bones}
    back = pose['Fist.R'].inverted() @ pose['Weapon.R']
    return back @ weapon.translation, (back.to_3x3() @ weapon.to_3x3() @ Vector((0, 0, 1))).normalized()


def fist(ob, arm):
    """Curls the right hand's fingers round the weapon grip: the hand is lifted until the
    fingers' midline runs GRIP_R from the grip axis, then everything past the knuckle line
    is wrapped round that axis, arc length kept (a bend deform)."""
    C, u = weapon_grip(arm)
    C, u = np.array(C), np.array(u)
    bone = arm.data.bones['Fist.R']
    wrist = np.array(bone.head_local)
    e1 = np.array(bone.tail_local - bone.head_local)
    e1 -= u * (e1 @ u)
    e1 /= np.linalg.norm(e1)
    e2 = np.cross(u, e1)
    if (C - wrist) @ e2 < 0:
        e2 = -e2                                      # from the back of the hand toward the grip
    gi = ob.vertex_groups['Fist.R'].index
    idx = np.array([v.index for v in ob.data.vertices if any(g.group == gi and g.weight > 0.5 for g in v.groups)])
    co = mesh_array(ob.data, 'co')[idx]
    rel = co - C
    band = (rel @ e1 > -0.06) & (rel @ e1 < 0) & (rel @ u > -0.05)
    n = rel[band] @ e2
    lift = -GRIP_R - (n.min() + n.max()) / 2
    co += np.outer(np.clip(((co - wrist) @ e1) / 0.12, 0, 1) * lift, e2)
    rel = co - C
    s, n, a = rel @ e1, rel @ e2, rel @ u
    bend = s > 0                                      # fingers, and the thumb round the front of the grip
    th = np.minimum(s[bend] / GRIP_R, math.radians(250))
    r = -n[bend]
    co[bend] = C + np.outer(-r * np.cos(th), e2) + np.outer(r * np.sin(th), e1) + np.outer(a[bend], u)
    full = mesh_array(ob.data, 'co')
    full[idx] = co
    ob.data.vertices.foreach_set('co', full.ravel())
    ob.data.update()
    print("fist: hand verts", len(idx), "curled", int(bend.sum()))


# ---------------------------------------------------------------- head pieces

def split_head(ob):
    """Head (material 0) and hair (1) as two objects; their shared border keeps its
    vertices in both, so the scalp can close the head exactly."""
    hair = ob.copy()
    hair.data = ob.data.copy()
    bpy.context.scene.collection.objects.link(hair)
    for o, drop in ((ob, 1), (hair, 0)):
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index == drop], context='FACES')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
        for f in bm.faces:
            f.material_index = 0
        bm.to_mesh(o.data)
        bm.free()
        while len(o.data.materials) > 1:
            o.data.materials.pop()
    ob.name = ob.data.name = 'Commoner_Head'
    hair.name = hair.data.name = 'Commoner_Hair'
    return ob, hair


def scalp(head, hair):
    """A close crop under the hair, so the head is whole when a helmet hides the hair: the
    hair's own faces pulled in to an ellipsoid round the skull, its border left where it
    meets the face. Joined to the head; it keeps the hair's texture."""
    me = hair.data
    co = mesh_array(me, 'co')
    bm = bmesh.new()
    bm.from_mesh(me)
    rim = np.array([v.is_boundary for v in bm.verts])
    bm.free()
    edge = co[rim]
    c = np.array([0.0, edge[:, 1].mean(), edge[:, 2].mean()])
    radii = np.array([np.abs(edge[:, 0]).max(), np.ptp(edge[:, 1]) / 2, co[:, 2].max() - 0.09 - c[2]]) * 0.97
    d = co - c
    k = 1 / np.sqrt(((d / radii) ** 2).sum(1))         # scale that puts the point on the ellipsoid
    pulled = c + d * np.minimum(k, 1)[:, None]          # everything outside the cap onto it
    pulled[rim] = co[rim]
    tmp = bpy.data.meshes.new('Scalp')
    tmp.from_pydata(pulled.tolist(), [], [tuple(p.vertices) for p in me.polygons])
    for i, p in enumerate(me.polygons):
        tmp.polygons[i].use_smooth = True
    tmp.uv_layers.new(name=me.uv_layers[0].name)
    uv = np.empty(len(me.loops) * 2, np.float32)
    me.uv_layers[0].data.foreach_get('uv', uv)
    tmp.uv_layers[0].data.foreach_set('uv', uv)
    tmp.materials.append(me.materials[0])
    bm = bmesh.new()
    bm.from_mesh(head.data)
    bm.from_mesh(tmp)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
    deform = bm.verts.layers.deform.verify()
    g = head.vertex_groups['Head'].index
    for v in bm.verts:
        if not v[deform]:
            v[deform][g] = 1.0                       # the scalp rides the head like the hair
    bm.to_mesh(head.data)
    bm.free()
    bpy.data.meshes.remove(tmp)
    print("scalp: centre", c.round(3), "radii", radii.round(3))


COLLAR = ((0.0, 1.06), (0.05, 1.32), (0.12, 1.55))      # drop below the neck rim, radius against the rim's


def collar(head, body, img, arm):
    """A padded collar flaring from the neck rim down under the armour's neck opening,
    which is wider than this neck: its own mesh, Commoner_Collar, that UnitGear shows only
    while armour is worn. Belt leather from the atlas."""
    co = mesh_array(head.data, 'co')
    bm = bmesh.new()
    bm.from_mesh(head.data)
    rim = sorted({v.index for e in bm.edges if e.is_boundary and max(v.co.z for v in e.verts) < 2.2 for v in e.verts})
    bm.free()
    c = co[rim].mean(0)
    rim = sorted(rim, key=lambda i: math.atan2(co[i, 1] - c[1], co[i, 0] - c[0]))
    names = {g.index: g.name for g in head.vertex_groups}
    rim_w = [{names[g.group]: g.weight for g in head.data.vertices[i].groups} for i in rim]
    colours = texel_colours(body.data, img)
    bco = mesh_array(body.data, 'co')
    belt = np.where((np.abs(colours - (0.41, 0.24, 0.145)).sum(1) < 0.08) & (np.abs(bco[:, 0]) < 0.2) & (bco[:, 1] < 0))[0]
    belt = belt[np.argmin(np.abs(bco[belt, 0]))]
    uv = next(Vector(body.data.uv_layers[0].data[l.index].uv) for l in body.data.loops if l.vertex_index == belt)
    me = bpy.data.meshes.new('Commoner_Collar')
    verts = []
    for drop, scale in COLLAR:
        for i in rim:
            p = co[i]
            verts.append((c[0] + (p[0] - c[0]) * scale, c[1] + (p[1] - c[1]) * scale, p[2] - drop))
    n = len(rim)
    faces = [(k * n + i, k * n + (i + 1) % n, (k + 1) * n + (i + 1) % n, (k + 1) * n + i)
             for k in range(len(COLLAR) - 1) for i in range(n)]
    me.from_pydata(verts, [], faces)
    me.uv_layers.new(name='UVMap')
    for l in me.uv_layers[0].data:
        l.uv = uv
    me.materials.append(head.data.materials[0])
    bm = bmesh.new()
    bm.from_mesh(me)
    out = sum(f.normal.dot(f.calc_center_median() - Vector(c)) for f in bm.faces)
    if out < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    for f in bm.faces:
        f.smooth = True
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new('Commoner_Collar', me)
    bpy.context.scene.collection.objects.link(ob)
    for k in range(len(COLLAR)):
        f = k / (len(COLLAR) - 1)
        for i, w in enumerate(rim_w):
            mixed = {name: x * (1 - f) for name, x in w.items()}
            mixed['Torso'] = mixed.get('Torso', 0.0) + f
            for name, x in mixed.items():
                if x > 0.01:
                    (ob.vertex_groups.get(name) or ob.vertex_groups.new(name=name)).add([k * n + i], x, 'REPLACE')
    ob.parent = arm
    ob.modifiers.new('Armature', 'ARMATURE').object = arm
    print("collar: rim verts", n)
    return ob


def neck_skirt(head):
    """Extends the neck 0.12 down inside the collar (the tunic's, or the armour's), so no
    gap opens at the collar line when the head turns."""
    bm = bmesh.new()
    bm.from_mesh(head.data)
    low = [e for e in bm.edges if e.is_boundary and max(v.co.z for v in e.verts) < 2.2]
    centre = sum((v.co for e in low for v in e.verts), Vector()) / (2 * len(low))
    out = bmesh.ops.extrude_edge_only(bm, edges=low)
    for v in out['geom']:
        if isinstance(v, bmesh.types.BMVert):
            v.co = centre + (v.co - centre) * 0.88
            v.co.z -= 0.12
    bm.normal_update()
    bm.to_mesh(head.data)
    bm.free()
    print("neck: rim edges", len(low))


LID_GRID = (6, 5)       # vertices across and down each eyelid


def blink_key(head, eyes, to_rest, img):
    """Shape key 'Blink' on a skin-coloured lid over each eye (the eyes are painted, too few
    vertices to squash). At rest each lid is folded into a line along the top of its eye,
    just under the skin; Blink drops it over the eye a little proud of the face, its lower
    edge dark like lashes. HomeUnitPreview plays it now and then."""
    me = head.data
    co = mesh_array(me, 'co')
    tree = BVHTree.FromPolygons([Vector(c) for c in co], [tuple(p.vertices) for p in me.polygons])
    uv = np.empty(len(me.loops) * 2, np.float32)
    me.uv_layers[0].data.foreach_get('uv', uv)
    uv = uv.reshape(-1, 2)
    loop_of = {}
    for li, l in enumerate(me.loops):
        loop_of.setdefault(l.vertex_index, li)

    def uv_near(p):
        return Vector(uv[loop_of[int(np.argmin(np.linalg.norm(co - p, axis=1)))]])

    colour = texel_colours(me, img)
    lum = colour.sum(1)

    def uv_skin(p):
        """The UV of the vertex round p whose colour is the median of the skin there."""
        near = np.where((np.linalg.norm(co - p, axis=1) < 0.07) & (lum > 1.2) & (colour[:, 0] > colour[:, 2] + 0.15))[0]
        return Vector(uv[loop_of[int(near[np.argmin(np.abs(lum[near] - np.median(lum[near])))])]])

    def surface(x, z):
        hit, _, _, _ = tree.ray_cast(Vector((x, -1.0, z)), Vector((0, 1, 0)))
        return hit.y if hit else None

    bm = bmesh.new()
    bm.from_mesh(me)
    deform = bm.verts.layers.deform.verify()
    uvl = bm.loops.layers.uv.active
    g = head.vertex_groups['Head'].index
    shut = {}
    nx, nz = LID_GRID
    for centre, half in eyes:
        c = to_rest @ Vector(centre)
        hw, hh = half[0] * 1.2 + 0.005, half[2] * 1.2 + 0.005
        skin = uv_skin(np.array(c))
        lash = uv_near(np.array(c))
        top = c.z + hh
        rows = []
        for j in range(nz):
            row = []
            z = top - 2 * hh * j / (nz - 1)
            span = hw * math.sqrt(max(0.06, 1 - ((z - c.z) / hh) ** 2))       # the eye's oval
            for i in range(nx):
                x = c.x + span * (2 * i / (nx - 1) - 1)
                y_top, y = surface(x, top), surface(x, z)
                if y_top is None or y is None:
                    break
                v = bm.verts.new((x, y_top + 0.008, top))        # folded away under the skin
                v[deform][g] = 1.0
                shut[v] = Vector((x, min(y, y_top) - 0.004 - 0.003 * math.sin(math.pi * i / (nx - 1)), z))
                row.append(v)
            rows.append(row)
        for j in range(nz - 1):
            for i in range(min(len(rows[j]), len(rows[j + 1])) - 1):
                f = bm.faces.new((rows[j][i], rows[j + 1][i], rows[j + 1][i + 1], rows[j][i + 1]))     # facing out
                f.smooth = True
                for loop in f.loops:
                    last = loop.vert in rows[-1]
                    loop[uvl].uv = lash if last else skin
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    head.shape_key_add(name='Basis', from_mix=False)
    key = head.shape_key_add(name='Blink', from_mix=False)
    key.value = 0.0                                    # open; new keys start at 1
    n0 = len(co)
    for k, (v, p) in enumerate(shut.items()):
        key.data[n0 + k].co = p
    print("blink: lid verts", len(shut))


# ---------------------------------------------------------------- output

def team_material(mat):
    """Points the material at the saved atlas and tints it like UnitGear's TeamTint shader:
    colour x lerp(1, team, alpha), team blue here."""
    img = bpy.data.images.load(TEXTURE, check_existing=False)
    img.alpha_mode = 'CHANNEL_PACKED'
    nt = mat.node_tree
    tex = next(n for n in nt.nodes if n.type == 'TEX_IMAGE')
    tex.image = img
    mix = nt.nodes.new('ShaderNodeMix')
    mix.data_type = 'RGBA'
    mix.blend_type = 'MULTIPLY'
    mix.inputs['B'].default_value = (0.011, 0.105, 0.72, 1)     # (0.1, 0.33, 0.85) ** 2.2
    nt.links.new(tex.outputs['Alpha'], mix.inputs['Factor'])
    nt.links.new(tex.outputs['Color'], mix.inputs['A'])
    nt.links.new(mix.outputs['Result'], nt.nodes['Principled BSDF'].inputs['Base Color'])


def export(arm, objects, filename):
    select_only(arm, *objects)
    bpy.context.view_layer.objects.active = arm
    path = os.path.join(MODELS, filename)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                             bake_anim=False, mesh_smooth_type='OFF', path_mode='STRIP', use_mesh_modifiers=True,
                             use_armature_deform_only=False)
    print("EXPORTED", path)


def preview(arm, objects, img):
    """Front, three-quarter, side and back in Idle_Weapon with the pitchfork, and the face."""
    scene = bpy.context.scene
    shown = set(objects) | {bpy.data.objects['Weapon_Pitchfork']}
    hidden = {o: o.hide_render for o in bpy.data.objects}
    for o in bpy.data.objects:
        o.hide_render = o.type == 'MESH' and o not in shown
    ad = arm.animation_data or arm.animation_data_create()
    act = next(a for a in bpy.data.actions if a.name.endswith('|Idle_Weapon'))
    ad.action = act
    scene.frame_set(1)
    engine = scene.render.engine
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'TEXTURE'
    scene.display.shading.show_backface_culling = True
    tex = next(n for n in objects[0].data.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE')
    atlas, tex.image = tex.image, img           # workbench draws the image node, not the team tint
    cam = bpy.data.objects.new('PreviewCam', bpy.data.cameras.new('PreviewCam'))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = 'ORTHO'
    os.makedirs(PREVIEWS, exist_ok=True)
    for name, yaw, focus, scale in (('front', 0, (0, 0, 1.4), 3.0), ('quarter', 40, (0, 0, 1.4), 3.0),
                                    ('side', 90, (0, 0, 1.4), 3.0), ('back', 180, (0, 0, 1.4), 3.0),
                                    ('face', 25, (0, -0.05, 2.32), 0.8)):
        d = Vector((math.sin(math.radians(yaw)), -math.cos(math.radians(yaw)), 0))
        cam.location = Vector(focus) + d * 8
        cam.rotation_euler = (math.radians(90), 0, math.radians(yaw))
        cam.data.ortho_scale = scale
        scene.render.resolution_x, scene.render.resolution_y = (420, 560) if name != 'face' else (480, 480)
        scene.render.filepath = os.path.join(PREVIEWS, f"Commoner_{name}.png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)
    tex.image = atlas
    scene.render.engine = engine
    ad.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    for o, h in hidden.items():
        o.hide_render = h


def main():
    t0 = time.time()
    bpy.ops.wm.open_mainfile(filepath=BLEND)
    arm = bpy.data.objects['CharacterArmature']
    for old in ('Commoner_Body', 'Commoner_Head', 'Commoner_Hair', 'Commoner_Collar'):     # build_armor's, or an earlier run's
        if old in bpy.data.objects:
            me = bpy.data.objects[old].data
            bpy.data.objects.remove(bpy.data.objects[old])
            bpy.data.meshes.remove(me)
    for m in [m for m in bpy.data.materials if m.name.startswith('Commoner_Tex')]:
        bpy.data.materials.remove(m)          # UnitGear styles the FBX material by this exact name
    for i in [i for i in bpy.data.images if i.name.startswith(('Commoner', 'Image_')) and not i.users]:
        bpy.data.images.remove(i)
    statue = load_statue()
    cls, eyes = classify(statue)
    parts = split(statue, cls)
    img, mat = bake(statue, parts)
    team_mask(img)
    rest, tri = statue_bones(arm)
    bones = list(tri)
    maps = np.array([np.array(bone_map(rest, tri, n)) for n in bones])
    tree, tris, cos, PW = heat_weights(arm, statue, tri, bones)
    source = statue.data.materials[0]
    bpy.data.objects.remove(statue)
    for n in source.node_tree.nodes:                 # the sheet's 8k maps, so they aren't saved in the .blend
        if n.type == 'TEX_IMAGE' and n.image:
            bpy.data.images.remove(n.image)
    bpy.data.materials.remove(source)
    index = {n: i for i, n in enumerate(bones)}
    for name in ('Body', 'HeadHair'):
        lo = parts[name]
        co = mesh_array(lo.data, 'co')
        W = sample(tree, tris, cos, PW, co)
        if name == 'Body':
            body_overrides(co, W, texel_colours(lo.data, img), bones)
        else:
            hair = np.zeros(len(co), bool)
            for p in lo.data.polygons:
                if p.material_index == 1:
                    hair[list(p.vertices)] = True
            W[hair] = 0
            W[hair, index['Head']] = 1
        fit(lo, top4(W), maps, bones, arm)
    body = parts['Body']
    fist(body, arm)
    body.name = body.data.name = 'Commoner_Body'
    head, hair = split_head(parts['HeadHair'])
    scalp(head, hair)
    neck = collar(head, body, img, arm)
    neck_skirt(head)
    blink_key(head, eyes, bone_map(rest, tri, 'Head').inverted(), img)
    team_material(mat)
    for o in (body, head, hair, neck):
        o.data.calc_loop_triangles()
        print("MESH %-14s tris=%5d" % (o.name, len(o.data.loop_triangles)))
    export(arm, [head, hair, neck], 'Commoner_Head.fbx')
    export(arm, [body], 'Commoner_Body.fbx')
    if RENDER:
        preview(arm, [body, head, hair], img)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
    print("SAVED", BLEND, "in %.0fs" % (time.time() - t0))


main()
