"""Builds the Commoner's gear as skinned meshes on the Warrior.fbx armature, exports
one FBX per item into Resources/SwarmModels and saves armor_sets.blend next to this script.

  Armor_Leather/Iron/Gilded.fbx          armor sets, one mesh per piece
  Shield_Bronze_Round, Shield_Iron_Round, Shield_Gilded_Tall    rigid on the left forearm
  Weapon_Pitchfork, Weapon_Sword_Bronze/Steel/Gilded            rigid on Weapon.R
  Commoner_Head.fbx                      Commoner_Head (skull, nose, ears, face, neck) and Commoner_Hair
  Commoner_Body.fbx                      tunic, team tabard, belt, trousers, boots, bare arms and hands
  Warrior_Body_Armored.fbx               the Warrior body without its head and tunic skirt, worn under armor

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
from mathutils.interpolate import poly_3d_calc

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
    ("Skin_Natural", (0.94, 0.66, 0.47, 1)),
    ("Hair_Brown", (0.50, 0.29, 0.16, 1)),
    ("Cloth_Oatmeal", (0.90, 0.82, 0.66, 1)),
    ("Cloth_Taupe", (0.52, 0.40, 0.30, 1)),
    ("Leather_Boot", (0.40, 0.25, 0.17, 1)),
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
    """One exported mesh object. Parts are separate bmeshes, each rigid to a bone, or with
    bone None skinned like the nearest point of Warrior_Body (see body_weights); a tuple of
    bone names does the same limited to those bones, and a function of the vertex position
    returns its weights directly. Parts on Head are shrunk by HEAD_M first, so everything
    worn on the head fits the smaller head."""

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
            if bone == 'Head':
                bmesh.ops.transform(bm, matrix=HEAD_M, verts=bm.verts[:])
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
            if bones[bi] is None or isinstance(bones[bi], tuple) or callable(bones[bi]):
                field = bones[bi] if callable(bones[bi]) else lambda co, allow=bones[bi]: skin_weights(co, allow)
                for name, w in field(me.vertices[i].co).items():
                    groups.setdefault(name, []).append((i, w))
            else:
                groups.setdefault(bones[bi], []).append((i, 1.0))
        for mi in used:
            me.materials.append(bpy.data.materials[MATERIALS[mi][0]])
        for name, members in groups.items():
            g = ob.vertex_groups.get(name) or ob.vertex_groups.new(name=name)
            for i, w in members:
                g.add([i], w, 'REPLACE')
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
    if bone is None:
        return None
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


# --- face

SKULL = None   # BVH of the commoner skull (skull_bm) at rest, for laying hair and features on it
# Atlas cell (4x4 grid) and sRGB colour per feature; written to Textures/Face_Features.png.
FACE_SWATCH = {
    'pupil': ((2, 0), (0.12, 0.08, 0.06)),
    'glint': ((3, 0), (1.0, 1.0, 1.0)),
    'brow': ((1, 1), (0.3, 0.17, 0.09)),
    'lip': ((2, 1), (0.5, 0.27, 0.2)),
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
EYE_X, EYE_Z = 0.105, 2.47


def face_features(pc):
    """Eyes, brows and mouth after current/00_commoner_base.png, added to `pc` on Head:
    small dark eyes with a glint, heavy straight brows dipping inward, a faint smile."""
    for sx in (-1, 1):
        bm = pc.part('Head', bevel=0.0, hard=80)
        cx = sx * EYE_X
        face_ellipse(bm, cx, EYE_Z, 0.026, 0.04, 'pupil', 0.006)
        face_ellipse(bm, cx + 0.008, EYE_Z + 0.016, 0.008, 0.009, 'glint', 0.0085, seg=8)
        brow = [(sx * 0.03, 2.548), (sx * 0.075, 2.566), (sx * 0.125, 2.572), (sx * 0.172, 2.566)]
        face_strip(bm, brow, [0.05, 0.058, 0.056, 0.046], 'brow', 0.012)
    bm = pc.part('Head', bevel=0.0, hard=80)
    smile = [(f * 0.062, 2.238 + 0.009 * f * f) for f in (-1, -0.5, 0, 0.5, 1)]
    face_strip(bm, smile, [0.008, 0.011, 0.012, 0.011, 0.008], 'lip', 0.005)


def blink_key(ob):
    """Shape key 'Blink' on the built Commoner_Head: each eye's dark oval squashed into a
    line a little below its middle and its glint collapsed, both kept on the skin by
    ray casting the skull again. HomeUnitPreview plays it now and then."""
    me = ob.data
    inv = HEAD_M.inverted()
    glint_uv = Vector(face_uv('glint'))
    features = {i for i, m in enumerate(me.materials) if m.name == 'Face_Features'}
    uv = me.uv_layers.active.data
    glints = {me.loops[li].vertex_index for p in me.polygons if p.material_index in features
              for li in p.loop_indices if (Vector(uv[li].uv) - glint_uv).length < 0.01}
    ob.shape_key_add(name='Basis', from_mix=False)
    key = ob.shape_key_add(name='Blink', from_mix=False)
    eyes = {v for p in me.polygons if p.material_index in features for v in p.vertices}
    for i in eyes:
        co = inv @ me.vertices[i].co
        cx = EYE_X if co.x > 0 else -EYE_X
        if abs(co.x - cx) > 0.045 or abs(co.z - EYE_Z) > 0.06:
            continue
        x, z = co.x, EYE_Z - 0.008 + (co.z - EYE_Z) * 0.2
        if i in glints:
            x, z = cx + 0.008, EYE_Z - 0.008
        was, _, _, _ = SKULL.ray_cast(Vector((co.x, -1.0, co.z)), Vector((0, 1, 0)))
        now, _, _, _ = SKULL.ray_cast(Vector((x, -1.0, z)), Vector((0, 1, 0)))
        lift = co - was - (Vector((0, 0.003, 0)) if i in glints else Vector())
        key.data[i].co = HEAD_M @ (now + lift)


# --- commoner head
# After current/00_commoner_base.png: the Warrior's skull with a squarer jaw, nose and
# ears, scaled by HEAD_M toward the art's roughly five-head proportions. HEAD_M also
# shrinks every other part rigged to Head (face features, hair, helmets) the same way.

HEAD_S = 0.64                          # head size against the Warrior's
HEAD_PIVOT = Vector((0, 0.0, 2.12))    # chin height at the Head bone's root
HEAD_LIFT = 0.03                       # up onto the raised shoulders
HEAD_M = (Matrix.Translation(HEAD_PIVOT + Vector((0, 0, HEAD_LIFT))) @ Matrix.Scale(HEAD_S, 4)
          @ Matrix.Translation(-HEAD_PIVOT))


def smoothstep(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


# Head rings before HEAD_M, in the Warrior's head space (its skull spans the same box, so
# the nose, helmets and face layout still fit): (z, half-width, front, back, centre y,
# squareness). A broad flat face, square jaw and wide chin, after the art.
HEAD_RINGS = [(2.12, 0.19, 0.31, 0.02, 0.03, 3.0), (2.16, 0.235, 0.335, 0.11, 0.04, 3.0), (2.24, 0.262, 0.35, 0.25, 0.05, 2.8),
              (2.34, 0.28, 0.36, 0.34, 0.06, 2.6), (2.44, 0.29, 0.365, 0.38, 0.06, 2.5), (2.56, 0.295, 0.37, 0.39, 0.06, 2.5),
              (2.68, 0.29, 0.355, 0.385, 0.06, 2.4), (2.78, 0.26, 0.31, 0.34, 0.06, 2.3), (2.86, 0.19, 0.22, 0.25, 0.06, 2.2),
              (2.9, 0.08, 0.09, 0.1, 0.06, 2.0)]


def skull_bm():
    bm = bmesh.new()
    rings = [super_ring(z, w, df, db, cy, n=n, seg=20) for z, w, df, db, cy, n in HEAD_RINGS]
    ring_loft(bm, rings, MAT['Skin_Natural'], cap_first=True, cap_last=True)
    return bm


def flat_skin(bm):
    """Plain skin material and no UVs, so the face features' UV layer is the only one."""
    for layer in list(bm.loops.layers.uv.values()):
        bm.loops.layers.uv.remove(layer)
    for f in bm.faces:
        f.material_index = MAT['Skin_Natural']
    return bm


def nose_bm(arm):
    """The nose island of the Warrior's Face object (the rest is hair), in world space."""
    face = bpy.data.objects['Face']
    arm.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    M = face.matrix_world.copy()
    arm.data.pose_position = 'POSE'
    bpy.context.view_layer.update()
    bm = bmesh.new()
    bm.from_mesh(face.data)
    seen = set()
    drop = []
    for v in bm.verts:
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
        mid = sum((M @ c.co for c in comp), Vector()) / len(comp)
        if abs(mid.x) > 0.08 or mid.y > -0.25:      # hair strands and ponytail; the nose is front centre
            drop += comp
    bmesh.ops.delete(bm, geom=drop, context='VERTS')
    bmesh.ops.transform(bm, matrix=M, verts=bm.verts[:])
    # The art's nose is bigger: grown from where it meets the brow.
    root = Vector((0, -0.31, 2.5))
    for v in bm.verts:
        v.co = root + (v.co - root) * 1.25
    return flat_skin(bm)


def commoner_head(arm):
    """Skull, nose, ears and face features on Head, plus a neck skinned like the body's."""
    pc = Piece('Commoner_Head')
    pc.parts.append((skull_bm(), 'Head', 0.0, 80))
    pc.parts.append((nose_bm(arm), 'Head', 0.0, 80))
    ears = pc.part('Head', bevel=0.0, hard=80)
    for sx in (-1, 1):
        F = frame((sx * 0.265, 0.03, 2.40), Vector((sx, -0.45, 0)), UP)
        lathe(ears, [P(-0.03, 0.13, 0.08), P(0.04, 0.125, 0.076), P(0.075, 0.09, 0.052), P(0.088, 0)], F, 10,
              MAT['Skin_Natural'], cap_first=True)
    face_features(pc)
    # Thick neck (in final, unshrunk space) up under the jaw, flaring into the trapezius
    # below the collar: under armor it fills the chest plate's neck opening, where the
    # Warrior body's dark collar would otherwise show.
    neck = pc.part(None, bevel=0.0, hard=80)
    lathe(neck, [P(1.88, 0.22, 0.27, cx=0.02), P(1.95, 0.22, 0.26, cx=0.02), P(2.0, 0.21, 0.24, cx=0.02), P(2.06, 0.2, 0.2), P(2.12, 0.155, 0.16),
                 P(2.18, 0.14, 0.145), P(2.24, 0.12, 0.125), P(2.29, 0.095, 0.095)], frame((0, 0.05, 0)), 12, MAT['Skin_Natural'])
    return pc


# --- hair

HAIR_C = Vector((0, 0.06, 2.5))   # inside the skull; hair rays aim out from here
# Hairline elevation (degrees above HAIR_C) by azimuth from the front: a short forehead,
# sideburns in front of the ears, clear of the ear tops, down to the nape.
HAIRLINE = [(0, 30), (25, 26), (45, 14), (58, 10), (63, -20), (71, -20), (76, 8), (104, 8), (120, -24), (150, -34),
            (180, -38)]


def hairline(theta):
    a = abs((theta + 180) % 360 - 180)
    for (a0, e0), (a1, e1) in zip(HAIRLINE, HAIRLINE[1:]):
        if a <= a1:
            return e0 + (e1 - e0) * (a - a0) / (a1 - a0)
    return HAIRLINE[-1][1]


def head_dir(theta, phi):
    """Unit direction from HAIR_C; theta 0 = front, 90 = wearer's left; phi up from level."""
    t, p = D(theta), D(phi)
    return Vector((math.sin(t) * math.cos(p), -math.cos(t) * math.cos(p), math.sin(p)))


def on_skull(theta, phi, lift):
    d = head_dir(theta, phi)
    hit, _, _, _ = SKULL.ray_cast(HAIR_C + d * 2, -d)
    return hit + d * lift


def hair_lift(theta, f):
    """Height of the hair mass over the skull at row fraction f (0 hairline, 1 crown):
    thicker on top, with a quiff over the front."""
    front = math.exp(-(((theta + 180) % 360 - 180) / 55.0) ** 2)
    side = math.sin(D(theta)) ** 2 * (1 - f)       # close-cropped over the ears
    return 0.05 - 0.03 * side + 0.06 * f + 0.06 * front * math.sin(math.pi * min(1.0, 0.25 + f * 1.2))


# Locks over the cap: (theta, row fraction, half-width, length, rise off the cap, heading).
# Heading 0 runs up over the crown, 90 toward the wearer's left, 180 down. After the art:
# a quiff swept up off the forehead and a little to the wearer's left, locks combed back
# over the top, short sides swept back and layered locks hanging at the back.
LOCKS = [(-45, 0.05, 0.13, 0.1, 0.11, -40), (-18, 0.02, 0.15, 0.12, 0.13, -10), (10, 0.02, 0.16, 0.14, 0.14, 20),
         (36, 0.04, 0.15, 0.14, 0.12, 45), (58, 0.1, 0.12, 0.12, 0.08, 70),
         (-30, 0.38, 0.15, 0.2, 0.1, 10), (5, 0.38, 0.16, 0.22, 0.11, 20), (40, 0.4, 0.15, 0.2, 0.1, 40),
         (-60, 0.7, 0.13, 0.18, 0.07, 20), (0, 0.78, 0.14, 0.2, 0.07, 0), (60, 0.7, 0.13, 0.18, 0.07, -20),
         (120, 0.4, 0.13, 0.2, 0.06, 155), (-120, 0.4, 0.13, 0.2, 0.06, -155), (150, 0.6, 0.14, 0.22, 0.07, 172),
         (-150, 0.6, 0.14, 0.22, 0.07, -172), (180, 0.35, 0.14, 0.2, 0.06, 180), (160, 0.12, 0.11, 0.15, 0.04, 178),
         (-160, 0.12, 0.11, 0.15, 0.04, -178)]


def hair():
    """A faceted cap from the hairline to the crown plus blade-shaped locks over it."""
    pc = Piece('Commoner_Hair')
    bm = pc.part('Head', bevel=0.0, hard=24)
    mat = MAT['Hair_Brown']
    seg, rows = 20, 5
    grid = []
    for k in range(-1, rows):
        f = max(0, k) / rows
        ring = []
        for i in range(seg):
            theta = i * 360 / seg
            e0 = hairline(theta)
            phi = e0 + (88 - e0) * f
            lift = 0.004 if k < 0 else hair_lift(theta, f) + 0.012 * ((i + k) % 2)
            ring.append(bm.verts.new(on_skull(theta, phi, lift)))
        grid.append(ring)
    pole = bm.verts.new(on_skull(0, 90, hair_lift(0, 1.0)))
    faces = []
    for r0, r1 in zip(grid, grid[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            faces.append(bm.faces.new((r0[i], r0[j], r1[j], r1[i])))
    for i in range(seg):
        faces.append(bm.faces.new((grid[-1][i], grid[-1][(i + 1) % seg], pole)))
    for theta, f, w, length, rise, heading in LOCKS:
        e0 = hairline(theta)
        phi = e0 + (88 - e0) * f
        d = head_dir(theta, phi)
        t, p = D(theta), D(phi)
        e_t = Vector((math.cos(t), math.sin(t), 0))
        e_p = Vector((-math.sin(t) * math.sin(p), math.cos(t) * math.sin(p), math.cos(p)))
        h = D(heading)
        along = e_p * math.cos(h) + e_t * math.sin(h)
        across = e_t * math.cos(h) - e_p * math.sin(h)
        b = on_skull(theta, phi, hair_lift(theta, f) - 0.015)
        base = [bm.verts.new(b + across * x * w + d * y * 0.045) for x, y in ((-1, 0), (0, -1), (1, 0), (0, 1))]
        tip = bm.verts.new(b + along * length + d * rise)
        faces.append(bm.faces.new(list(reversed(base))))
        for i in range(4):
            faces.append(bm.faces.new((base[i], base[(i + 1) % 4], tip)))
    for fc in faces:
        fc.material_index = mat
    return pc


# --- commoner body
# Tunic, tabard, belt, trousers, boots, bare arms and hands as one mesh. Its parts have
# no bone of their own: Piece.build gives each vertex the weights of the nearest point
# on Warrior_Body, so the clothes bend the way the original body did in every clip.

WEIGHTS = None   # Warrior_Body at rest: (BVH, vertex positions, polygons, per-vertex weights)
LIMITED = {}     # bone-name tuple -> (BVH of the Warrior_Body faces those bones move, their indices)


def body_weights():
    body = bpy.data.objects['Warrior_Body']
    names = [g.name for g in body.vertex_groups]
    cos = [body.matrix_world @ v.co for v in body.data.vertices]
    polys = [tuple(p.vertices) for p in body.data.polygons]
    weights = []
    for v in body.data.vertices:
        w = {}
        for g in v.groups:
            if g.weight > 0.001:
                name = HAND_OF.get(names[g.group], names[g.group])
                w[name] = w.get(name, 0.0) + g.weight
        weights.append(w)
    return BVHTree.FromPolygons(cos, polys), cos, polys, weights


# A few Warrior_Body hand vertices ride the weapon bone; they go to the hand instead, so the
# menu idle can slide the pitchfork through the fist without dragging skin along.
HAND_OF = {'Weapon.R': 'Fist.R'}


def rehome_weights(ob):
    """Moves vertex weights of HAND_OF's bones onto the hand."""
    for src, dst in HAND_OF.items():
        g = ob.vertex_groups.get(src)
        if g is None:
            continue
        to = ob.vertex_groups.get(dst) or ob.vertex_groups.new(name=dst)
        for v in ob.data.vertices:
            w = next((e.weight for e in v.groups if e.group == g.index), 0.0)
            if w > 0:
                have = next((e.weight for e in v.groups if e.group == to.index), 0.0)
                to.add([v.index], have + w, 'REPLACE')
        ob.vertex_groups.remove(g)


def skin_weights(co, allow=None):
    """Weights at the nearest point of Warrior_Body, interpolated over its face; top four.
    With `allow`, the nearest point among faces those bones move, and only their weights:
    a wide boot cuff would otherwise reach the other leg and stretch between the two."""
    tree, cos, polys, weights = WEIGHTS
    if allow:
        if allow not in LIMITED:
            keep = [i for i, p in enumerate(polys) if any(weights[v].get(n, 0) > 0.1 for v in p for n in allow)]
            LIMITED[allow] = (BVHTree.FromPolygons(cos, [polys[i] for i in keep]), keep)
        sub, keep = LIMITED[allow]
        loc, _, fi, _ = sub.find_nearest(co)
        fi = keep[fi]
    else:
        loc, _, fi, _ = tree.find_nearest(co)
    vids = polys[fi]
    acc = {}
    for i, b in zip(vids, poly_3d_calc([cos[i] for i in vids], loc)):
        for name, w in weights[i].items():
            acc[name] = acc.get(name, 0.0) + b * w
    if allow and any(name in allow and w > 0.01 for name, w in acc.items()):
        acc = {name: w for name, w in acc.items() if name in allow}
    top = sorted(acc.items(), key=lambda kv: -kv[1])[:4]
    total = sum(w for _, w in top)
    return {name: w / total for name, w in top if w / total > 0.01}


def super_ring(z, w, df, db, cy, n=2.6, seg=24, cx=0.0):
    """Superellipse cross-section at height z: half-width w, front/back depths df/db
    from y = cy. Starts at the front and runs toward the wearer's left."""
    pts = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        s, c = math.sin(a), math.cos(a)
        x = w * math.copysign(abs(s) ** (2 / n), s)
        f = math.copysign(abs(c) ** (2 / n), c)
        pts.append(Vector((cx + x, cy - (df if f > 0 else db) * f, z)))
    return pts


def ring_loft(bm, rings, mat, cap_first=False, cap_last=False):
    vs = [[bm.verts.new(p) for p in r] for r in rings]
    n = len(vs[0])
    faces = []
    for r0, r1 in zip(vs, vs[1:]):
        for i in range(n):
            j = (i + 1) % n
            faces.append(bm.faces.new((r0[i], r0[j], r1[j], r1[i])))
    if cap_first:
        faces.append(bm.faces.new(list(reversed(vs[0]))))
    if cap_last:
        faces.append(bm.faces.new(vs[-1]))
    for f in faces:
        f.material_index = mat
    return faces


def slab(bm, outer, inner, mat, wrap=False):
    """Thin closed solid between two matching grids (rows of points); `wrap` joins each
    row's last column back to its first, for bands."""
    vo = [[bm.verts.new(p) for p in row] for row in outer]
    vi = [[bm.verts.new(p) for p in row] for row in inner]
    nr, nc = len(outer), len(outer[0])
    cols = list(range(nc)) + ([0] if wrap else [])
    faces = []
    for g in (vo, vi):
        for r in range(nr - 1):
            for a, b in zip(cols, cols[1:]):
                faces.append(bm.faces.new((g[r][a], g[r][b], g[r + 1][b], g[r + 1][a])))
    rims = [[(0, c) for c in cols], [(nr - 1, c) for c in cols]]
    if not wrap:
        rims += [[(r, 0) for r in range(nr)], [(r, nc - 1) for r in range(nr)]]
    for rim in rims:
        for (ra, ca), (rb, cb) in zip(rim, rim[1:]):
            faces.append(bm.faces.new((vo[ra][ca], vo[rb][cb], vi[rb][cb], vi[ra][ca])))
    for f in faces:
        f.material_index = mat
    return faces


def remeshed(bm, voxel, target):
    """Voxel remesh (merges overlapping shapes into one surface) then decimate to about
    `target` faces. Consumes bm; every face of the result gets bm's first material."""
    bm.faces.ensure_lookup_table()
    mat = bm.faces[0].material_index
    me = bpy.data.meshes.new('remesh_src')
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new('remesh_src', me)
    bpy.context.scene.collection.objects.link(ob)
    r = ob.modifiers.new('remesh', 'REMESH')
    r.mode = 'VOXEL'
    r.voxel_size = voxel
    deps = bpy.context.evaluated_depsgraph_get()
    dense = bpy.data.meshes.new_from_object(ob.evaluated_get(deps))
    ob.modifiers.clear()
    ob.data = dense
    d = ob.modifiers.new('decimate', 'DECIMATE')
    d.ratio = min(1.0, target / max(1, len(dense.polygons)))
    deps = bpy.context.evaluated_depsgraph_get()
    low = bpy.data.meshes.new_from_object(ob.evaluated_get(deps))
    out = bmesh.new()
    out.from_mesh(low)
    for f in out.faces:
        f.material_index = mat
    bpy.data.objects.remove(ob)
    for m in (me, dense, low):
        bpy.data.meshes.remove(m)
    return out


def tree_of(*bms):
    verts, polys = [], []
    for bm in bms:
        base = len(verts)
        verts += [v.co.copy() for v in bm.verts]
        bm.verts.index_update()
        polys += [tuple(base + v.index for v in f.verts) for f in bm.faces]
    return BVHTree.FromPolygons(verts, polys)


def lerp_bones(chain, z):
    """Point on a chain of bone segments (head, tail) at height z (chains run downward)."""
    for h, t in chain:
        if t.z <= z <= h.z:
            return h.lerp(t, (h.z - z) / (h.z - t.z))
    h, t = chain[0] if z > chain[0][0].z else chain[-1]
    return h.lerp(t, (h.z - z) / (h.z - t.z))


# Torso from inside the skirt up to the collar: (z, half-width, front, back, centre y).
# The chest stays narrower than the shoulders: rounded shoulder caps (SHOULDER) carry the
# sleeves, and the arms hang clear of the ribs.
TORSO = [(1.34, 0.322, 0.262, 0.27, 0.05), (1.47, 0.31, 0.255, 0.265, 0.05), (1.60, 0.33, 0.285, 0.275, 0.04),
         (1.74, 0.345, 0.31, 0.285, 0.03), (1.86, 0.33, 0.30, 0.28, 0.03), (1.97, 0.29, 0.265, 0.26, 0.03),
         (2.06, 0.23, 0.21, 0.215, 0.03), (2.13, 0.18, 0.17, 0.18, 0.03)]
SHOULDER = (0.05, 0.015, 0.145, 0.15)    # cap centre along the upper arm past the joint, lift; radii along / across
TORSO_BONES = ('Hips', 'Body', 'Abdomen', 'Torso', 'Neck', 'Head', 'Shoulder.L', 'Shoulder.R', 'UpperArm.L', 'UpperArm.R',
               'LowerArm.L', 'LowerArm.R')
SKIRT = [P(1.45, 0.258, 0.312), P(1.30, 0.295, 0.365), P(1.15, 0.335, 0.415), P(0.99, 0.37, 0.46)]
SLIT = 1.15     # the skirt splits into front and back flaps below this
SKIRT_M = frame((0, 0.05, 0))
TABARD_HALF = 0.23


def tunic(pc):
    """Body, rounded shoulders and short sleeves remeshed into one surface, cuffed sleeve
    hems, a collar, and the skirt below the belt split at the sides. The body keeps to the
    torso's bones; the skirt shares hem_weights with the tabard. Returns a BVH of it all."""
    cloth = MAT['Cloth_Oatmeal']
    bm = pc.part(TORSO_BONES, bevel=0.0, hard=89)
    raw = bmesh.new()
    ring_loft(raw, [super_ring(z, w, df, db, cy) for z, w, df, db, cy in TORSO], cloth, cap_first=True, cap_last=True)
    along, lift, ra, rc = SHOULDER
    for side in ('L', 'R'):
        h, t = BONES['UpperArm.' + side]
        d = (t - h).normalized()
        cap = frame(h + d * along + Vector((0, 0, lift)), d, UP)
        lathe(raw, [P(-ra, 0), P(-ra * 0.7, rc * 0.72), P(0, rc), P(ra * 0.7, rc * 0.72), P(ra, 0)], cap, 12, cloth)
        pts = [h.lerp(t, f) + Vector((0, 0, 0.015 * (1 - f))) for f in (0.05, 0.3, 0.6)]
        tube(raw, pts, [0.148, 0.145, 0.14], 12, cloth, UP)
    upper = remeshed(raw, 0.016, 560)
    for side in ('L', 'R'):
        h, t = BONES['UpperArm.' + side]
        M = frame(h.lerp(t, 0.6), t - h, UP)
        lathe(bm, shell([P(-0.03, 0.146), P(0.005, 0.148), P(0.012, 0.143)], 0.02), M, 12, cloth, closed=True)
    lathe(bm, shell([P(2.105, 0.185, 0.195, cx=0.002), P(2.14, 0.18, 0.19, cx=0.002)], 0.025), frame((0, 0.04, 0)), 16, cloth,
          closed=True)
    skirt = pc.part(hem_weights, bevel=0.0, hard=89)
    top = [p for p in SKIRT if p['t'] >= SLIT]
    low = [p for p in SKIRT if p['t'] <= SLIT]
    lathe(skirt, shell(top, 0.02), SKIRT_M, 24, cloth, closed=True)
    for arc in ((-80, 80), (100, 260)):       # front and back flaps; slits at the sides
        lathe(skirt, shell(low, 0.02), SKIRT_M, 10, cloth, arc=arc, closed=True, caps=True)
    tree = tree_of(upper, skirt)
    append(bm, upper)
    return tree


def hem_weights(co):
    """Torso weights that hand more and more to the thighs toward the hem, in one smooth
    field (left thigh on the left, right on the right). The skirt flaps and the tabard
    over them both use it, so they bend alike and the tabard always stays on top."""
    w = skin_weights(co, TORSO_BONES)
    share = 0.35 * smoothstep(1.3, 0.95, co.z)
    if share <= 0:
        return w
    left = smoothstep(-0.4, 0.4, co.x)       # gentle across the panel, so the hem doesn't fold into a V
    w = {name: x * (1 - share) for name, x in w.items()}
    w['UpperLeg.L'] = w.get('UpperLeg.L', 0.0) + share * left
    w['UpperLeg.R'] = w.get('UpperLeg.R', 0.0) + share * (1 - left)
    top = sorted(w.items(), key=lambda kv: -kv[1])[:4]
    total = sum(x for _, x in top)
    return {name: x / total for name, x in top if x / total > 0.01}


def append(bm, src):
    """Adds bmesh `src` (freed) to `bm`, keeping its material indices."""
    me = bpy.data.meshes.new('tmp_part')
    src.to_mesh(me)
    src.free()
    bm.from_mesh(me)
    bpy.data.meshes.remove(me)


def tabard(bm, tree):
    """Team-coloured panels front and back, hanging past the tunic hem, joined by straps
    over the shoulders. Laid on the tunic by ray casts."""
    blue = MAT['Team_Blue']
    xs = [TABARD_HALF * (2 * i / 4 - 1) for i in range(5)]
    zs = [2.1, 2.04, 1.97, 1.88, 1.76, 1.62, 1.48, 1.36, 1.24, 1.16, 0.93]

    def lay(start, d, out, inn):
        """Outer and inner cloth points where the ray from `start` along `d` meets the tunic,
        pushed off halfway between its normal and back up the ray, so the cloth clears the
        tunic where the shoulders slope away from the ray."""
        hit, n, _, _ = tree.ray_cast(start, d)
        if hit is None:
            return None
        if n.dot(d) > 0:
            n = -n
        n = (n - d).normalized()
        return hit + n * out, hit + n * inn

    def panel(sign, bottom):
        outer, inner = [], []
        last = {}
        for z in zs:
            z = max(z, bottom)
            ro, ri = [], []
            for x in xs:
                if z > SLIT:
                    lift = 0.01 * smoothstep(1.85, 2.05, z)
                    pair, zz = None, z
                    while pair is None and zz > SLIT:      # the top corners step down onto the shoulders
                        pair = lay(Vector((x, -2.0 * sign, zz)), Vector((0, sign, 0)), 0.026 + lift, 0.01 + lift)
                        zz -= 0.02
                    last[x] = pair
                else:
                    # Hangs straight past the flaring skirt instead of following its facets.
                    drop = Vector((0, -sign * 0.3 * (SLIT - z), 0))
                    pair = tuple(Vector((x, p.y, z)) + drop for p in last[x])
                ro.append(pair[0])
                ri.append(pair[1])
            outer.append(ro)
            inner.append(ri)
        slab(bm, outer, inner, blue)

    panel(1, 0.92)
    panel(-1, 0.97)
    for sx in (-1, 1):
        cols = [sx * x for x in (0.12, 0.155, 0.19, TABARD_HALF)]
        outer, inner = [], []
        for k in range(11):
            phi = D(20 + 140 * k / 10)      # well down onto both panels
            d = Vector((0, -math.cos(phi), math.sin(phi)))
            pairs = [lay(Vector((x, 0.03, 1.9)) + d * 2, -d, 0.037, 0.017) for x in cols]
            outer.append([p[0] for p in pairs])
            inner.append([p[1] for p in pairs])
        slab(bm, outer, inner, blue)


def belt(bm, tree):
    """Brown belt over tunic and tabard with a square iron buckle at the front."""
    leather = MAT['Leather_Boot']
    rows_o, rows_i = [], []
    for z in (1.425, 1.515):
        ro, ri = [], []
        for i in range(24):
            a = 2 * math.pi * i / 24
            d = Vector((math.sin(a), -math.cos(a), 0))
            c = Vector((0, 0.05, z))
            hit, _, _, _ = tree.ray_cast(c + d * 2, -d)
            ro.append(hit + d * 0.03)
            ri.append(hit + d * 0.01)
        rows_o.append(ro)
        rows_i.append(ri)
    slab(bm, rows_o, rows_i, leather, wrap=True)
    front = (rows_o[0][0] + rows_o[1][0]) / 2
    B = frame(front + Vector((0, -0.006, 0)), FRONT, LEFT)     # local x across, y up
    iron = MAT['Metal_Dark']
    for x, y, sx, sy in ((0, 0.052, 0.15, 0.026), (0, -0.052, 0.15, 0.026), (0.062, 0, 0.026, 0.13), (-0.062, 0, 0.026, 0.13),
                         (-0.012, 0, 0.016, 0.1)):
        box(bm, B @ Matrix.Translation((x, y, 0.012)), (sx, sy, 0.024), iron)


def trousers(pc):
    taupe = MAT['Cloth_Taupe']
    bm = pc.part(None, bevel=0.0, hard=89)
    ring_loft(bm, [super_ring(1.40, 0.30, 0.235, 0.265, 0.05), super_ring(1.18, 0.35, 0.25, 0.285, 0.05),
                   super_ring(1.02, 0.37, 0.24, 0.275, 0.05, n=2.2)], taupe, cap_last=True)
    for side, sx in (('L', 1), ('R', -1)):
        bm = pc.part(('Hips', 'Body', 'UpperLeg.' + side, 'LowerLeg.' + side), bevel=0.0, hard=89)
        chain = [BONES['UpperLeg.' + side], BONES['LowerLeg.' + side]]
        zs = [1.18, 1.02, 0.86, 0.72, 0.62, 0.55, 0.44, 0.32]
        radii = [0.19, 0.2, 0.194, 0.182, 0.174, 0.158, 0.115, 0.1]
        # A little outward, so the baggy legs part at the crotch.
        pts = [lerp_bones(chain, z) + Vector((sx * 0.012, 0, 0)) for z in zs]
        tube(bm, pts, radii, 12, taupe, LEFT)


# Boot foot from the heel toward the toes: (distance, half-width, height over the sole).
BOOT_FOOT = [(0.0, 0.12, 0.16), (0.08, 0.14, 0.2), (0.22, 0.158, 0.18), (0.36, 0.165, 0.155), (0.47, 0.158, 0.14),
             (0.54, 0.13, 0.115), (0.575, 0.07, 0.08)]
BOOT_HEEL = 0.17      # heel y; the toe ends 0.575 in front of it


def boots(pc):
    """Chunky work boots: shaft, folded cuff, rounded toe box, thick sole and an instep strap.
    Each follows only its own shin and foot, or the cuffs stretch up to the knee in a stride."""
    brown = MAT['Leather_Boot']
    dark = MAT['Leather_Dark']
    dome = [(math.cos(D(a)), math.sin(D(a)) ** 0.6) for a in range(0, 181, 30)]
    for side, sx in (('L', 1), ('R', -1)):
        bm = pc.part(('LowerLeg.' + side, 'Foot.' + side), bevel=0.0, hard=55)
        h, t = BONES['LowerLeg.' + side]
        chain = [(h, t)]
        rings = [super_ring(z, 0.125, 0.14, 0.135, lerp_bones(chain, z).y + 0.005, n=2.3, seg=12, cx=lerp_bones(chain, z).x)
                 for z in (0.08, 0.24, 0.46)]
        ring_loft(bm, rings, brown, cap_first=True, cap_last=True)
        c = lerp_bones(chain, 0.47)
        M = frame((c.x, c.y + 0.005, 0))
        lathe(bm, shell([P(0.40, 0.2, 0.19), P(0.48, 0.215, 0.205), P(0.565, 0.235, 0.225)], 0.06), M, 12, brown, closed=True)
        x0 = c.x + sx * 0.008
        F = frame((x0, BOOT_HEEL, 0.04), FRONT, LEFT)        # local z toward the toes, y up
        loft(bm, F, dome, [(d, w, ht) for d, w, ht in BOOT_FOOT], brown)
        loft(bm, F, dome, [(0.2, 0.163, 0.186), (0.27, 0.165, 0.175)], dark)       # instep strap
        rim = 0.016
        outline = [(x0 - w - rim, BOOT_HEEL - d) for d, w, _ in BOOT_FOOT] + [(x0, BOOT_HEEL - 0.575 - rim)] + \
                  [(x0 + w + rim, BOOT_HEEL - d) for d, w, _ in reversed(BOOT_FOOT)] + [(x0, BOOT_HEEL + rim)]
        plate(bm, outline, 0.045, Matrix(), dark, z0=-0.002)


def arms(bm):
    """Bare, muscular arms from inside the sleeves to the wrists."""
    skin = MAT['Skin_Natural']
    for side in ('L', 'R'):
        h0, t0 = BONES['UpperArm.' + side]
        h1, t1 = BONES['LowerArm.' + side]
        pts = [h0.lerp(t0, 0.32), h0.lerp(t0, 0.6), h0.lerp(t0, 0.85), t0, h1.lerp(t1, 0.12), h1.lerp(t1, 0.38), h1.lerp(t1, 0.7),
               h1.lerp(t1, 1.02)]
        tube(bm, pts, [0.105, 0.11, 0.1, 0.094, 0.1, 0.097, 0.085, 0.072], 10, skin, UP)


def tube_auto(bm, pts, radii, seg, mat):
    """tube() with its side taken from the plane of the (roughly planar) path."""
    side = (pts[1] - pts[0]).cross(pts[-1] - pts[0])
    if side.length < 1e-6:
        side = (pts[-1] - pts[0]).orthogonal()
    return tube(bm, pts, radii, seg, mat, side.normalized())


def open_hand(bm, sx):
    """The free hand (sx 1 = left): a big open hand with a palm block, four fingers along the
    Fist bones and a thumb along Thumb1/2, in the rest pose (palm down, thumb to the front)."""
    skin = MAT['Skin_Natural']
    wrist = BONES['Fist.L'][0]
    section = [(math.cos(D(a)), math.sin(D(a))) for a in range(0, 360, 36)]

    def at(u, v, w):
        # u toward the fingertips, v toward the thumb, w the back of the hand
        return Vector((sx * (wrist.x + u), wrist.y - v, wrist.z + w))
    palm = [[at(u, v0 + hw * x, ht * y) for x, y in section]
            for u, hw, ht, v0 in ((-0.03, 0.07, 0.055, 0.0), (0.02, 0.092, 0.052, 0.005), (0.11, 0.098, 0.048, 0.008),
                                  (0.15, 0.09, 0.04, 0.008))]
    ring_loft(bm, palm, skin, cap_first=True, cap_last=True)
    for v, length, r in ((0.062, 0.18, 0.03), (0.021, 0.19, 0.031), (-0.021, 0.18, 0.03), (-0.062, 0.15, 0.027)):
        pts = [at(0.12 + length * f, v + 0.008, -0.01 - 0.04 * f * f) for f in (0.0, 0.45, 0.8, 0.93, 1.0)]
        tube_auto(bm, pts, [r, r * 0.97, r * 0.92, r * 0.75, 0.0], 6, skin)
    pts = [at(0.02, 0.075, -0.015), at(0.075, 0.12, -0.05), at(0.13, 0.15, -0.09), at(0.15, 0.156, -0.1), at(0.16, 0.16, -0.108)]
    tube_auto(bm, pts, [0.037, 0.034, 0.03, 0.023, 0.0], 6, skin)


def commoner_body():
    pc = Piece('Commoner_Body')
    tree = tunic(pc)
    # Weighted like the tunic right under it, so it moves with the cloth it lies on.
    tabard(pc.part(lambda co: hem_weights(tree.find_nearest(co)[0]), bevel=0.0, hard=60), tree)
    trousers(pc)
    boots(pc)
    skin = pc.part(None, bevel=0.0, hard=89)
    arms(skin)
    open_hand(skin, 1)
    fist(pc)
    clothed = tree_of(*[b for b, _, _, _ in pc.parts[:3]])
    belt(pc.part(TORSO_BONES, bevel=0.006, hard=50), clothed)
    return pc


FIST_R = 0.073     # finger tubes' centre radius around the grip (pitchfork and sword grips are <= 0.047)


def fist(pc):
    """The right hand closed around the weapon grip: four finger rings, a palm out to the
    wrist and a thumb over the index finger. Built around the grip in the Idle_Weapon pose
    and rigid to the hand (Fist.R). Weapon.R keeps a fixed offset from it in the gameplay
    clips, so the fist holds the grip there; the menu idle slides the pitchfork through it."""
    skin = MAT['Skin_Natural']
    bm = pc.part('Fist.R', bevel=0.0, hard=70)
    held = POSE['Weapon.R']
    grip = held @ WEAPON.translation
    u = (held.to_3x3() @ WEAPON.to_3x3() @ Vector((0, 0, 1))).normalized()      # toward the tines
    wrist = POSE['Fist.R'] @ BONES['Fist.R'][0]
    w = wrist - grip
    w = (w - u * w.dot(u)).normalized()                                        # from the grip toward the wrist
    v = u.cross(w)

    def around(s, ang, r=FIST_R):
        a = D(ang)
        return grip + u * s + (w * math.cos(a) + v * math.sin(a)) * r

    for s, r in ((0.078, 0.026), (0.026, 0.028), (-0.026, 0.027), (-0.078, 0.024)):
        pts = [around(s, a) for a in range(25, 336, 31)]
        tube_auto(bm, pts, [r * 0.9] + [r] * (len(pts) - 2) + [r * 0.85], 6, skin)
    # Palm from inside the forearm's end to the finger rings, a fist's height along the grip.
    start, end = wrist - w * 0.02, grip + w * (FIST_R * 0.75)
    d = (end - start).normalized()
    across = (u - d * u.dot(d)).normalized()
    up = d.cross(across)
    section = [(math.cos(D(a)), math.sin(D(a))) for a in range(0, 360, 36)]
    rings = [[c + across * (x * hw) + up * (y * ht) for x, y in section]
             for c, hw, ht in ((start, 0.07, 0.055), (start.lerp(end, 0.5), 0.1, 0.055), (end, 0.105, 0.05))]
    ring_loft(bm, rings, skin, cap_first=True, cap_last=True)
    thumb = [wrist.lerp(grip, 0.35) + u * 0.07 + v * 0.03, around(0.1, -40, FIST_R + 0.012),
             around(0.095, -90, FIST_R + 0.02), around(0.088, -135, FIST_R + 0.016), around(0.08, -165, FIST_R + 0.008)]
    tube_auto(bm, thumb, [0.034, 0.032, 0.03, 0.026, 0.02], 6, skin)
    back = POSE['Fist.R'].inverted()
    bmesh.ops.transform(bm, matrix=back, verts=bm.verts[:])


def swing_arm(ob, side, degrees):
    """Bakes the free arm a little away from the body: each vertex turns about the shoulder
    joint by its weight on the arm's bones, so every clip leaves the arm hanging clear of
    the torso instead of pressed into its side."""
    chain = {b + side for b in ('UpperArm.', 'LowerArm.', 'Fist.', 'Fist1.', 'Fist2.', 'Thumb1.', 'Thumb2.')}
    pivot = BONES['UpperArm.' + side][0]
    turn = Matrix.Translation(pivot) @ Matrix.Rotation(D(degrees), 4, FRONT if side == 'L' else BACK) @ \
        Matrix.Translation(-pivot)
    idx = {g.index for g in ob.vertex_groups if g.name in chain}
    for vert in ob.data.vertices:
        wsum = sum(g.weight for g in vert.groups if g.group in idx)
        if wsum > 0:
            vert.co = vert.co.lerp(turn @ vert.co, wsum)


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


def shield_frame(lift=0.95, offset=0.26, out=28):
    """Local z faces out of the shield, local x is its top. Designed in the idle pose:
    upright in front of the left forearm, facing front and turned `out` degrees
    outward, clear of the gauntlet; then mapped back to rest so the skinned mesh lands
    there. Following the forearm's own tilt instead leans the top into the shoulder.
    `lift` is how far along the forearm the centre sits: near the wrist, so the fist is
    behind the boss and doesn't hang out under the rim."""
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
    M = shield_frame(lift=0.9, offset=0.27)
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


FORK_DROP = 0.45   # how far the fork sits below the grip compared with a sword: the hand holds
                   # it with more shaft below the hand than above, toward the manifest's grip
                   # 1.15 m up a 1.95 m shaft, while the butt still clears the ground at a run


def pitchfork():
    """Weapon_Pitchfork after core_v1/02_equipment.png: three-tine iron head on a wooden
    shaft, held just above the middle, tines on the blade side so they point up at idle."""
    pc = Piece('Weapon_Pitchfork')
    W = WEAPON @ Matrix.Translation((0, 0, -FORK_DROP))
    shaft = pc.part('Weapon.R', bevel=0.0)
    lathe(shaft, [P(-0.4, 0), P(-0.39, 0.03), P(-0.36, 0.04), P(1.08, 0.034), P(1.1, 0)], W, 8, MAT['Wood_Brown'], offset=22.5)
    band(shaft, W, FORK_DROP - 0.15, FORK_DROP + 0.15, 0.044, 0.044, 0.014, 8, MAT['Leather_Dark'], offset=22.5)
    band(shaft, W, -0.33, -0.26, 0.044, 0.044, 0.014, 8, MAT['Leather_Dark'], offset=22.5)
    head = pc.part('Weapon.R', bevel=0.0, hard=40)
    iron = MAT['Metal_Dark']
    lathe(head, [P(1.0, 0.04), P(1.12, 0.048), P(1.17, 0.042)], W, 8, iron, cap_first=True, cap_last=True, offset=22.5)
    rivet(head, frame(W @ Vector((0, -0.046, 1.08)), WEAPON.to_3x3() @ Vector((0, -1, 0))), r=0.018, h=0.018, mat=iron)
    side = WEAPON.to_3x3() @ Vector((0, 1, 0))
    # The U-shaped bar that carries the outer tines, then the three tines.
    bar = [(-0.16, 1.3), (-0.155, 1.235), (-0.12, 1.185), (-0.06, 1.165), (0, 1.16), (0.06, 1.165), (0.12, 1.185), (0.155, 1.235), (0.16, 1.3)]
    tube(head, [W @ Vector((x, 0, z)) for x, z in bar], [0.028] * len(bar), 6, iron, side)
    tube(head, [W @ Vector((0, 0, z)) for z in (1.15, 1.3, 1.45, 1.6, 1.66)], [0.03, 0.027, 0.023, 0.014, 0], 6, iron, side)
    for sx in (-1, 1):
        tine = [(0.16, 1.29), (0.168, 1.42), (0.165, 1.52), (0.155, 1.6), (0.148, 1.645)]
        tube(head, [W @ Vector((sx * x, 0, z)) for x, z in tine], [0.027, 0.024, 0.02, 0.013, 0], 6, iron, side)
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
    """Warrior_Body minus its head and collar (Commoner_Head replaces them), its tunic skirt and flaps
    (faces near the hips that hang more than 0.2 off the leg bones) and what the thigh
    plates enclose. Weights, UVs and normals are kept; Unity shows it in place of
    Warrior_Body while armor is worn."""
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
    gi = src.vertex_groups['Head'].index
    head = [next((g.weight for g in v.groups if g.group == gi), 0.0) > 0.5 for v in me.vertices]
    skirt = [p.z < 1.15 and off_leg(p) > 0.2 for p in co]
    inside = [0.76 < p.z < 1.1 and off_leg(p) <= 0.2 for p in co]
    for o in bpy.data.objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode='EDIT')
    bm = bmesh.from_edit_mesh(me)
    bm.verts.ensure_lookup_table()
    # The neck ring and trapezius tops too: their dark collar texture showed above the chest
    # plates once the smaller head stopped covering them; the commoner neck fills the gap.
    collar = [p.z > 1.95 and abs(p.x) < 0.36 for p in co]
    kill = [f for f in bm.faces if any(skirt[v.index] for v in f.verts) or all(inside[v.index] for v in f.verts)
            or all(head[v.index] or collar[v.index] for v in f.verts)]
    print("ARMORED BODY drops", len(kill), "of", len(bm.faces), "faces")
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    bmesh.update_edit_mesh(me)
    bpy.ops.object.mode_set(mode='OBJECT')
    rehome_weights(ob)
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
    for name in ('Warrior_Body', 'ShoulderPad.L', 'ShoulderPad.R', 'Face', 'Warrior_Sword'):
        bpy.data.objects[name].hide_render = True
    bpy.data.objects['Commoner_Body'].hide_render = tier > 0
    bpy.data.objects['Warrior_Body_Armored'].hide_render = tier == 0
    bpy.data.objects['Commoner_Head'].hide_render = False
    bpy.data.objects['Commoner_Hair'].hide_render = TIERS[tier][1] not in (None, 'Armor_Leather')     # under a helmet


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
    global WEAPON, SKULL, WEIGHTS
    arm = setup()
    idle = idle_pose()
    WEAPON = weapon_frame(arm)
    WEIGHTS = body_weights()
    SKULL = tree_of(skull_bm())
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
    scene_col = bpy.context.scene.collection
    head = commoner_head(arm).build(arm, scene_col)
    blink_key(head)
    export(arm, [head, hair().build(arm, scene_col)], 'Commoner_Head.fbx')
    body_ob = commoner_body().build(arm, scene_col)
    swing_arm(body_ob, 'L', 12)
    export(arm, [body_ob], 'Commoner_Body.fbx')
    body = armored_body(arm)
    export(arm, [body], 'Warrior_Body_Armored.fbx')
    tri = 0
    for o in bpy.data.objects:
        if o.type == 'MESH' and o.name.startswith(('Armor_', 'Shield_', 'Weapon_', 'Commoner_')):
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
