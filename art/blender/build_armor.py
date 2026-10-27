"""Builds the Commoner armor sets (leather / iron / gilded) and the two shields
as skinned meshes on the Warrior.fbx armature, then exports one FBX per set into
Resources/SwarmModels and saves armor_sets.blend next to this script.

Run headless:
  /Applications/Blender.app/Contents/MacOS/Blender -b --python art/blender/build_armor.py [-- --no-render]

Units: Blender units as in Warrior.fbx (body 2.85 tall; the game scales it to
2.1 m). Z up, character front -Y, wearer's left +X. Every piece is built around
the bone it is weighted to, so Unity can remap the skinned bones by name onto a
live Warrior instance (see UnitGear.cs).
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.abspath(os.path.join(HERE, "..", ".."))
MODELS = os.path.join(GAME, "Prototype", "SwarmPrototype", "Resources", "SwarmModels")
WARRIOR = os.path.join(MODELS, "Warrior.fbx")
PREVIEWS = os.path.join(HERE, "previews")
BLEND_OUT = os.path.join(HERE, "armor_sets.blend")
RENDER = "--no-render" not in sys.argv

D = math.radians
FRONT = Vector((0, -1, 0))
BACK = Vector((0, 1, 0))
UP = Vector((0, 0, 1))
DOWN = Vector((0, 0, -1))
LEFT = Vector((1, 0, 0))   # wearer's left

# Material name -> viewport/base color. Unity restyles these by name in UnitGear.
MATERIALS = [
    ("Leather_Brown", (0.64, 0.36, 0.17, 1)),
    ("Leather_Dark", (0.38, 0.20, 0.10, 1)),
    ("Metal_Iron", (0.80, 0.82, 0.86, 1)),
    ("Metal_Gold", (1.00, 0.72, 0.16, 1)),
    ("Metal_Dark", (0.46, 0.48, 0.53, 1)),
    ("Team_Blue", (0.13, 0.36, 0.86, 1)),
    ("Wood_Brown", (0.64, 0.41, 0.20, 1)),
]
MAT = {name: i for i, (name, _) in enumerate(MATERIALS)}

# ---------------------------------------------------------------- geometry helpers

def frame(origin, z_dir=UP, x_dir=FRONT):
    """Matrix whose local z is z_dir and local x is x_dir projected off z."""
    z = Vector(z_dir).normalized()
    x = Vector(x_dir)
    x = x - z * x.dot(z)
    if x.length < 1e-4:
        x = UP - z * UP.dot(z)
        if x.length < 1e-4:
            x = LEFT - z * LEFT.dot(z)
    x.normalize()
    y = z.cross(x)
    o = Vector(origin)
    return Matrix(((x.x, y.x, z.x, o.x), (x.y, y.y, z.y, o.y), (x.z, y.z, z.z, o.z), (0, 0, 0, 1)))


def P(t, rx, ry=None, a0=None, a1=None, cx=0.0, cy=0.0):
    return dict(t=t, rx=rx, ry=rx if ry is None else ry, a0=a0, a1=a1, cx=cx, cy=cy)


def shell(profile, thickness):
    """Outer profile followed by the inner profile, for a closed lathe with wall thickness."""
    inner = [P(p['t'], max(p['rx'] - thickness, 0.002), max(p['ry'] - thickness, 0.002), p['a0'], p['a1'], p['cx'], p['cy'])
             for p in reversed(profile)]
    return profile + inner


def lathe(bm, profile, M, seg, mat, arc=None, closed=False, caps=False, cap_first=False, cap_last=False, offset=0.0):
    """Revolves `profile` (list of P) around local z of M. `arc=(a0,a1)` in degrees
    builds a partial revolve with `seg` segments across it; 0 degrees is local +x."""
    full = arc is None
    rings = []
    for p in profile:
        if p['rx'] <= 1e-6 and p['ry'] <= 1e-6:
            rings.append([bm.verts.new(M @ Vector((p['cx'], p['cy'], p['t'])))])
            continue
        a0 = p['a0'] if p['a0'] is not None else (0 if full else arc[0])
        a1 = p['a1'] if p['a1'] is not None else (360 if full else arc[1])
        count = seg if full else seg + 1
        vs = []
        for i in range(count):
            a = D(a0 + offset + (a1 - a0) * i / seg)
            vs.append(bm.verts.new(M @ Vector((p['cx'] + p['rx'] * math.cos(a), p['cy'] + p['ry'] * math.sin(a), p['t']))))
        rings.append(vs)
    faces = []

    def bridge(r0, r1):
        if len(r0) == 1 and len(r1) == 1:
            return
        n = max(len(r0), len(r1))
        steps = n if full else n - 1
        for i in range(steps):
            j = (i + 1) % n
            if len(r0) == 1:
                faces.append(bm.faces.new((r0[0], r1[i], r1[j])))
            elif len(r1) == 1:
                faces.append(bm.faces.new((r0[i], r0[j], r1[0])))
            else:
                faces.append(bm.faces.new((r0[i], r0[j], r1[j], r1[i])))

    for k in range(len(rings) - 1):
        bridge(rings[k], rings[k + 1])
    if closed:
        bridge(rings[-1], rings[0])
    if caps and not full:
        col0 = [r[0] for r in rings if len(r) > 1]
        col1 = [r[-1] for r in rings if len(r) > 1]
        if len(col0) > 2:
            faces.append(bm.faces.new(col0))
        if len(col1) > 2:
            faces.append(bm.faces.new(list(reversed(col1))))
    if cap_first and len(rings[0]) > 2:
        faces.append(bm.faces.new(list(reversed(rings[0]))))
    if cap_last and len(rings[-1]) > 2:
        faces.append(bm.faces.new(rings[-1]))
    for f in faces:
        f.material_index = mat
    if caps and not full:
        fan = [f for f in faces if len(f.verts) > 4]
        if fan:
            bmesh.ops.triangulate(bm, faces=fan)
    return faces


def plate(bm, pts, thickness, M, mat, z0=0.0):
    """Polygon `pts` (local xy) extruded from local z0 to z0+thickness."""
    bot = [bm.verts.new(M @ Vector((x, y, z0))) for x, y in pts]
    top = [bm.verts.new(M @ Vector((x, y, z0 + thickness))) for x, y in pts]
    faces = [bm.faces.new(list(reversed(bot))), bm.faces.new(top)]
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        faces.append(bm.faces.new((bot[i], bot[j], top[j], top[i])))
    for f in faces:
        f.material_index = mat
    return faces


def rect(w, h):
    return [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)]


def box(bm, M, size, mat):
    sx, sy, sz = size
    return plate(bm, rect(sx, sy), sz, M, mat, z0=-sz / 2)


def band(bm, M, t0, t1, rx, ry, thickness, seg, mat, offset=0.0, arc=None):
    """A ring (strap / rim) from t0 to t1 along local z."""
    return lathe(bm, shell([P(t0, rx, ry), P(t1, rx, ry)], thickness), M, seg, mat, arc=arc, closed=True, caps=arc is not None, offset=offset)


def rivet(bm, M, r=0.024, h=0.028, mat=MAT['Metal_Dark']):
    """Small dome whose base sits on local z=0 of M, pointing along local z."""
    return lathe(bm, [P(0, r), P(h * 0.6, r * 0.9), P(h, 0)], M, 6, mat, cap_first=True)


def ring_point(M, rx, ry, t, angle_deg):
    a = D(angle_deg)
    return M @ Vector((rx * math.cos(a), ry * math.sin(a), t))


def ring_normal(M, angle_deg):
    a = D(angle_deg)
    return (M.to_3x3() @ Vector((math.cos(a), math.sin(a), 0))).normalized()


def scale_pts(pts, s):
    return [(x * s, y * s) for x, y in pts]


# ---------------------------------------------------------------- pieces

class Piece:
    """One exported mesh object. Parts are separate bmeshes, each rigid to a bone."""

    def __init__(self, name):
        self.name = name
        self.parts = []

    def part(self, bone, bevel=0.012):
        bm = bmesh.new()
        self.parts.append((bm, bone, bevel))
        return bm

    def mirrored(self, name):
        q = Piece(name)
        for bm, bone, bevel in self.parts:
            bm2 = bm.copy()
            bmesh.ops.scale(bm2, vec=(-1, 1, 1), verts=bm2.verts[:])
            bmesh.ops.reverse_faces(bm2, faces=bm2.faces[:])
            q.parts.append((bm2, swap_side(bone), bevel))
        return q

    def build(self, armature, collection):
        me = bpy.data.meshes.new(self.name)
        ob = bpy.data.objects.new(self.name, me)
        collection.objects.link(ob)
        final = bmesh.new()
        layer = final.verts.layers.int.new('bone')
        bones = []
        for bm, bone, bevel in self.parts:
            bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
            if bevel > 0:
                sharp = [e for e in bm.edges if e.is_manifold and e.calc_length() > 0.03 and e.calc_face_angle(0) > D(50)]
                if sharp:
                    bmesh.ops.bevel(bm, geom=sharp, offset=bevel, segments=2, affect='EDGES', clamp_overlap=True, loop_slide=True)
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
            sharp = [e for e in bm.edges if e.is_manifold and e.calc_face_angle(0) > D(62)]
            if sharp:
                bmesh.ops.split_edges(bm, edges=sharp)
            for f in bm.faces:
                f.smooth = True
            tmp = bpy.data.meshes.new('tmp')
            bm.to_mesh(tmp)
            if bone not in bones:
                bones.append(bone)
            bi = bones.index(bone)
            before = len(final.verts)
            final.from_mesh(tmp)
            final.verts.ensure_lookup_table()
            for v in final.verts[before:]:
                v[layer] = bi
            bpy.data.meshes.remove(tmp)
            bm.free()
        # Only used materials go on the mesh, in first-use order.
        used = []
        for f in final.faces:
            if f.material_index not in used:
                used.append(f.material_index)
        for f in final.faces:
            f.material_index = used.index(f.material_index)
        bone_of_vert = [v[layer] for v in final.verts]
        final.to_mesh(me)
        final.free()
        groups = {}
        for i, bi in enumerate(bone_of_vert):
            groups.setdefault(bi, []).append(i)
        for mi in used:
            me.materials.append(bpy.data.materials[MATERIALS[mi][0]])
        for bi, idx in groups.items():
            ob.vertex_groups.new(name=bones[bi]).add(idx, 1.0, 'REPLACE')
        ob.parent = armature
        ob.modifiers.new('Armature', 'ARMATURE').object = armature
        return ob


def swap_side(bone):
    if bone.endswith('.L'):
        return bone[:-2] + '.R'
    if bone.endswith('.R'):
        return bone[:-2] + '.L'
    return bone


BONES = {}


def bone_frame(name, t=0.0, x_dir=FRONT):
    head, tail = BONES[name]
    d = (tail - head).normalized()
    return frame(head + d * t, d, x_dir)


class Style:
    def __init__(self, tier):
        self.tier = tier
        self.T = tier.capitalize()
        self.metal = MAT['Metal_Gold'] if tier == 'gilded' else MAT['Metal_Iron']
        self.plate_seg = 10 if tier == 'leather' else 8
        self.shell_mat = MAT['Leather_Brown'] if tier == 'leather' else self.metal
        self.trim = MAT['Leather_Dark'] if tier == 'leather' else MAT['Leather_Brown']
        self.rivet = MAT['Metal_Dark'] if tier != 'gilded' else MAT['Metal_Gold']

    def name(self, part):
        return f"Armor_{self.T}_{part}"


# --- torso

def chest(s):
    pc = Piece(s.name('Chest'))
    bm = pc.part('Torso')
    M = frame((0, 0.02, 0))
    leather = s.tier == 'leather'
    slim = 0.012 if leather else 0.0
    prof = [P(1.43, 0.375 - slim, 0.345 - slim), P(1.50, 0.335 - slim, 0.305 - slim), P(1.62, 0.35 - slim, 0.32 - slim),
            P(1.80, 0.375 - slim, 0.345 - slim), P(1.95, 0.35 - slim, 0.31 - slim), P(2.03, 0.27, 0.25)]
    lathe(bm, shell(prof, 0.03), M, 12, s.shell_mat, closed=True, offset=15)
    if leather:
        # Shoulder straps over the top, a dark belt at the hem, rivets at the strap ends.
        for sx in (-0.19, 0.19):
            box(bm, frame((sx, 0.02, 2.035), UP, FRONT), (0.56, 0.1, 0.04), MAT['Leather_Dark'])
            for fy in (-0.355, 0.395):
                rivet(bm, frame((sx, fy, 1.94), FRONT if fy < 0 else BACK), mat=s.rivet)
        band(bm, M, 1.44, 1.50, 0.375 - slim + 0.008, 0.345 - slim + 0.008, 0.02, 12, MAT['Leather_Dark'], offset=15)
        for ang in (90, 270):
            rivet(bm, frame(ring_point(M, 0.37, 0.34, 1.47, ang), ring_normal(M, ang)), mat=s.rivet)
    else:
        band(bm, M, 1.43, 1.49, 0.385, 0.355, 0.02, 12, s.metal, offset=15)
        band(bm, M, 1.99, 2.04, 0.285, 0.265, 0.02, 12, s.metal, offset=15)
        if s.tier == 'gilded':
            diamond = [(0, 0.16), (0.11, 0), (0, -0.16), (-0.11, 0)]
            plate(bm, diamond, 0.045, frame((0, -0.32, 1.79), FRONT, LEFT), s.metal)
        else:
            box(bm, frame((0, -0.335, 1.78), FRONT, UP), (0.42, 0.035, 0.035), s.metal)
        for sx in (-0.2, 0.2):
            box(bm, frame((sx, 0.02, 2.045), UP, FRONT), (0.46, 0.09, 0.03), MAT['Leather_Brown'])
    return pc


def waist(s):
    pc = Piece(s.name('Waist'))
    bm = pc.part('Hips')
    M = frame((0, 0.03, 0))
    leather = s.tier == 'leather'
    lathe(bm, shell([P(1.30, 0.345, 0.315), P(1.42, 0.345, 0.315)], 0.03), M, 16, s.shell_mat, closed=True, offset=11.25)
    if leather:
        box(bm, frame((0, -0.335, 1.36), FRONT, LEFT), (0.1, 0.08, 0.03), MAT['Metal_Dark'])
    else:
        for ang in (30, -30):
            rivet(bm, frame(ring_point(M, 0.345, 0.315, 1.36, ang), ring_normal(M, ang)), mat=s.rivet)
    # Tassets hang from the belt as curved cone segments; the body's own skirt shows between them.
    top = P(1.33, 0.36, 0.33)
    if leather:
        front = [top, P(1.02, 0.45, 0.42)]
        lathe(bm, shell(front, 0.025), M, 3, s.shell_mat, arc=(-24, 24), closed=True, caps=True)
    else:
        front = [P(1.33, 0.36, 0.33, -27, 27), P(1.10, 0.43, 0.40, -25, 25), P(0.93, 0.50, 0.47, -4, 4)]
        lathe(bm, shell(front, 0.025), M, 4, s.shell_mat, arc=(-27, 27), closed=True, caps=True)
    for a0, a1 in ((44, 84), (-84, -44), (156, 204)):
        lathe(bm, shell([top, P(1.06, 0.45, 0.42)], 0.025), M, 3, s.shell_mat, arc=(a0, a1), closed=True, caps=True)
        mid = (a0 + a1) / 2
        rivet(bm, frame(ring_point(M, 0.372, 0.342, 1.29, mid), ring_normal(M, mid)), mat=s.rivet)
    rivet(bm, frame(ring_point(M, 0.372, 0.342, 1.29, 0), ring_normal(M, 0)), mat=s.rivet)
    return pc


def hip_cloth(s):
    pc = Piece(s.name('HipCloth_L'))
    bm = pc.part('Hips')
    M = frame((0, 0.03, 0))
    ang = 104
    origin = ring_point(M, 0.36, 0.33, 1.31, ang)
    normal = ring_normal(M, ang)
    F = frame(origin, normal, DOWN) @ Matrix.Rotation(D(-10), 4, 'Y')
    cloth = [(0, -0.10), (0, 0.10), (0.32, 0.12), (0.32, -0.12)]
    plate(bm, cloth, 0.02, F, MAT['Team_Blue'])
    strap_mat = MAT['Leather_Brown'] if s.tier == 'leather' else s.metal
    plate(bm, [(-0.02, -0.125), (-0.02, 0.125), (0.07, 0.125), (0.07, -0.125)], 0.035, F, strap_mat, z0=-0.005)
    for y in (-0.07, 0.07):
        rivet(bm, F @ Matrix.Translation((0.025, y, 0.03)), r=0.02, h=0.02, mat=s.rivet)
    return pc


# --- arms

def shoulder(s):
    pc = Piece(s.name('Shoulder_L'))
    bm = pc.part('UpperArm.L')
    M = bone_frame('UpperArm.L')      # z along the arm, x front, y down
    leather = s.tier == 'leather'
    seg = 10 if leather else 6
    seg = 12 if leather else 8
    prof = [P(-0.07, 0.20), P(-0.01, 0.255), P(0.07, 0.275), P(0.15, 0.255), P(0.24, 0.185)]
    lathe(bm, shell(prof, 0.035), M, seg, s.shell_mat, arc=(-200, 20), closed=True, caps=True)
    # Team inset on top, a hair proud of the shell.
    lathe(bm, shell([P(-0.01, 0.262), P(0.07, 0.285), P(0.15, 0.263)], 0.012), M, 4, MAT['Team_Blue'], arc=(-138, -42), closed=True, caps=True)
    if leather:
        band(bm, M, 0.15, 0.185, 0.255, 0.255, 0.015, seg, MAT['Leather_Dark'], arc=(-200, 20))
    else:
        band(bm, M, 0.16, 0.20, 0.253, 0.253, 0.02, seg, s.metal, arc=(-200, 20))
    if leather:
        for ang in (-20, -160):
            rivet(bm, frame(ring_point(M, 0.245, 0.245, 0.10, ang), ring_normal(M, ang)), mat=s.rivet)
    return pc


def upper_arm(s):
    pc = Piece(s.name('UpperArm_L'))
    bm = pc.part('UpperArm.L')
    M = bone_frame('UpperArm.L')
    prof = [P(0.19, 0.182), P(0.215, 0.182), P(0.215, 0.17), P(0.31, 0.17), P(0.31, 0.182), P(0.335, 0.182)]
    lathe(bm, shell(prof, 0.03), M, 12, s.metal, closed=True)
    return pc


def elbow(s):
    pc = Piece(s.name('Elbow_L'))
    bm = pc.part('LowerArm.L')
    head, _ = BONES['LowerArm.L']
    M = frame(head + Vector((0, 0.09, 0)), BACK, UP)
    seg = 8
    lathe(bm, [P(0, 0.135, 0.115), P(0.05, 0.125, 0.105), P(0.09, 0.075, 0.065), P(0.115, 0)], M, seg, s.metal, cap_first=True, offset=30)
    band(bm, bone_frame('LowerArm.L'), -0.03, 0.03, 0.165, 0.165, 0.02, 10, MAT['Leather_Brown'])
    return pc


def forearm(s):
    leather = s.tier == 'leather'
    pc = Piece(s.name('Bracer_L' if leather else 'Forearm_L'))
    bm = pc.part('LowerArm.L')
    M = bone_frame('LowerArm.L')
    if leather:
        lathe(bm, shell([P(0.07, 0.158), P(0.22, 0.152), P(0.40, 0.15)], 0.03), M, 12, s.shell_mat, closed=True)
        for t in (0.12, 0.31):
            band(bm, M, t, t + 0.045, 0.17, 0.17, 0.018, 10, MAT['Leather_Dark'])
            rivet(bm, frame(ring_point(M, 0.17, 0.17, t + 0.022, -90), ring_normal(M, -90)), r=0.02, mat=s.rivet)
    else:
        prof = [P(0.06, 0.175), P(0.09, 0.175), P(0.09, 0.163), P(0.37, 0.148), P(0.37, 0.16), P(0.40, 0.16)]
        lathe(bm, shell(prof, 0.03), M, 12, s.metal, closed=True)
        for t in (0.13, 0.32):
            rivet(bm, frame(ring_point(M, 0.16, 0.16, t, -90), ring_normal(M, -90)), r=0.02, mat=s.rivet)
    return pc


def glove(s):
    leather = s.tier == 'leather'
    pc = Piece(s.name('Glove_L' if leather else 'Gauntlet_L'))
    cuff = pc.part('Fist.L')
    M = bone_frame('Fist.L')
    prof = [P(-0.03, 0.18), P(0.02, 0.17), P(0.10, 0.165)]
    lathe(cuff, shell(prof, 0.03), M, 12, s.shell_mat, closed=True)
    hand = pc.part('Fist.L', bevel=0.02)
    box(hand, frame((1.305, 0.035, 1.82), UP, FRONT), (0.31, 0.22, 0.20), s.shell_mat if leather else MAT['Leather_Brown'])
    if not leather:
        box(hand, frame((1.31, 0.03, 1.925), UP, FRONT), (0.22, 0.17, 0.035), s.metal)
    fingers = pc.part('Fist2.L', bevel=0.018)
    for i in range(4):
        y = -0.075 + i * 0.07
        box(fingers, frame((1.475, y, 1.825), UP, FRONT), (0.064, 0.18, 0.15), s.shell_mat)
    thumb = pc.part('Thumb2.L', bevel=0.018)
    box(thumb, frame((1.27, -0.145, 1.80), UP, FRONT), (0.07, 0.14, 0.10), s.shell_mat)
    return pc


# --- head

def helmet(s):
    pc = Piece(s.name('Helmet'))
    bm = pc.part('Head')
    M = frame((0, 0.06, 2.50))      # brow line; skull is x +-0.29, y -0.32..0.44, top 2.89
    dome = [P(0, 0.36, 0.43), P(0.12, 0.35, 0.42), P(0.24, 0.31, 0.37), P(0.35, 0.23, 0.27), P(0.42, 0.12, 0.14), P(0.46, 0)]
    lathe(bm, dome, M, 12, s.metal, cap_first=True, offset=15)
    if s.tier == 'iron':
        # Kettle hat: flat brim, a ridge over the crown, leather tabs hanging at the ears.
        lathe(bm, [P(0.0, 0.35, 0.42), P(-0.02, 0.52, 0.59), P(-0.05, 0.52, 0.59), P(-0.035, 0.35, 0.42)], M, 12, s.metal, closed=True, offset=15)
        box(bm, frame((0, 0.06, 2.945), UP, FRONT), (0.50, 0.05, 0.06), s.metal)
        for ang in (0, 90, 180, 270):
            rivet(bm, frame(ring_point(M, 0.365, 0.435, 0.05, ang), ring_normal(M, ang)), mat=s.rivet)
        for sx in (-0.335, 0.335):
            box(bm, frame((sx, 0.12, 2.36), UP, FRONT), (0.14, 0.03, 0.22), MAT['Leather_Brown'])
    else:
        # Closed helm: brow band, side-and-back skirt flaring at the neck, cheekguards, crest, diamond brow.
        band(bm, M, -0.03, 0.04, 0.375, 0.445, 0.025, 12, s.metal, offset=15)
        skirt = [P(0.0, 0.36, 0.43), P(-0.12, 0.36, 0.43), P(-0.24, 0.38, 0.47, cx=-0.03)]
        lathe(bm, shell(skirt, 0.03), M, 8, s.metal, arc=(62, 298), closed=True, caps=True)
        for ang in (54, -54):
            p = ring_point(M, 0.36, 0.43, -0.08, ang)
            F = frame(p, ring_normal(M, ang), DOWN)
            plate(bm, [(-0.06, -0.08), (-0.06, 0.08), (0.24, 0.07), (0.24, -0.05)], 0.03, F, s.metal, z0=-0.005)
        box(bm, frame((0, 0.06, 2.95), UP, FRONT), (0.50, 0.06, 0.07), s.metal)
        box(bm, frame((0, 0.06, 2.985), UP, FRONT), (0.42, 0.026, 0.03), MAT['Team_Blue'])
        diamond = [(0, 0.11), (0.08, 0), (0, -0.11), (-0.08, 0)]
        plate(bm, diamond, 0.04, frame((0, -0.365, 2.60), FRONT, LEFT), s.metal)
    return pc


def bare_head(arm):
    """The Warrior's Face object is hair strands plus ears and nose. This copies ears and
    nose (hair islands dropped) as a mesh skinned to Head, so a helmet tier can hide the
    Face renderer and keep a face."""
    face = bpy.data.objects['Face']
    arm.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    M = face.matrix_world.copy()
    arm.data.pose_position = 'POSE'
    bpy.context.view_layer.update()
    src = bmesh.new()
    src.from_mesh(face.data)
    seen = set()
    drop = []
    for v in src.verts:
        if v in seen:
            continue
        stack = [v]
        comp = []
        seen.add(v)
        while stack:
            a = stack.pop()
            comp.append(a)
            for e in a.link_edges:
                o = e.other_vert(a)
                if o not in seen:
                    seen.add(o)
                    stack.append(o)
        top = max((M @ c.co).z for c in comp)
        if top > 2.70:      # hair strands and ponytail; ears and nose stay below
            drop += comp
    bmesh.ops.delete(src, geom=drop, context='VERTS')
    bmesh.ops.transform(src, matrix=M, verts=src.verts[:])
    me = bpy.data.meshes.new('Warrior_Head_Bare')
    src.to_mesh(me)
    src.free()
    for m in face.data.materials:
        me.materials.append(m)
    ob = bpy.data.objects.new('Warrior_Head_Bare', me)
    bpy.context.scene.collection.objects.link(ob)
    ob.vertex_groups.new(name='Head').add(list(range(len(me.vertices))), 1.0, 'REPLACE')
    ob.parent = arm
    ob.modifiers.new('Armature', 'ARMATURE').object = arm
    return ob


# --- legs

def knee(s):
    pc = Piece(s.name('Knee_L'))
    bm = pc.part('LowerLeg.L')
    head, _ = BONES['LowerLeg.L']
    M = frame(head + Vector((0.01, -0.125, 0.01)), FRONT, UP)
    seg = 10 if s.tier == 'leather' else 8
    lathe(bm, [P(0, 0.145, 0.155), P(0.05, 0.135, 0.145), P(0.09, 0.085, 0.09), P(0.115, 0)], M, seg, s.shell_mat, cap_first=True, offset=30)
    band(bm, bone_frame('LowerLeg.L'), -0.025, 0.025, 0.165, 0.165, 0.02, 12, s.trim)
    return pc


def shin(s):
    leather = s.tier == 'leather'
    pc = Piece(s.name('Shin_L' if leather else 'Greave_L'))
    bm = pc.part('LowerLeg.L')
    M = bone_frame('LowerLeg.L')      # z runs down the shin, x front
    seg = 10 if leather else 8
    prof = [P(0.08, 0.165, 0.165, cx=0.01), P(0.20, 0.16, 0.16, cx=0.01), P(0.33, 0.165, 0.165, cx=0.01), P(0.37, 0.19, 0.19, cx=0.01)]
    lathe(bm, shell(prof, 0.03), M, seg, s.shell_mat, arc=(-105, 105), closed=True, caps=True)
    if leather:
        for t in (0.13, 0.28):
            band(bm, M, t, t + 0.045, 0.175, 0.175, 0.018, 10, MAT['Leather_Dark'])
    else:
        box(bm, frame(M @ Vector((0.17, 0, 0.21)), M.to_3x3() @ Vector((1, 0, 0)), M.to_3x3() @ Vector((0, 0, 1))), (0.24, 0.035, 0.035), s.metal)
        for ang in (80, -80):
            rivet(bm, frame(ring_point(M, 0.165, 0.165, 0.21, ang) + M.to_3x3() @ Vector((0.01, 0, 0)), ring_normal(M, ang)), r=0.02, mat=s.rivet)
    return pc


def boot(s):
    leather = s.tier == 'leather'
    pc = Piece(s.name('Boot_L'))
    shaft = pc.part('LowerLeg.L')
    M = frame((0.215, -0.015, 0))
    lathe(shaft, shell([P(0.33, 0.19, 0.185), P(0.30, 0.178, 0.173), P(0.10, 0.172, 0.167)], 0.03), M, 12, MAT['Leather_Brown'], closed=True)
    if not leather:
        band(shaft, M, 0.17, 0.235, 0.186, 0.181, 0.025, 12, s.metal)
    foot = pc.part('Foot.L', bevel=0.03)
    F = frame((0.235, 0.0, 0.0), UP, FRONT)     # local x toward the toes
    outline = [(-0.15, -0.07), (-0.13, -0.105), (-0.06, -0.12), (0.16, -0.12), (0.27, -0.105), (0.335, -0.06), (0.35, 0.0),
               (0.335, 0.06), (0.27, 0.105), (0.16, 0.12), (-0.06, 0.12), (-0.13, 0.105), (-0.15, 0.07)]
    plate(foot, outline, 0.15, F, MAT['Leather_Brown'], z0=0.01)
    sole = pc.part('Foot.L', bevel=0.0)
    plate(sole, scale_pts(outline, 1.04), 0.03, F, MAT['Leather_Dark'], z0=-0.004)
    if not leather:
        toe = [(0.04, -0.125), (0.16, -0.125), (0.28, -0.105), (0.345, -0.06), (0.36, 0.0), (0.345, 0.06), (0.28, 0.105), (0.16, 0.125), (0.04, 0.125)]
        cap = pc.part('Foot.L', bevel=0.02)
        plate(cap, toe, 0.03, F, s.metal, z0=0.155)
        plate(cap, [(0.03, -0.128), (0.03, 0.128), (0.10, 0.128), (0.10, -0.128)], 0.11, F, s.metal, z0=0.06)
    else:
        box(foot, F @ Matrix.Translation((0.09, 0, 0.14)), (0.06, 0.252, 0.03), MAT['Leather_Dark'])
    return pc


# --- shields

def shield_round():
    pc = Piece('Shield_Iron_Round')
    bm = pc.part('LowerArm.L')
    M = frame((0.94, -0.13, 1.86), FRONT, UP)   # local z faces forward, x up
    r = 0.40
    lathe(bm, [P(0, r), P(0.04, r)], M, 16, MAT['Wood_Brown'], cap_first=True, cap_last=True)
    plate(bm, rect(2 * r * 0.95, 0.15), 0.012, M, MAT['Team_Blue'], z0=0.04)
    lathe(bm, shell([P(0.0, r + 0.03), P(0.075, r + 0.03)], 0.065), M, 16, MAT['Metal_Iron'], closed=True)
    lathe(bm, [P(0.05, 0.13), P(0.10, 0.115), P(0.145, 0.065), P(0.165, 0)], M, 10, MAT['Metal_Iron'], cap_first=True)
    for i in range(8):
        a = D(i * 45 + 22.5)
        rivet(bm, M @ Matrix.Translation((math.cos(a) * (r - 0.002), math.sin(a) * (r - 0.002), 0.075)), r=0.022, h=0.022, mat=MAT['Metal_Dark'])
    for y in (-0.09, 0.09):
        box(bm, M @ Matrix.Translation((0, y, -0.04)), (0.30, 0.06, 0.07), MAT['Leather_Brown'])
    pc.r = r
    return pc


def shield_tall():
    pc = Piece('Shield_Gilded_Tall')
    bm = pc.part('LowerArm.L')
    M = frame((0.94, -0.13, 1.84), FRONT, UP)
    outline = [(0.50, -0.36), (0.53, -0.18), (0.53, 0.18), (0.50, 0.36), (0.27, 0.36), (-0.05, 0.32), (-0.32, 0.22), (-0.50, 0.07),
               (-0.55, 0), (-0.50, -0.07), (-0.32, -0.22), (-0.05, -0.32), (0.27, -0.36)]
    plate(bm, scale_pts(outline, 0.92), 0.03, M, MAT['Wood_Brown'], z0=-0.025)
    plate(bm, outline, 0.07, M, MAT['Metal_Gold'])
    plate(bm, scale_pts(outline, 0.84), 0.015, M, MAT['Team_Blue'], z0=0.07)
    lathe(bm, [P(0.085, 0.15, 0.10), P(0.16, 0)], M @ Matrix.Translation((0.02, 0, 0)), 4, MAT['Metal_Gold'], cap_first=True)
    for x, y in scale_pts(outline, 0.93):
        rivet(bm, M @ Matrix.Translation((x, y, 0.07)), r=0.02, h=0.02, mat=MAT['Metal_Gold'])
    for y in (-0.09, 0.09):
        box(bm, M @ Matrix.Translation((0, y, -0.05)), (0.30, 0.06, 0.07), MAT['Leather_Brown'])
    return pc


# ---------------------------------------------------------------- scene / export

def setup():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=WARRIOR, use_anim=False)
    arm = bpy.data.objects['CharacterArmature']
    for b in arm.data.bones:
        BONES[b.name] = (arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local)
    for name, rgba in MATERIALS:
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        bsdf = m.node_tree.nodes.get('Principled BSDF')
        if bsdf:
            bsdf.inputs['Base Color'].default_value = rgba
            bsdf.inputs['Metallic'].default_value = 0.15 if name.startswith('Metal') else 0.0
            bsdf.inputs['Roughness'].default_value = 0.4 if name.startswith('Metal') else 0.65
        m.diffuse_color = rgba
        m.metallic = 0.15 if name.startswith('Metal') else 0.0
        m.roughness = 0.4 if name.startswith('Metal') else 0.65
    for m in bpy.data.materials:
        if m.name.startswith('Warrior'):
            m.use_nodes = True
            bsdf = m.node_tree.nodes.get('Principled BSDF')
            tex_path = os.path.join(MODELS, 'Textures', m.name + '.png')
            if bsdf and os.path.exists(tex_path):
                tex = m.node_tree.nodes.new('ShaderNodeTexImage')
                tex.image = bpy.data.images.load(tex_path)
                m.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
                bsdf.inputs['Roughness'].default_value = 0.7
            m.diffuse_color = (0.78, 0.62, 0.48, 1)
    return arm


def build_set(s, arm):
    col = bpy.data.collections.new(f"Armor_{s.T}")
    bpy.context.scene.collection.children.link(col)
    pieces = [chest(s), waist(s)]
    sided = [hip_cloth(s), shoulder(s), forearm(s), glove(s), knee(s), shin(s), boot(s)]
    if s.tier != 'leather':
        pieces.append(helmet(s))
        sided += [upper_arm(s), elbow(s)]
    for p in sided:
        pieces.append(p)
        pieces.append(p.mirrored(p.name[:-2] + '_R'))
    return [p.build(arm, col) for p in pieces], col


def export(arm, objects, filename):
    for o in bpy.data.objects:
        o.select_set(False)
    arm.select_set(True)
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    path = os.path.join(MODELS, filename)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                             bake_anim=False, mesh_smooth_type='OFF', path_mode='STRIP', use_mesh_modifiers=True,
                             use_armature_deform_only=False)
    print("EXPORTED", path, len(objects), "meshes")


def render_previews(sets):
    scene = bpy.context.scene
    try:
        scene.render.engine = 'BLENDER_EEVEE'
    except TypeError:
        scene.render.engine = 'BLENDER_EEVEE_NEXT'
    scene.render.resolution_x = 700
    scene.render.resolution_y = 900
    scene.view_settings.view_transform = 'Standard'
    scene.render.film_transparent = False
    world = bpy.data.worlds.new('W')
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get('Background')
    if bg:
        bg.inputs['Color'].default_value = (0.66, 0.70, 0.76, 1)
        bg.inputs['Strength'].default_value = 0.2
    for name, kind, loc, rot, energy in (('key', 'SUN', (4, -6, 9), (D(40), 0, D(-30)), 0.5),
                                         ('fill', 'SUN', (-6, -4, 5), (D(60), 0, D(50)), 0.15),
                                         ('rim', 'SUN', (0, 7, 6), (D(-50), 0, 0), 0.25)):
        light = bpy.data.lights.new(name, kind)
        light.energy = energy
        light.angle = D(12)
        lo = bpy.data.objects.new(name, light)
        scene.collection.objects.link(lo)
        lo.location = loc
        lo.rotation_euler = rot
    cam = bpy.data.cameras.new('cam')
    co = bpy.data.objects.new('cam', cam)
    scene.collection.objects.link(co)
    cam.type = 'ORTHO'
    scene.camera = co
    shots = {
        'front': ((0, -12, 1.45), (D(90), 0, 0), 3.6),
        'side': ((12, 0, 1.45), (D(90), 0, D(90)), 3.6),
        'back': ((0, 12, 1.45), (D(90), 0, D(180)), 3.6),
        'quarter': ((8.5, -8.5, 1.45), (D(90), 0, D(45)), 3.6),
        'rts': ((0, -6.5, 12.5), (D(28), 0, 0), 7.0),
    }
    warrior_hidden = ['ShoulderPad.L', 'ShoulderPad.R']
    for name, col in sets:
        for other in bpy.data.collections:
            if other.name.startswith('Armor_'):
                other.hide_render = other is not col
        for hn in warrior_hidden:
            bpy.data.objects[hn].hide_render = True
        helmet_on = name != 'Armor_Leather'
        bpy.data.objects['Face'].hide_render = helmet_on
        bpy.data.objects['Warrior_Head_Bare'].hide_render = not helmet_on
        for shot, (loc, rot, size) in shots.items():
            co.location = loc
            co.rotation_euler = rot
            cam.ortho_scale = size
            scene.render.filepath = os.path.join(PREVIEWS, f"{name}_{shot}.png")
            bpy.ops.render.render(write_still=True)


def main():
    arm = setup()
    sets = []
    exported = []
    for tier in ('leather', 'iron', 'gilded'):
        s = Style(tier)
        objects, col = build_set(s, arm)
        export(arm, objects, f"Armor_{s.T}.fbx")
        sets.append((f"Armor_{s.T}", col))
    shields = bpy.data.collections.new('Shields')
    bpy.context.scene.collection.children.link(shields)
    for builder, fname in ((shield_round, 'Shield_Iron_Round.fbx'), (shield_tall, 'Shield_Gilded_Tall.fbx')):
        ob = builder().build(arm, shields)
        export(arm, [ob], fname)
    bare = bare_head(arm)
    export(arm, [bare], 'Warrior_Head_Bare.fbx')
    bare.hide_render = True
    tri = 0
    for o in bpy.data.objects:
        if o.type == 'MESH' and o.name.startswith(('Armor_', 'Shield_')):
            o.data.calc_loop_triangles()
            tri += len(o.data.loop_triangles)
            print("MESH %-28s tris=%5d" % (o.name, len(o.data.loop_triangles)))
    print("TOTAL tris", tri)
    if RENDER:
        os.makedirs(PREVIEWS, exist_ok=True)
        round_ob = bpy.data.objects['Shield_Iron_Round']
        tall_ob = bpy.data.objects['Shield_Gilded_Tall']
        for name, col in sets:
            for o in bpy.data.objects:
                if o.name.startswith('Shield_'):
                    o.hide_render = True
            pair = {'Armor_Iron': round_ob, 'Armor_Gilded': tall_ob}.get(name)
            if pair:
                pair.hide_render = False
            render_previews([(n, c) for n, c in sets if n == name])
    for o in bpy.data.objects:
        o.hide_render = False
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
    print("SAVED", BLEND_OUT)


main()
