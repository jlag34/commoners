"""Builds the Commoner's gear as skinned meshes on the Warrior.fbx armature, exports
one FBX per item into Resources/SwarmModels and saves armor_sets.blend next to this script.

  Armor_Leather/Iron/Gilded.fbx          armor sets, one mesh per piece
  Shield_Bronze_Round, Shield_Iron_Round, Shield_Gilded_Tall    rigid on the left forearm
  Weapon_Pitchfork, Weapon_Sword_Bronze/Steel/Gilded            rigid on Weapon.R
  Warrior_Head_Bare.fbx                  the Face mesh without its hair (nose) plus ears
  Warrior_Face_Features.fbx              eyes, brows and mouth laid on the skull
  Warrior_Body_Armored.fbx               the body without its tunic skirt, worn under armor

Run headless:
  /Applications/Blender.app/Contents/MacOS/Blender -b --python art/blender/build_armor.py [-- --no-render]

Units: Blender units as in Warrior.fbx (body 2.85 tall; the game scales it to
2.1 m). Z up, character front -Y, wearer's left +X. Every piece is built in the
rest (T) pose around the bone it is weighted to, so Unity can remap the skinned
bones by name onto a live Warrior instance (see UnitGear.cs). Shields are placed
for the Idle_Weapon pose and mapped back to rest, so they face forward in game;
previews are rendered in that pose. Lathe profiles P(t, rx, ry) put rx on the
frame's local x (usually FRONT) and ry on local y (usually sideways).
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from mathutils.geometry import intersect_point_line

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
    ("Metal_Steel", (0.84, 0.86, 0.90, 1)),
    ("Metal_Bronze", (0.66, 0.37, 0.22, 1)),
    ("Face_Features", (1, 1, 1, 1)),     # textured with the FACE_SWATCH atlas
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


def loft(bm, M, section, stations, mat, cap_first=True):
    """Polygon `section` (local xy) scaled per station (z, sx, sy) along M's local z.
    A station with zero scale closes the loft in a point."""
    rings = []
    for z, sx, sy in stations:
        if sx <= 1e-6 and sy <= 1e-6:
            rings.append([bm.verts.new(M @ Vector((0, 0, z)))])
        else:
            rings.append([bm.verts.new(M @ Vector((x * sx, y * sy, z))) for x, y in section])
    faces = []
    n = len(section)
    for r0, r1 in zip(rings, rings[1:]):
        for i in range(n):
            j = (i + 1) % n
            if len(r1) == 1:
                faces.append(bm.faces.new((r0[i], r0[j], r1[0])))
            elif len(r0) == 1:
                faces.append(bm.faces.new((r0[0], r1[j], r1[i])))
            else:
                faces.append(bm.faces.new((r0[i], r0[j], r1[j], r1[i])))
    if cap_first and len(rings[0]) > 2:
        faces.append(bm.faces.new(list(reversed(rings[0]))))
    if len(rings[-1]) > 2:
        faces.append(bm.faces.new(rings[-1]))
    for f in faces:
        f.material_index = mat
    return faces


def tube(bm, pts, radii, seg, mat, side):
    """Circles of `radii` swept through world points `pts`. `side` is a fixed world
    direction perpendicular to the whole path (the path is planar), so rings never twist.
    Zero radius at either end closes it in a point; otherwise the end is capped."""
    rings = []
    n = len(pts)
    for i, (p, r) in enumerate(zip(pts, radii)):
        d = (Vector(pts[min(i + 1, n - 1)]) - Vector(pts[max(i - 1, 0)])).normalized()
        F = frame(p, d, side)
        if r <= 1e-6:
            rings.append([bm.verts.new(F @ Vector((0, 0, 0)))])
        else:
            rings.append([bm.verts.new(F @ Vector((r * math.cos(D(a * 360 / seg)), r * math.sin(D(a * 360 / seg)), 0))) for a in range(seg)])
    faces = []
    for r0, r1 in zip(rings, rings[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            if len(r1) == 1:
                faces.append(bm.faces.new((r0[i], r0[j], r1[0])))
            elif len(r0) == 1:
                faces.append(bm.faces.new((r0[0], r1[j], r1[i])))
            else:
                faces.append(bm.faces.new((r0[i], r0[j], r1[j], r1[i])))
    if len(rings[0]) > 2:
        faces.append(bm.faces.new(list(reversed(rings[0]))))
    if len(rings[-1]) > 2:
        faces.append(bm.faces.new(rings[-1]))
    for f in faces:
        f.material_index = mat
    return faces


def ridge(bm, path, side, half_width, height, mat, centre):
    """A raised strip of rectangular section laid along `path` (world points on a
    surface). Its top faces away from `centre`; `side` is the strip's width direction."""
    side = Vector(side).normalized()
    n = len(path)
    rings = []
    for i, p in enumerate(path):
        p = Vector(p)
        d = Vector(path[min(i + 1, n - 1)]) - Vector(path[max(i - 1, 0)])
        up = d.cross(side).normalized()
        if up.dot(p - Vector(centre)) < 0:
            up = -up
        rings.append([bm.verts.new(p + side * sw - up * 0.01 + up * h) for sw, h in
                      ((-half_width, 0), (half_width, 0), (half_width, height), (-half_width, height))])
    faces = []
    for r0, r1 in zip(rings, rings[1:]):
        for i in range(4):
            j = (i + 1) % 4
            faces.append(bm.faces.new((r0[i], r0[j], r1[j], r1[i])))
    faces.append(bm.faces.new(list(reversed(rings[0]))))
    faces.append(bm.faces.new(rings[-1]))
    for f in faces:
        f.material_index = mat
    return faces


# ---------------------------------------------------------------- pieces

class Piece:
    """One exported mesh object. Parts are separate bmeshes, each rigid to a bone."""

    def __init__(self, name):
        self.name = name
        self.parts = []

    def part(self, bone, bevel=0.012, hard=62):
        """A rigid part on `bone`. Edges sharper than `hard` degrees shade flat."""
        bm = bmesh.new()
        self.parts.append((bm, bone, bevel, hard))
        return bm

    def mirrored(self, name):
        q = Piece(name)
        for bm, bone, bevel, hard in self.parts:
            bm2 = bm.copy()
            bmesh.ops.scale(bm2, vec=(-1, 1, 1), verts=bm2.verts[:])
            bmesh.ops.reverse_faces(bm2, faces=bm2.faces[:])
            q.parts.append((bm2, swap_side(bone), bevel, hard))
        return q

    def build(self, armature, collection):
        me = bpy.data.meshes.new(self.name)
        ob = bpy.data.objects.new(self.name, me)
        collection.objects.link(ob)
        final = bmesh.new()
        layer = final.verts.layers.int.new('bone')
        bones = []
        for bm, bone, bevel, hard in self.parts:
            bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
            if bevel > 0:
                sharp = [e for e in bm.edges if e.is_manifold and e.calc_length() > 0.03 and e.calc_face_angle(0) > D(50)]
                if sharp:
                    made = bmesh.ops.bevel(bm, geom=sharp, offset=bevel, segments=2, affect='EDGES', clamp_overlap=True, loop_slide=True)
                    inherit_material(made['faces'])
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
            sharp = [e for e in bm.edges if e.is_manifold and e.calc_face_angle(0) > D(hard)]
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


def inherit_material(faces):
    """Bevel faces come out with material 0; give each the material of a face it borders."""
    todo = set(faces)
    while todo:
        done = []
        for f in todo:
            for e in f.edges:
                o = next((g for g in e.link_faces if g is not f and g not in todo), None)
                if o is not None:
                    f.material_index = o.material_index
                    done.append(f)
                    break
        if not done:
            break
        todo.difference_update(done)


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
    # The top tucks up inside the chest's hem so no gap opens when the idle leans forward.
    lathe(bm, shell([P(1.30, 0.375, 0.325), P(1.42, 0.375, 0.325), P(1.47, 0.32, 0.288)], 0.03), M, 16, s.shell_mat, closed=True, offset=11.25)
    if leather:
        box(bm, frame((0, -0.365, 1.36), FRONT, LEFT), (0.1, 0.08, 0.03), MAT['Metal_Dark'])
    else:
        for ang in (30, -30):
            rivet(bm, frame(ring_point(M, 0.375, 0.325, 1.36, ang), ring_normal(M, ang)), mat=s.rivet)
    # Padded underskirt: Warrior_Body_Armored has no skirt, so this is what shows
    # between the tassets.
    pad = MAT['Leather_Dark'] if leather else MAT['Leather_Brown']
    lathe(bm, shell([P(1.34, 0.37, 0.32), P(1.20, 0.41, 0.355), P(1.03, 0.44, 0.39)], 0.025), M, 16, pad, closed=True, offset=11.25)
    # Tassets hang from the belt as curved cone segments over it.
    top = P(1.33, 0.39, 0.34)
    if leather:
        front = [top, P(1.02, 0.47, 0.43)]
        lathe(bm, shell(front, 0.025), M, 3, s.shell_mat, arc=(-24, 24), closed=True, caps=True)
    else:
        front = [P(1.33, 0.39, 0.34, -27, 27), P(1.10, 0.455, 0.41, -25, 25), P(0.93, 0.52, 0.48, -4, 4)]
        lathe(bm, shell(front, 0.025), M, 4, s.shell_mat, arc=(-27, 27), closed=True, caps=True)
    for a0, a1 in ((44, 84), (-84, -44), (156, 204)):
        lathe(bm, shell([top, P(1.06, 0.47, 0.43)], 0.025), M, 3, s.shell_mat, arc=(a0, a1), closed=True, caps=True)
        mid = (a0 + a1) / 2
        rivet(bm, frame(ring_point(M, 0.402, 0.352, 1.29, mid), ring_normal(M, mid)), mat=s.rivet)
    rivet(bm, frame(ring_point(M, 0.402, 0.352, 1.29, 0), ring_normal(M, 0)), mat=s.rivet)
    return pc


def hip_cloth(s):
    pc = Piece(s.name('HipCloth_L'))
    bm = pc.part('Hips')
    M = frame((0, 0.03, 0))
    ang = 104
    origin = ring_point(M, 0.39, 0.345, 1.31, ang)
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

# The skull (Warrior_Body, Head bone) spans x +-0.286, y -0.318..0.442 and tops out at
# z 2.888: longer front to back than it is wide. The dome is a superellipse sized to
# clear it by >= 0.035 everywhere above its rim, which sits just over the brows.
HELM_O = Vector((0, 0.06, 2.57))
HELM_FRONT, HELM_SIDE, HELM_H, HELM_N = 0.45, 0.355, 0.47, 2.4
HELM_T = (0.0, 0.2, 0.4, 0.55, 0.7, 0.82, 0.92, 1.0)


def dome_k(f):
    """Superellipse radius factor at height fraction f of the dome."""
    return max(0.0, 1 - f ** HELM_N) ** (1 / HELM_N)


def dome_point(f, angle, lift=0.0):
    """Point on the dome at height fraction f; angle 0 = front, 90 = wearer's left."""
    k = dome_k(f)
    a = D(angle)
    return HELM_O + Vector((math.sin(a) * (HELM_SIDE * k + lift), -math.cos(a) * (HELM_FRONT * k + lift), f * HELM_H + lift * (1 - k)))


def helmet_ridge(bm, angle, half_width, height, mat, f0=0.06):
    """A strip over the crown in the vertical plane at `angle`, rim to rim."""
    # Same heights as the dome's rings, so the strip lies on the lathe's vertex columns.
    fs = [f0] + [f for f in HELM_T if f0 < f < 1]
    path = [dome_point(f, angle) for f in fs] + [dome_point(1, angle)] + [dome_point(f, angle + 180) for f in reversed(fs)]
    a = D(angle)
    side = Vector((math.cos(a), math.sin(a), 0))
    return ridge(bm, path, side, half_width, height, mat, HELM_O + Vector((0, 0, HELM_H * 0.2)))


def helmet(s):
    pc = Piece(s.name('Helmet'))
    bm = pc.part('Head')
    M = frame(HELM_O)      # brow line; local x front, y wearer's left
    dome = [P(f * HELM_H, HELM_FRONT * dome_k(f), HELM_SIDE * dome_k(f)) for f in HELM_T]
    lathe(bm, dome, M, 16, s.metal)
    F, S = HELM_FRONT, HELM_SIDE
    if s.tier == 'iron':
        # Kettle hat: wide flat brim, crossed ridges over the crown, leather tabs at the ears.
        lathe(bm, [P(0.0, F, S), P(-0.035, F + 0.16, S + 0.16), P(-0.065, F + 0.16, S + 0.16), P(-0.045, F - 0.01, S - 0.01)],
              M, 16, s.metal, closed=True)
        helmet_ridge(bm, 0, 0.035, 0.035, s.metal)
        helmet_ridge(bm, 90, 0.022, 0.022, s.metal, f0=0.3)
        for ang in (0, 90, 180, 270):
            p = dome_point(0.08, ang)
            rivet(bm, frame(p, (p - HELM_O - Vector((0, 0, 0.03))).normalized()), mat=s.rivet)
        for sx in (-1, 1):
            box(bm, frame((sx * (S - 0.005), 0.14, HELM_O.z - 0.12), UP, FRONT), (0.13, 0.03, 0.22), MAT['Leather_Brown'])
    else:
        # Closed helm: brow band, side-and-back skirt flaring at the neck, cheekguards,
        # a crest with a blue inset from brow to nape, diamond on the brow.
        band(bm, M, -0.03, 0.05, F + 0.02, S + 0.02, 0.03, 16, s.metal)
        skirt = [P(0.0, F, S), P(-0.12, F, S), P(-0.25, F + 0.05, S + 0.05, cx=0.03)]
        lathe(bm, shell(skirt, 0.03), M, 10, s.metal, arc=(62, 298), closed=True, caps=True)
        for ang in (56, -56):
            p = ring_point(M, F, S, -0.06, ang)
            Fm = frame(p, ring_normal(M, ang), DOWN) @ Matrix.Rotation(D(12), 4, 'Y')
            plate(bm, [(-0.06, -0.085), (-0.06, 0.085), (0.25, 0.07), (0.25, -0.045)], 0.03, Fm, s.metal, z0=-0.01)
        helmet_ridge(bm, 0, 0.045, 0.045, s.metal, f0=0.12)
        helmet_ridge(bm, 0, 0.018, 0.058, MAT['Team_Blue'], f0=0.14)
        diamond = [(0, 0.11), (0.085, 0), (0, -0.11), (-0.085, 0)]
        lathe(bm, [P(0, 0.11, 0.085), P(0.035, 0.09, 0.07), P(0.06, 0)], frame(HELM_O + Vector((0, -F - 0.02, 0.07)), FRONT, UP), 4, s.metal,
              cap_first=True, offset=0)
    return pc


def bare_head(arm):
    """The Warrior's Face object is hair strands plus a nose. This keeps the nose (hair
    islands dropped) and adds ears, as a mesh skinned to Head that Unity shows in place of
    the Face renderer."""
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
    # Ears on the sides of the skull, mapped to the cheek's skin texels so they take the
    # same texture and team tint as the face.
    uv = src.loops.layers.uv.active
    body = bpy.data.objects['Warrior_Body'].data
    _, _, cheek, _ = SKULL.ray_cast(Vector((0.15, -1.0, 2.38)), Vector((0, 1, 0)))
    corners = [body.uv_layers.active.data[i].uv for i in body.polygons[cheek].loop_indices]
    skin = sum(corners, Vector((0, 0))) / len(corners)
    for sx in (-1, 1):
        F = frame((sx * 0.268, 0.04, EYE_Z - 0.01), Vector((sx, 0.25, 0)), UP)
        made = lathe(src, [P(-0.02, 0.085, 0.055), P(0.02, 0.08, 0.05), P(0.038, 0.055, 0.034), P(0.046, 0)], F, 10, 0, cap_first=True)
        bmesh.ops.recalc_face_normals(src, faces=made)
        for f in made:
            f.smooth = True
            for l in f.loops:
                l[uv].uv = skin
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


# --- face

SKULL = None   # BVH of Warrior_Body at rest, for laying features on the face
# Atlas cell (4x4 grid) and sRGB colour per feature; written to Textures/Face_Features.png.
FACE_SWATCH = {
    'white': ((0, 0), (0.96, 0.95, 0.92)),
    'iris': ((1, 0), (0.36, 0.2, 0.11)),
    'pupil': ((2, 0), (0.07, 0.05, 0.04)),
    'glint': ((3, 0), (1.0, 1.0, 1.0)),
    'lid': ((0, 1), (0.2, 0.12, 0.07)),
    'brow': ((1, 1), (0.3, 0.18, 0.1)),
    'lip': ((2, 1), (0.55, 0.25, 0.2)),
}
FACE_ATLAS = os.path.join(MODELS, 'Textures', 'Face_Features.png')


def face_atlas():
    """Writes the swatch texture and hooks it into the Face_Features material."""
    import numpy as np
    size, cell = 32, 8
    px = np.ones((size, size, 4), dtype=np.float32)
    for (i, j), rgb in FACE_SWATCH.values():
        px[j * cell:(j + 1) * cell, i * cell:(i + 1) * cell, :3] = rgb
    img = bpy.data.images.new('Face_Features', size, size, alpha=True)
    img.pixels.foreach_set(px.ravel())
    img.filepath_raw = FACE_ATLAS
    img.file_format = 'PNG'
    img.save()
    m = bpy.data.materials['Face_Features']
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    tex = m.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = img
    tex.interpolation = 'Closest'
    m.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])


def face_uv(name):
    (i, j), _ = FACE_SWATCH[name]
    return ((i + 0.5) / 4, (j + 0.5) / 4)


def on_face(bm, x, z, lift):
    """A vertex `lift` off the skull where a ray from straight ahead at (x, z) lands."""
    hit, n, _, _ = SKULL.ray_cast(Vector((x, -1.0, z)), Vector((0, 1, 0)))
    return bm.verts.new(hit + n * lift)


def face_slab(bm, top, low, swatch, rim):
    """Closes a lifted grid (`top`) against its copy just above the skin (`low`) into a thin
    solid, so recalculated normals face out. `rim` lists (row, col) around the edge."""
    faces = []

    def quad(vs):
        vs = [v for k, v in enumerate(vs) if v not in vs[:k]]
        if len(vs) >= 3:
            faces.append(bm.faces.new(vs))

    for g in (top, low):
        for r0, r1 in zip(g, g[1:]):
            for i in range(len(r0) - 1):
                quad([r0[i], r0[i + 1], r1[i + 1], r1[i]])
    for (ra, ca), (rb, cb) in zip(rim, rim[1:] + rim[:1]):
        quad([low[ra][ca], low[rb][cb], top[rb][cb], top[ra][ca]])
    uv = bm.loops.layers.uv.verify()
    for f in faces:
        f.material_index = MAT['Face_Features']
        for loop in f.loops:
            loop[uv].uv = face_uv(swatch)
    return faces


def face_patch(bm, rows, swatch, lift):
    """A thin raised patch over a grid of front-view points (rows of (x, z)), following
    the facets of the low-poly face."""
    top = [[on_face(bm, x, z, lift) for x, z in row] for row in rows]
    low = [[on_face(bm, x, z, 0.0015) for x, z in row] for row in rows]
    nr, nc = len(rows), len(rows[0])
    rim = [(0, c) for c in range(nc)] + [(r, nc - 1) for r in range(1, nr)] + \
          [(nr - 1, c) for c in range(nc - 2, -1, -1)] + [(r, 0) for r in range(nr - 2, 0, -1)]
    return face_slab(bm, top, low, swatch, rim)


def face_ellipse(bm, cx, cz, rx, rz, swatch, lift, seg=14):
    """Filled ellipse: a centre point, then rings outward (the grid's first row is the centre)."""
    rows = [[(cx + rx * f * math.cos(D(a * 360 / seg)), cz + rz * f * math.sin(D(a * 360 / seg))) for a in range(seg)]
            for f in (0.5, 1.0)]

    def layer(lift_):
        centre = on_face(bm, cx, cz, lift_)
        grid = [[centre] * (seg + 1)]
        for row in rows:
            ring = [on_face(bm, x, z, lift_) for x, z in row]
            grid.append(ring + ring[:1])     # closed ring
        return grid

    top, low = layer(lift), layer(0.0015)
    return face_slab(bm, top, low, swatch, [(2, c) for c in range(seg)])


def face_strip(bm, centre, widths, swatch, lift):
    """A band along a front-view polyline, `widths` thick (full width) at each point."""
    rows = [[], [], []]
    for k, ((x, z), w) in enumerate(zip(centre, widths)):
        x0, z0 = centre[max(k - 1, 0)]
        x1, z1 = centre[min(k + 1, len(centre) - 1)]
        tx, tz = x1 - x0, z1 - z0
        ln = math.hypot(tx, tz) or 1.0
        nx, nz = -tz / ln, tx / ln
        for r, f in zip(rows, (-0.5, 0.0, 0.5)):
            r.append((x + nx * w * f, z + nz * w * f))
    return face_patch(bm, rows, swatch, lift)


# Front-view layout on the face (x wearer's left, z up). Nose runs z 2.31..2.51.
EYE_X, EYE_Z = 0.105, 2.445


def face_features():
    """Eyes, lids, brows and a smile, after current/01_full_body_armor_fit.png."""
    pc = Piece('Warrior_Face_Features')
    for sx in (-1, 1):
        bm = pc.part('Head', bevel=0.0, hard=80)
        cx = sx * EYE_X
        face_ellipse(bm, cx, EYE_Z, 0.043, 0.04, 'white', 0.004)
        face_ellipse(bm, cx - sx * 0.006, EYE_Z - 0.003, 0.03, 0.035, 'iris', 0.007)
        face_ellipse(bm, cx - sx * 0.006, EYE_Z - 0.003, 0.015, 0.019, 'pupil', 0.0095)
        face_ellipse(bm, cx - sx * 0.006 + 0.011, EYE_Z + 0.013, 0.008, 0.008, 'glint', 0.012, seg=8)
        lid = [(cx + sx * f * 0.05, EYE_Z + 0.03 + 0.012 * (1 - (f / 1.0) ** 2) - (0.012 if f > 0.8 else 0)) for f in (-0.95, -0.6, -0.2, 0.2, 0.6, 0.95)]
        face_strip(bm, lid, [0.008, 0.013, 0.015, 0.015, 0.013, 0.01], 'lid', 0.0105)
        # Heavy brows, set a touch low over the eyes for the determined look.
        brow = [(sx * 0.04, 2.506), (sx * 0.075, 2.522), (sx * 0.115, 2.53), (sx * 0.15, 2.527), (sx * 0.178, 2.515)]
        face_strip(bm, brow, [0.034, 0.033, 0.03, 0.025, 0.017], 'brow', 0.008)
    bm = pc.part('Head', bevel=0.0, hard=80)
    smile = [(f * 0.07, 2.226 + 0.014 * f * f) for f in (-1, -0.66, -0.33, 0, 0.33, 0.66, 1)]
    face_strip(bm, smile, [0.008, 0.011, 0.012, 0.012, 0.012, 0.011, 0.008], 'lip', 0.005)
    return pc


# --- legs

def thigh(s):
    """Cuisse around the thigh. Armored tiers wear Warrior_Body_Armored, whose skirt is
    gone, so the plate is the thigh's visible surface between the tassets and the knee."""
    leather = s.tier == 'leather'
    pc = Piece(s.name('Thigh_L'))
    bm = pc.part('UpperLeg.L')
    M = bone_frame('UpperLeg.L')      # z down the thigh, x front, y toward the other leg
    # Centre shifted outward so the two thighs clear each other at the crotch.
    prof = [P(0.03, 0.225, 0.2, cy=-0.025), P(0.14, 0.235, 0.21, cy=-0.025), P(0.34, 0.225, 0.2, cy=-0.02), P(0.47, 0.205, 0.185, cy=-0.015)]
    lathe(bm, shell(prof, 0.025), M, 14 if leather else 12, s.shell_mat, closed=True)
    if leather:
        for t in (0.12, 0.36):
            band(bm, M, t, t + 0.045, 0.245, 0.22, 0.02, 14, MAT['Leather_Dark'])
            rivet(bm, frame(ring_point(M, 0.245, 0.22, t + 0.022, 0), ring_normal(M, 0)), r=0.02, mat=s.rivet)
    else:
        band(bm, M, 0.02, 0.07, 0.24, 0.215, 0.025, 12, s.metal)
        box(bm, frame(M @ Vector((0.235, 0, 0.26)), M.to_3x3() @ Vector((1, 0, 0)), M.to_3x3() @ Vector((0, 0, 1))), (0.30, 0.035, 0.03), s.metal)
        for t in (0.16, 0.38):
            rivet(bm, frame(ring_point(M, 0.233, 0.208, t, -90) + M.to_3x3() @ Vector((0, -0.02, 0)), ring_normal(M, -90)), r=0.02, mat=s.rivet)
    return pc


def knee(s):
    pc = Piece(s.name('Knee_L'))
    bm = pc.part('LowerLeg.L')
    head, _ = BONES['LowerLeg.L']
    M = frame(head + Vector((0.01, -0.125, 0.01)), FRONT, UP)
    seg = 10 if s.tier == 'leather' else 8
    lathe(bm, [P(0, 0.145, 0.155), P(0.05, 0.135, 0.145), P(0.09, 0.085, 0.09), P(0.115, 0)], M, seg, s.shell_mat, cap_first=True, offset=30)
    # Padding around the joint fills the gap between thigh plate and shin at the back.
    band(bm, bone_frame('LowerLeg.L'), -0.10, 0.06, 0.168, 0.168, 0.025, 12, s.trim)
    return pc


def shin(s):
    leather = s.tier == 'leather'
    pc = Piece(s.name('Shin_L' if leather else 'Greave_L'))
    bm = pc.part('LowerLeg.L')
    M = bone_frame('LowerLeg.L')      # z runs down the shin, x front
    seg = 10 if leather else 8
    prof = [P(0.08, 0.165, 0.165, cx=0.01), P(0.20, 0.16, 0.16, cx=0.01), P(0.33, 0.165, 0.165, cx=0.01), P(0.37, 0.19, 0.19, cx=0.01)]
    lathe(bm, shell(prof, 0.03), M, seg, s.shell_mat, arc=(-105, 105), closed=True, caps=True)
    # Leather backing closes the open back of the shell over the calf.
    back = [P(0.07, 0.15, 0.15, cx=0.01), P(0.36, 0.15, 0.15, cx=0.01)]
    lathe(bm, shell(back, 0.02), M, 6, s.trim, arc=(100, 260), closed=True, caps=True)
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

POSE = {}      # bone -> world matrix taking rest positions to the Idle_Weapon pose


def shield_frame(lift=0.6, offset=0.26, out=28):
    """Local z faces out of the shield, local x is its top. Designed in the idle pose:
    upright in front of the left forearm, facing front and turned `out` degrees
    outward, clear of the gauntlet; then mapped back to rest so the skinned mesh lands
    there. Following the forearm's own tilt instead leans the top into the shoulder."""
    pose = POSE['LowerArm.L']
    head, tail = BONES['LowerArm.L']
    n = Vector((math.sin(D(out)), -math.cos(D(out)), 0))
    return pose.inverted() @ frame((pose @ head).lerp(pose @ tail, lift) + n * offset, n, UP)


def shield_straps(bm, M, depth):
    """Two straps across the back reaching in to the forearm."""
    for x in (-0.1, 0.1):
        box(bm, M @ Matrix.Translation((x, 0, depth - 0.05)), (0.06, 0.30, 0.15), MAT['Leather_Brown'])


def shield_round(name, r, rim, boss):
    pc = Piece(name)
    bm = pc.part('LowerArm.L')
    M = shield_frame()
    lathe(bm, [P(0, r), P(0.04, r)], M, 16, MAT['Wood_Brown'], cap_first=True, cap_last=True)
    plate(bm, rect(2 * r * 0.95, 0.15), 0.012, M, MAT['Team_Blue'], z0=0.04)
    lathe(bm, shell([P(0.0, r + 0.03), P(0.075, r + 0.03)], 0.065), M, 16, MAT[rim], closed=True)
    lathe(bm, [P(0.05, 0.13), P(0.10, 0.115), P(0.145, 0.065), P(0.165, 0)], M, 10, MAT[boss], cap_first=True)
    rivet_mat = MAT['Metal_Dark'] if rim == 'Metal_Iron' else MAT[rim]
    for i in range(8):
        a = D(i * 45 + 22.5)
        rivet(bm, M @ Matrix.Translation((math.cos(a) * (r - 0.002), math.sin(a) * (r - 0.002), 0.075)), r=0.022, h=0.022, mat=rivet_mat)
    shield_straps(bm, M, -0.04)
    return pc


def shield_tall():
    pc = Piece('Shield_Gilded_Tall')
    bm = pc.part('LowerArm.L')
    M = shield_frame(lift=0.55, offset=0.27)
    outline = [(0.50, -0.36), (0.53, -0.18), (0.53, 0.18), (0.50, 0.36), (0.27, 0.36), (-0.05, 0.32), (-0.32, 0.22), (-0.50, 0.07),
               (-0.55, 0), (-0.50, -0.07), (-0.32, -0.22), (-0.05, -0.32), (0.27, -0.36)]
    plate(bm, scale_pts(outline, 0.92), 0.03, M, MAT['Wood_Brown'], z0=-0.025)
    plate(bm, outline, 0.07, M, MAT['Metal_Gold'])
    plate(bm, scale_pts(outline, 0.84), 0.015, M, MAT['Team_Blue'], z0=0.07)
    lathe(bm, [P(0.085, 0.15, 0.10), P(0.16, 0)], M @ Matrix.Translation((0.02, 0, 0)), 4, MAT['Metal_Gold'], cap_first=True)
    for x, y in scale_pts(outline, 0.93):
        rivet(bm, M @ Matrix.Translation((x, y, 0.07)), r=0.02, h=0.02, mat=MAT['Metal_Gold'])
    shield_straps(bm, M, -0.05)
    return pc


# --- weapons

WEAPON = Matrix()   # rest frame of the Warrior's sword: z grip -> tip, x across the guard, y off the flat


def weapon_point(x, y, z):
    return WEAPON @ Vector((x, y, z))


SWORDS = {
    # blade length / width, guard half-span, fittings, blade metal
    'Bronze': dict(blade=1.02, width=0.17, guard=0.21, fit='Metal_Bronze', steel='Metal_Bronze'),
    'Steel': dict(blade=1.10, width=0.18, guard=0.24, fit='Metal_Steel', steel='Metal_Steel'),
    'Gilded': dict(blade=1.16, width=0.19, guard=0.28, fit='Metal_Gold', steel='Metal_Steel'),
}


def sword(kind):
    """Weapon_Sword_<kind> after current/09_swords_bronze_corrected.png: faceted blade,
    leather-wrapped grip through the fist, guard and pommel in the fitting metal."""
    c = SWORDS[kind]
    pc = Piece(f"Weapon_Sword_{kind}")
    W = WEAPON
    fit = MAT[c['fit']]
    grip = pc.part('Weapon.R', bevel=0.0)
    lathe(grip, [P(-0.2, 0.038), P(0.18, 0.042)], W, 8, MAT['Leather_Brown'], cap_first=True, cap_last=True, offset=22.5)
    for t in (-0.14, -0.06, 0.02, 0.10):
        band(grip, W @ Matrix.Rotation(D(14), 4, 'X'), t - 0.016, t + 0.016, 0.047, 0.047, 0.014, 8, MAT['Leather_Dark'], offset=22.5)

    w, L, th = c['width'], c['blade'], 0.05
    base = 0.235
    blade = pc.part('Weapon.R', bevel=0.0, hard=25)
    section = [(-w / 2, 0), (-w * 0.13, th / 2), (w * 0.13, th / 2), (w / 2, 0), (w * 0.13, -th / 2), (-w * 0.13, -th / 2)]
    loft(blade, W, section, [(base, 0.86, 1), (base + 0.12, 1, 1), (base + L - 0.27, 1, 1), (base + L, 0, 0)], MAT[c['steel']])
    if kind == 'Gilded':
        inlay = [(-0.032, 0), (0, th / 2 + 0.008), (0.032, 0), (0, -th / 2 - 0.008)]
        loft(blade, W, inlay, [(base, 1, 1), (base + L - 0.30, 1, 1), (base + L - 0.12, 0, 0)], fit)

    guard = pc.part('Weapon.R', bevel=0.014, hard=40)
    G = W @ Matrix.Translation((0, 0, 0.205))
    g = c['guard']
    box(guard, G, (2 * g - 0.08, 0.075, 0.065), fit)
    for sx in (-1, 1):
        # Flared, faceted ends angled up toward the blade.
        end = G @ Matrix.Translation((sx * (g - 0.045), 0, 0.01)) @ Matrix.Rotation(D(-sx * 14), 4, 'Y')
        box(guard, end, (0.09, 0.095, 0.10 if kind != 'Bronze' else 0.085), fit)
    box(guard, G @ Matrix.Translation((0, 0, 0.0)), (0.13, 0.095, 0.09), fit)
    if kind == 'Gilded':
        for sy in (-1, 1):
            Dm = frame(G @ Vector((0, sy * 0.045, 0.03)), W.to_3x3() @ Vector((0, sy, 0)), W.to_3x3() @ Vector((0, 0, 1)))
            lathe(guard, [P(0, 0.085, 0.06), P(0.03, 0.06, 0.042), P(0.045, 0)], Dm, 4, fit, cap_first=True)

    pommel = pc.part('Weapon.R', bevel=0.01, hard=30)
    lathe(pommel, [P(-0.24, 0.03), P(-0.2, 0.034)], W, 8, fit, cap_first=True, cap_last=True)
    Pm = frame(weapon_point(0, 0, -0.29), WEAPON.to_3x3() @ Vector((0, 1, 0)), WEAPON.to_3x3() @ Vector((0, 0, 1)))
    if kind == 'Bronze':
        lathe(pommel, [P(-0.04, 0.072), P(0.04, 0.072)], Pm, 16, fit, cap_first=True, cap_last=True)
        for sy in (-1, 1):
            lathe(pommel, [P(0.035, 0.05), P(0.052, 0.045), P(0.056, 0)], Pm @ Matrix.Rotation(D(90 - sy * 90), 4, 'X'), 16, fit, cap_first=True)
    else:
        lathe(pommel, [P(-0.045, 0.06), P(-0.03, 0.078), P(0.03, 0.078), P(0.045, 0.06)], Pm, 8, fit, cap_first=True, cap_last=True, offset=22.5)
    return pc


def pitchfork():
    """Weapon_Pitchfork after core_v1/02_equipment.png: three-tine iron head on a wooden
    shaft. Held a third of the way up, tines on the blade side so they point up at idle."""
    pc = Piece('Weapon_Pitchfork')
    W = WEAPON
    shaft = pc.part('Weapon.R', bevel=0.0)
    lathe(shaft, [P(-0.47, 0), P(-0.46, 0.03), P(-0.43, 0.04), P(1.08, 0.034), P(1.1, 0)], W, 8, MAT['Wood_Brown'], offset=22.5)
    band(shaft, W, -0.15, 0.15, 0.044, 0.044, 0.014, 8, MAT['Leather_Dark'], offset=22.5)
    band(shaft, W, -0.4, -0.33, 0.044, 0.044, 0.014, 8, MAT['Leather_Dark'], offset=22.5)
    head = pc.part('Weapon.R', bevel=0.0, hard=40)
    iron = MAT['Metal_Dark']
    lathe(head, [P(1.0, 0.04), P(1.12, 0.048), P(1.17, 0.042)], W, 8, iron, cap_first=True, cap_last=True, offset=22.5)
    rivet(head, frame(weapon_point(0, -0.046, 1.08), WEAPON.to_3x3() @ Vector((0, -1, 0))), r=0.018, h=0.018, mat=iron)
    side = WEAPON.to_3x3() @ Vector((0, 1, 0))
    # The U-shaped bar that carries the outer tines, then the three tines.
    bar = [(-0.16, 1.3), (-0.155, 1.235), (-0.12, 1.185), (-0.06, 1.165), (0, 1.16), (0.06, 1.165), (0.12, 1.185), (0.155, 1.235), (0.16, 1.3)]
    tube(head, [weapon_point(x, 0, z) for x, z in bar], [0.028] * len(bar), 6, iron, side)
    tube(head, [weapon_point(0, 0, z) for z in (1.15, 1.3, 1.45, 1.6, 1.66)], [0.03, 0.027, 0.023, 0.014, 0], 6, iron, side)
    for sx in (-1, 1):
        tine = [(0.16, 1.29), (0.168, 1.42), (0.165, 1.52), (0.155, 1.6), (0.148, 1.645)]
        tube(head, [weapon_point(sx * x, 0, z) for x, z in tine], [0.027, 0.024, 0.02, 0.013, 0], 6, iron, side)
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


def idle_pose():
    """Imports a second Warrior with its clips and reads Idle_Weapon at frame 1 into POSE.
    The copy is deleted again; the action is kept for the previews."""
    objects = set(bpy.data.objects)
    materials = set(bpy.data.materials)
    bpy.ops.import_scene.fbx(filepath=WARRIOR, use_anim=True)
    new = [o for o in bpy.data.objects if o not in objects]
    tmp = next(o for o in new if o.type == 'ARMATURE')
    action = next(a for a in bpy.data.actions if a.name.endswith('|Idle_Weapon'))
    pose_with(tmp, action)
    for pb in tmp.pose.bones:
        POSE[pb.name] = tmp.matrix_world @ pb.matrix @ pb.bone.matrix_local.inverted() @ tmp.matrix_world.inverted()
    for o in new:
        data = o.data
        bpy.data.objects.remove(o)
        if data is not None and data.users == 0:
            (bpy.data.meshes if isinstance(data, bpy.types.Mesh) else bpy.data.armatures).remove(data)
    for m in set(bpy.data.materials) - materials:
        bpy.data.materials.remove(m)
    action.use_fake_user = True
    return action


def pose_with(arm, action, frame_no=1):
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = action
    if hasattr(ad, 'action_slot') and ad.action_slot is None and len(getattr(action, 'slots', [])):
        ad.action_slot = action.slots[0]
    bpy.context.scene.frame_set(frame_no)
    bpy.context.view_layer.update()


def weapon_frame(arm):
    """The Warrior_Sword's rest placement without its scale; new weapons are built in it."""
    sw = bpy.data.objects['Warrior_Sword']
    arm.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    loc, rot, _ = sw.matrix_world.decompose()
    arm.data.pose_position = 'POSE'
    bpy.context.view_layer.update()
    return Matrix.Translation(loc) @ rot.to_matrix().to_4x4()


def armored_body(arm):
    """Warrior_Body minus its tunic skirt and flaps (faces near the hips that hang more
    than 0.2 off the leg bones) and minus what the thigh plates enclose. Weights, UVs and
    normals are kept; Unity shows it in place of Warrior_Body while armor is worn."""
    src = bpy.data.objects['Warrior_Body']
    me = src.data.copy()
    me.name = 'Warrior_Body_Armored'
    ob = bpy.data.objects.new('Warrior_Body_Armored', me)
    bpy.context.scene.collection.objects.link(ob)
    ob.matrix_world = src.matrix_world.copy()
    if not ob.vertex_groups:
        for g in src.vertex_groups:
            ob.vertex_groups.new(name=g.name)
    legs = [BONES[n] for n in ('UpperLeg.L', 'LowerLeg.L', 'UpperLeg.R', 'LowerLeg.R')]

    def off_leg(p):
        best = 9.0
        for h, t in legs:
            _, f = intersect_point_line(p, h, t)
            best = min(best, (p - h.lerp(t, min(1.0, max(0.0, f)))).length)
        return best

    co = [ob.matrix_world @ v.co for v in me.vertices]
    skirt = [p.z < 1.15 and off_leg(p) > 0.2 for p in co]
    inside = [0.76 < p.z < 1.1 and off_leg(p) <= 0.2 for p in co]
    for o in bpy.data.objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode='EDIT')
    bm = bmesh.from_edit_mesh(me)
    bm.verts.ensure_lookup_table()
    kill = [f for f in bm.faces if any(skirt[v.index] for v in f.verts) or all(inside[v.index] for v in f.verts)]
    print("ARMORED BODY drops", len(kill), "of", len(bm.faces), "faces")
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    bmesh.update_edit_mesh(me)
    bpy.ops.object.mode_set(mode='OBJECT')
    ob.parent = arm
    ob.modifiers.new('Armature', 'ARMATURE').object = arm
    return ob


def build_set(s, arm):
    col = bpy.data.collections.new(f"Armor_{s.T}")
    bpy.context.scene.collection.children.link(col)
    pieces = [chest(s), waist(s)]
    sided = [hip_cloth(s), shoulder(s), forearm(s), glove(s), thigh(s), knee(s), shin(s), boot(s)]
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


# ---------------------------------------------------------------- validation / previews

def world_tree(o, deps):
    ev = o.evaluated_get(deps)
    me = ev.to_mesh()
    verts = [o.matrix_world @ v.co for v in me.vertices]
    polys = [tuple(p.vertices) for p in me.polygons]
    ev.to_mesh_clear()
    return BVHTree.FromPolygons(verts, polys), verts, polys


def report_clipping(label, pairs):
    """Counts intersecting triangle pairs between posed meshes: armor the body pokes
    through, or a shield cutting into the hip. Prints only pairs that touch."""
    deps = bpy.context.evaluated_depsgraph_get()
    trees = {}
    for a, b in pairs:
        for o in (a, b):
            if o.name not in trees:
                trees[o.name] = world_tree(o, deps)
        ta, va, pa = trees[a.name]
        tb, _, _ = trees[b.name]
        hits = ta.overlap(tb)
        if hits:
            zs = [va[i].z for h, _ in hits for i in pa[h]]
            print("CLIP %-6s %-26s x %-26s %4d  z %.2f..%.2f" % (label, a.name, b.name, len(hits), min(zs), max(zs)))


# Tier -> (armor set, shield, weapon), as UnitGear dresses them.
TIERS = [
    ('Tier0_Commoner', None, None, 'Weapon_Pitchfork'),
    ('Tier1_Bronze', 'Armor_Leather', 'Shield_Bronze_Round', 'Weapon_Sword_Bronze'),
    ('Tier2_Silver', 'Armor_Iron', 'Shield_Iron_Round', 'Weapon_Sword_Steel'),
    ('Tier3_Gold', 'Armor_Gilded', 'Shield_Gilded_Tall', 'Weapon_Sword_Gilded'),
]
GEAR_PREFIXES = ('Shield_', 'Weapon_')


def show_tier(tier):
    _, armor, shield, weapon = TIERS[tier]
    for col in bpy.data.collections:
        if col.name.startswith('Armor_'):
            col.hide_render = col.name != armor
    for o in bpy.data.objects:
        if o.name.startswith(GEAR_PREFIXES):
            o.hide_render = o.name not in (shield, weapon)
    for name in ('Warrior_Body', 'ShoulderPad.L', 'ShoulderPad.R'):
        bpy.data.objects[name].hide_render = tier > 0
    bpy.data.objects['Warrior_Body_Armored'].hide_render = tier == 0
    for name in ('Face', 'Warrior_Sword'):
        bpy.data.objects[name].hide_render = True
    bpy.data.objects['Warrior_Head_Bare'].hide_render = False


def render_previews():
    scene = bpy.context.scene
    try:
        scene.render.engine = 'BLENDER_EEVEE'
    except TypeError:
        scene.render.engine = 'BLENDER_EEVEE_NEXT'
    scene.render.resolution_x = 560
    scene.render.resolution_y = 720
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
        'front': ((0, -12, 1.45), (D(90), 0, 0), 3.4),
        'quarter': ((8.5, -8.5, 1.45), (D(90), 0, D(45)), 3.4),
        'side': ((12, 0, 1.45), (D(90), 0, D(90)), 3.4),
        'back': ((0, 12, 1.45), (D(90), 0, D(180)), 3.4),
        'head': ((4.2, -11.2, 2.45), (D(90), 0, D(20)), 1.5),
        'hips': ((4.2, 11.2, 1.05), (D(90), 0, D(160)), 1.5),
        'weapon': ((0.3, -12, 1.8), (D(90), 0, 0), 1.6),
        'rts': ((0, -6.5, 12.5), (D(28), 0, 0), 6.0),
    }
    for f in os.listdir(PREVIEWS):
        if f.endswith('.png'):
            os.remove(os.path.join(PREVIEWS, f))
    for tier, (label, *_rest) in enumerate(TIERS):
        show_tier(tier)
        paths = []
        for shot, (loc, rot, size) in shots.items():
            co.location = loc
            co.rotation_euler = rot
            cam.ortho_scale = size
            scene.render.filepath = os.path.join(PREVIEWS, f"{label}_{shot}.png")
            bpy.ops.render.render(write_still=True)
            paths.append(scene.render.filepath)
        montage(paths, os.path.join(PREVIEWS, f"{label}_sheet.png"))


def montage(paths, out):
    """Side-by-side strip of same-sized renders, for checking a tier at a glance."""
    import numpy as np
    tiles = []
    for p in paths:
        img = bpy.data.images.load(p)
        w, h = img.size
        px = np.empty(w * h * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        tiles.append(px.reshape(h, w, 4))
        bpy.data.images.remove(img)
    strip = np.concatenate(tiles, axis=1)
    h, w = strip.shape[:2]
    img = bpy.data.images.new(os.path.basename(out), w, h, alpha=True)
    img.pixels.foreach_set(strip.ravel())
    img.filepath_raw = out
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def main():
    global WEAPON, SKULL
    arm = setup()
    idle = idle_pose()
    WEAPON = weapon_frame(arm)
    body_src = bpy.data.objects['Warrior_Body']
    SKULL = BVHTree.FromPolygons([body_src.matrix_world @ v.co for v in body_src.data.vertices],
                                 [tuple(p.vertices) for p in body_src.data.polygons])
    face_atlas()
    sets = []
    for tier in ('leather', 'iron', 'gilded'):
        s = Style(tier)
        objects, col = build_set(s, arm)
        export(arm, objects, f"Armor_{s.T}.fbx")
        sets.append((objects, col))
    gear = bpy.data.collections.new('Gear')
    bpy.context.scene.collection.children.link(gear)
    builders = [lambda: shield_round('Shield_Bronze_Round', 0.35, 'Metal_Bronze', 'Metal_Bronze'),
                lambda: shield_round('Shield_Iron_Round', 0.40, 'Metal_Iron', 'Metal_Iron'),
                shield_tall, pitchfork,
                lambda: sword('Bronze'), lambda: sword('Steel'), lambda: sword('Gilded')]
    for builder in builders:
        ob = builder().build(arm, gear)
        export(arm, [ob], ob.name + '.fbx')
    bare = bare_head(arm)
    export(arm, [bare], 'Warrior_Head_Bare.fbx')
    features = face_features().build(arm, bpy.context.scene.collection)
    export(arm, [features], 'Warrior_Face_Features.fbx')
    body = armored_body(arm)
    export(arm, [body], 'Warrior_Body_Armored.fbx')
    tri = 0
    for o in bpy.data.objects:
        if o.type == 'MESH' and o.name.startswith(('Armor_', 'Shield_', 'Weapon_')):
            o.data.calc_loop_triangles()
            tri += len(o.data.loop_triangles)
            print("MESH %-28s tris=%5d" % (o.name, len(o.data.loop_triangles)))
    print("TOTAL tris", tri)

    pose_with(arm, idle)
    obj = bpy.data.objects
    for (objects, col), (_, _, shield, _) in zip(sets, TIERS[1:]):
        pairs = [(o, body) for o in objects]
        pairs += [(obj[shield], o) for o in objects if o.name.split('_')[2] in ('Waist', 'HipCloth', 'Thigh', 'Knee', 'Chest')]
        pairs.append((obj[shield], body))
        report_clipping('idle', pairs)
    if RENDER:
        os.makedirs(PREVIEWS, exist_ok=True)
        render_previews()
    for o in bpy.data.objects:
        o.hide_render = False
    for col in bpy.data.collections:
        col.hide_render = False
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
    print("SAVED", BLEND_OUT)


main()
