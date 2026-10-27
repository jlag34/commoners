"""Menu presentation for the hub's character preview (HomeUnitPreview): a relaxed,
ready stance with a slow looping idle, one action per equipment combination, on the
Warrior rig. Exports Resources/SwarmModels/Warrior_Menu.fbx (armature and actions only;
Unity binds the clips to the Warrior by bone path) and saves menu_pose.blend.

  Menu_Fork          pitchfork upright beside him, its butt planted on the plinth
  Menu_Fork_Shield   the same with the shield carried upright at his left side
  Menu_Sword         sword lowered diagonally outward, free left hand
  Menu_Sword_Shield  sword lowered, shield at his side

Legs, hips, spine and head are identical in all four, so switching gear only moves the
arms: the weapon picks the right arm, the shield (worn from armor tier 1) the left. The
rest pose and the gameplay clips are untouched.

Feet stay planted (two-bone IK to fixed ankles), the pitchfork's butt stays on one spot
(the fork sways about it and the hand follows the shaft), and every motion is periodic
in LOOP frames. Poses are solved here in armature space and keyed on every frame, so no
constraints have to survive the export.

Needs armor_sets.blend, so run build_armor.py first. Headless:
  /Applications/Blender.app/Contents/MacOS/Blender -b --python art/blender/menu_pose.py [-- [outfits] [--static] [--no-render]]
Outfits (keys of OUTFITS, default commoner and silver) are rendered in the static pose to
art/blender/previews/Menu_<outfit>.png and Menu_<outfit>_hands.png; --static stops there.
"""
import bpy, math, os, sys
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.abspath(os.path.join(HERE, "..", ".."))
MODELS = os.path.join(GAME, "Prototype", "SwarmPrototype", "Resources", "SwarmModels")
BLEND_IN = os.path.join(HERE, "armor_sets.blend")
BLEND_OUT = os.path.join(HERE, "menu_pose.blend")
PREVIEWS = os.path.join(HERE, "previews")
RENDER = "--no-render" not in sys.argv

D = math.radians
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
FRONT = -Y

FPS = 24
LOOP = 192          # 8 s: one weight shift, two breaths
FLOOR = 0.0         # sole height; the plinth top

# The hub camera looks at him from 20 degrees off his left (HomeUnitPreview turns the
# unit to 200), slightly from above: the shield faces it, the weapon hangs on the far side.
CAM_YAW, CAM_PITCH = -20, 9.3
CAM_DIR = Vector((-math.sin(D(CAM_YAW)), -math.cos(D(CAM_YAW)), 0))

# --- stance (wearer's left +X, front -Y): weight on the right leg, left foot forward
FEET = {'L': (Vector((0.28, -0.10)), 9), 'R': (Vector((-0.30, 0.075)), -13)}   # ankle x, y; toe-out degrees
PELVIS_SHIFT = Vector((-0.05, 0.015, -0.03))  # over the standing leg, knees softened
PELVIS = dict(roll=4.0, yaw=-6.0)             # right hip up; left hip turned forward with the left foot
SPINE = [('Abdomen', -2.0, 2.5, -1.0), ('Torso', -2.5, 4.0, -1.5)]   # bone, roll, yaw, pitch: shoulders counter the hips
NECK = (0.5, 5.0, 2.0)
HEAD = (3.0, 9.0, 3.0)                         # toward the camera, a slight tilt, chin a touch down

# --- equipment
FORK_BUTT = Vector((-0.62, -0.12, FLOOR))
FORK_GRIP = 1.2           # height of the right hand on the shaft
FORK_LEAN = (0.0, 12.0)   # degrees the top leans in toward him and forward, square to his forearm
SWORD_GRIP = Vector((-0.60, 0.20, 0.82))       # beside and a little behind the hip, arm nearly straight
SWORD_DIR = Vector((-0.28, -0.86, -0.41))       # grip -> tip: 24 degrees down, 18 out from straight ahead
SHIELD_UPPER = Vector((0.55, 0.0, -1.0))       # upper arm carrying the shield: down and out from the side
SHIELD_FOREARM = Vector((0.12, -0.30, -0.95))  # forearm hanging behind the shield, a little forward: upright at his side
SHIELD_FACING = Vector((0.77, -0.64, 0.0))     # face turned 50 degrees out from the front, beside him rather than before him
SHIELD_REACH = 0.5                             # shield radius to keep the shoulder clear of (the tall kite's top)
FORK_POLE = Vector((-0.7, 1.0, -0.2))          # right elbow back and out, before solve_grip turns it
SWORD_POLE = Vector((-0.5, 1.0, -0.1))
FREE_WRIST = Vector((0.03, -0.07, -0.80))      # free left wrist from the shoulder (the commoner mesh adds 12 deg out)

FINGER_BONES = ('Fist1', 'Fist2', 'Thumb1', 'Thumb2')
VARIANTS = [('Menu_Fork', 'fork', False), ('Menu_Fork_Shield', 'fork', True),
            ('Menu_Sword', 'sword', False), ('Menu_Sword_Shield', 'sword', True)]


# ---------------------------------------------------------------- rig

REST = {}      # bone -> armature-space rest matrix
TAIL = {}      # bone -> armature-space rest tail
PARENT = {}
ORDER = []     # parents before children
IDLE = {}      # bone -> Idle_Weapon local basis at frame 1
HELD = {}      # Idle_Weapon armature-space pose, for gear placed relative to it
WEAPON = Matrix()   # rest frame of the Warrior's sword (z grip -> tip): where gameplay's fist holds weapons
HOLD = Matrix()     # WEAPON's frame in Fist.R's space as gameplay holds it
SHIELD = Matrix()   # the shields' frame in the Idle_Weapon pose (build_armor.shield_frame)
ROLL = {}           # weapon -> (hand roll about the grip axis, elbow pole), solved once on the static pose


def load_rig():
    bpy.ops.wm.open_mainfile(filepath=BLEND_IN)
    arm = bpy.data.objects['CharacterArmature']
    assert all(abs(arm.matrix_world[i][j] - (i == j)) < 1e-5 for i in range(4) for j in range(4)), "armature expected at the origin"
    for b in arm.data.bones:
        REST[b.name] = b.matrix_local.copy()
        TAIL[b.name] = b.tail_local.copy()
        PARENT[b.name] = b.parent.name if b.parent else None
    ORDER.extend(sorted(REST, key=lambda n: len(arm.data.bones[n].parent_recursive)))
    idle = next(a for a in bpy.data.actions if a.name.endswith('|Idle_Weapon'))
    set_action(arm, idle, 1)
    for pb in arm.pose.bones:
        IDLE[pb.name] = pb.matrix_basis.copy()
    for b in ORDER:
        HELD[b] = inherited(HELD, b, IDLE[b])
    sw = bpy.data.objects['Warrior_Sword']
    arm.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    loc, r, _ = sw.matrix_world.decompose()
    arm.data.pose_position = 'POSE'
    bpy.context.view_layer.update()
    WEAPON[:] = Matrix.Translation(loc) @ r.to_matrix().to_4x4()
    HOLD[:] = HELD['Fist.R'].inverted() @ HELD['Weapon.R'] @ REST['Weapon.R'].inverted() @ WEAPON
    # As build_armor.shield_frame: strapped flat on the forearm, face square to it.
    fa = HELD['LowerArm.L'] @ REST['LowerArm.L'].inverted()
    a, b = fa @ head_rest('LowerArm.L'), fa @ TAIL['LowerArm.L']
    f = (b - a).normalized()
    n = Vector((math.sin(D(50)), -math.cos(D(50)), 0))     # build_armor.SHIELD_OUT
    n = (n - f * n.dot(f)).normalized()
    SHIELD[:] = zframe(a.lerp(b, 0.72) + n * 0.2, n, Z)                 # SHIELD_LIFT, SHIELD_BACK
    arm.animation_data.action = None
    return arm


def set_action(arm, action, frame_no):
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = action
    if hasattr(ad, 'action_slot') and ad.action_slot is None and len(getattr(action, 'slots', [])):
        ad.action_slot = action.slots[0]
    bpy.context.scene.frame_set(frame_no)
    bpy.context.view_layer.update()


def rel(b):
    return REST[PARENT[b]].inverted() @ REST[b] if PARENT[b] else REST[b].copy()


def inherited(P, b, basis=None):
    parent = P[PARENT[b]] if PARENT[b] else Matrix()
    return parent @ rel(b) @ (basis if basis is not None else Matrix())


def rest3(b):
    return REST[b].to_3x3()


def head_rest(b):
    return REST[b].translation.copy()


def rot(axis, deg):
    return Matrix.Rotation(D(deg), 3, axis)


def turn(P, b, R=None, loc=None):
    """Poses `b` from its parent, rotated by R (3x3, in world axes as at rest) about its
    head and moved by `loc` (world axes as at rest)."""
    basis = Matrix()
    if R is not None:
        basis = (rest3(b).inverted() @ R @ rest3(b)).to_4x4()
    if loc is not None:
        basis = Matrix.Translation(rest3(b).inverted() @ loc) @ basis
    P[b] = inherited(P, b, basis)


def place(P, b, R, head=None):
    """Poses `b` with world rotation R (from rest) at `head` (default: where its parent puts it)."""
    if head is None:
        head = inherited(P, b).translation
    P[b] = Matrix.Translation(head) @ (R @ rest3(b)).to_4x4()


def delta(P, b):
    """World rotation taking b's rest orientation to its pose."""
    return P[b].to_3x3() @ rest3(b).inverted()


def axes(a, b):
    """Rotation whose x is `a` and whose z is `b` made perpendicular to it."""
    x = a.normalized()
    z = (b - x * b.dot(x)).normalized()
    return Matrix((x, z.cross(x), z)).transposed()


def zframe(origin, z_dir, x_dir):
    """Frame at `origin` with z along z_dir and x toward x_dir (WEAPON's and the shields' layout)."""
    z = z_dir.normalized()
    x = (x_dir - z * x_dir.dot(z)).normalized()
    return Matrix.Translation(origin) @ Matrix((x, z.cross(x), z)).transposed().to_4x4()


def euler(roll=0.0, yaw=0.0, pitch=0.0):
    """Roll about the front axis (+ drops his left side), yaw about up (+ turns him to his
    left), pitch about the left axis (+ leans forward)."""
    return rot(Z, yaw) @ rot(FRONT, -roll) @ rot(X, pitch)


def two_bone(P, upper, lower, target, pole, pole_rest, twist=0.0):
    """Places upper and lower so lower's tail reaches `target`, the joint bending toward
    `pole`. The hinge is matched to the rest hinge (pole_rest x limb), so knees and
    elbows bend the way the mesh expects; `twist` turns the lower bone about itself.
    Returns how far the target was out of reach."""
    S = inherited(P, upper).translation
    J = head_rest(lower)
    l1 = (J - head_rest(upper)).length
    l2 = (TAIL[lower] - J).length
    d = target - S
    dist = min(max(d.length, abs(l1 - l2) + 1e-4), l1 + l2 - 1e-4)
    dn = d.normalized()
    a = (l1 * l1 - l2 * l2 + dist * dist) / (2 * dist)
    h = math.sqrt(max(l1 * l1 - a * a, 0.0))
    perp = (pole - dn * pole.dot(dn)).normalized()
    E = S + dn * a + perp * h
    T = S + dn * dist
    n = perp.cross(dn).normalized()
    n_r = pole_rest.cross((TAIL[lower] - head_rest(upper)).normalized()).normalized()
    R1 = axes(E - S, n) @ axes(J - head_rest(upper), n_r).inverted()
    R2 = axes(T - E, n) @ axes(TAIL[lower] - J, n_r).inverted()
    place(P, upper, R1, S)
    place(P, lower, rot((T - E).normalized(), twist) @ R2)
    return max(d.length - (l1 + l2), 0.0)


def fill(P, idle_bones=()):
    """Every bone not posed yet follows its parent: the right hand's fingers and Weapon.R
    as in Idle_Weapon (so the grip matches gameplay) plus `idle_bones`, the rest at rest."""
    for b in ORDER:
        if b not in P:
            idle = b.endswith('.R') and b.split('.')[0] in FINGER_BONES or b == 'Weapon.R' or b in idle_bones
            P[b] = inherited(P, b, IDLE[b] if idle else None)


def basis_of(P, b):
    return inherited(P, b).inverted() @ P[b]


# ---------------------------------------------------------------- the pose

def wave(t, cycles, phase=0.0):
    return math.sin(2 * math.pi * (cycles * t + phase))


def body(P, t, live):
    """Feet, legs, pelvis, spine, neck, head and clavicles at loop time t (0..1). `live`
    scales the idle motion (0 = the static pose)."""
    shift = wave(t, 1) * live            # weight drifts further onto the right leg and back
    breath = wave(t, 2, 0.1) * live      # two slow breaths
    look = wave(t, 1, 0.3) * live
    nod = wave(t, 2, 0.55) * live
    P['Root'] = REST['Root'].copy()
    for side, (xy, yaw) in FEET.items():
        foot = 'Foot.' + side
        place(P, foot, rot(Z, yaw), Vector((xy.x, xy.y, head_rest(foot).z)))
    loc = PELVIS_SHIFT + Vector((-0.018 * shift, 0.0, -0.006 * (1 + shift) * 0.5))
    turn(P, 'Body', euler(PELVIS['roll'] - 0.8 * shift, PELVIS['yaw'] + 0.6 * shift), loc)
    for side, (xy, yaw) in FEET.items():
        knee = rot(Z, yaw) @ FRONT + X * (0.15 if side == 'L' else -0.15)
        miss = two_bone(P, 'UpperLeg.' + side, 'LowerLeg.' + side, P['Foot.' + side].translation, knee, FRONT)
        if miss > 1e-3:
            print("WARN %s leg short by %.3f" % (side, miss))
    turn(P, 'Hips')
    for b, roll, yaw, pitch in SPINE:
        k = 0.4 if b == 'Abdomen' else 0.6
        turn(P, b, euler(roll + 0.5 * shift, yaw - 0.4 * shift, pitch - 2.0 * k * breath))
    turn(P, 'Neck', euler(NECK[0], NECK[1] + 0.6 * look, NECK[2] + 0.8 * breath))
    turn(P, 'Head', euler(HEAD[0] + 0.5 * look, HEAD[1] + 1.2 * look, HEAD[2] + 0.6 * breath + 0.7 * nod))
    for side, sign in (('L', 1), ('R', -1)):
        # Clavicles drop a little to relax and lift with each breath.
        turn(P, 'Shoulder.' + side, rot(Y, sign * (5.0 - 2.0 * breath)))


def wrist_strain(P):
    """Degrees the right hand bends off the forearm (swing) and turns about it (twist)."""
    q = (delta(P, 'LowerArm.R').inverted() @ delta(P, 'Fist.R')).to_quaternion()
    a = (TAIL['LowerArm.R'] - head_rest('LowerArm.R')).normalized()
    tw = Quaternion((q.w, *(a * Vector(q[1:]).dot(a))))
    if tw.magnitude < 1e-9:
        return 180.0, 0.0
    tw.normalize()
    swing = q @ tw.inverted()
    twist = math.degrees(2 * math.atan2(Vector(tw[1:]).dot(a), tw.w))
    twist = (twist + 180) % 360 - 180
    return math.degrees(2 * math.acos(min(1.0, abs(swing.w)))), twist


def strain_cost(swing, twist):
    """Bending the wrist off the forearm counts fully; twist (half of it the forearm's)
    is cheap up to about 35 degrees and steep beyond, near the end of the forearm's roll."""
    return swing + 0.5 * abs(twist) + 3.0 * max(0.0, abs(twist) - 35)


def hold(P, G, pole):
    """Right arm to a hand holding weapon frame G the way gameplay does; the forearm takes
    up half the wrist's twist."""
    fist = G @ HOLD.inverted()
    two_bone(P, 'UpperArm.R', 'LowerArm.R', fist.translation, pole, Y)
    _, twist = wrist_strain({**P, 'Fist.R': fist})
    two_bone(P, 'UpperArm.R', 'LowerArm.R', fist.translation, pole, Y, twist=0.5 * twist)
    P['Fist.R'] = fist


def grip(origin, axis, roll):
    """Weapon frame at `origin`, grip axis `axis`, turned `roll` degrees about it."""
    ref = (Z if abs(axis.normalized().z) < 0.9 else FRONT)
    return zframe(origin, axis, rot(axis.normalized(), roll) @ ref)


def solve_grip(P, origin, axis, pole):
    """Hand roll about the grip axis, and how far the elbow turns from `pole` about the
    shoulder-to-hand line, that strain the wrist least: bending off the forearm costs
    fully, twisting about it (shared with the forearm) less."""
    best = None
    S = inherited(P, 'UpperArm.R').translation
    for deg in range(0, 360, 3):
        G = grip(origin, axis, deg)
        line = ((G @ HOLD.inverted()).translation - S).normalized()
        for swing_pole in range(-90, 91, 10):
            trial = dict(P)
            p = rot(line, swing_pole) @ pole
            hold(trial, G, p)
            swing, twist = wrist_strain(trial)
            cost = strain_cost(swing, twist) + 0.05 * abs(swing_pole)
            if best is None or cost < best[0]:
                best = (cost, deg, swing_pole, swing, twist, p)
    print("GRIP cost %.1f roll %d elbow %d swing %.1f twist %.1f" % best[:5])
    return best[1], best[5]


def fork_axis(sway, drift):
    return (rot(Y, FORK_LEAN[0] + 0.5 * sway) @ rot(X, FORK_LEAN[1] + 0.4 * drift) @ Z).normalized()


def right_arm(P, t, weapon, live):
    sway = wave(t, 1, 0.15) * live
    drift = wave(t, 3, 0.4) * live
    if weapon == 'fork':
        # The fork rocks a little about its planted butt and the hand rides the shaft.
        u = fork_axis(sway, drift)
        reach = (FORK_GRIP - FORK_BUTT.z) / u.z
        if 'fork' not in ROLL:
            ROLL['fork'] = solve_grip(P, FORK_BUTT + u * reach, u, FORK_POLE)
        roll, pole = ROLL['fork']
        hold(P, grip(FORK_BUTT + u * reach, u, roll), pole)
        fill(P)
        # Weapon.R slides down the shaft (the butt is 0.85 below gameplay's grip) and
        # turns about it so the tines face the camera; the round fist hides both.
        P['Weapon.R'] = zframe(FORK_BUTT + u * 0.85, u, CAM_DIR.cross(Z)) @ WEAPON.inverted() @ REST['Weapon.R']
        return reach - 0.85
    axis = rot(Y, -1.0 * sway) @ rot(X, 1.2 * drift) @ SWORD_DIR
    origin = SWORD_GRIP + Vector((0.004 * sway, 0.0, 0.004 * drift))
    if 'sword' not in ROLL:
        ROLL['sword'] = solve_grip(P, origin, axis, SWORD_POLE)
    roll, pole = ROLL['sword']
    hold(P, grip(origin, axis, roll), pole)
    return 0.0


def shield_roll(R2, d2, E, S):
    """Forearm roll (degrees, from the elbow's hinge) that turns the strapped shield's
    face most toward SHIELD_FACING while the shoulder stays clear of the disc (the
    pauldron would cut through the rim); rolling far from the hinge costs a little."""
    mount = HELD['LowerArm.L'].inverted() @ SHIELD
    want = SHIELD_FACING.normalized()

    def score(a):
        F = Matrix.Translation(E) @ (rot(d2, a) @ R2 @ rest3('LowerArm.L')).to_4x4()
        shield = F @ mount
        n, c = shield.to_3x3() @ Z, shield.translation
        q = (S - c) - n * (S - c).dot(n)
        rim = c + q.normalized() * min(q.length, SHIELD_REACH)
        return n.dot(want) - 0.002 * abs(a) - 4.0 * max(0.0, 0.32 - (S - rim).length)

    best = max(range(-150, 151, 2), key=score)
    print("SHIELD roll %d" % best)
    return best


def left_arm(P, t, shield, live):
    breath = wave(t, 2, 0.1) * live
    drift = wave(t, 1, 0.65) * live
    S = inherited(P, 'UpperArm.L').translation
    if shield:
        # The shield is strapped flat on the forearm (build_armor.shield_frame): carry it
        # upright at his left side, the arm behind it, and roll the forearm until the face
        # turns toward SHIELD_FACING, keeping the pauldron clear of the rim.
        l1 = (head_rest('LowerArm.L') - head_rest('UpperArm.L')).length
        E = S + (rot(Y, 1.5 * drift) @ SHIELD_UPPER).normalized() * l1
        d2 = (rot(X, 1.2 * breath) @ SHIELD_FOREARM).normalized()
        n = (E - S).cross(d2).normalized()
        n_r = Y.cross((TAIL['LowerArm.L'] - head_rest('UpperArm.L')).normalized()).normalized()
        place(P, 'UpperArm.L', axes(E - S, n) @ axes(head_rest('LowerArm.L') - head_rest('UpperArm.L'), n_r).inverted(), S)
        R2 = axes(d2, n) @ axes(TAIL['LowerArm.L'] - head_rest('LowerArm.L'), n_r).inverted()
        if 'shield' not in ROLL:
            ROLL['shield'] = shield_roll(R2, d2, E, S)
        place(P, 'LowerArm.L', rot(d2, ROLL['shield']) @ R2)
        # A closed fist on the grip behind the boss, not a hand hanging under the rim.
        turn(P, 'Fist.L', rot(Y, 12))
        turn(P, 'Fist1.L', rot(Y, 70 + 3 * drift))
        turn(P, 'Fist2.L', rot(Y, 80))
        turn(P, 'Thumb1.L', rot(Y, 25))
        return ()
    wrist = S + FREE_WRIST + Vector((0.0, 0.006 * drift, 0.006 * breath))
    two_bone(P, 'UpperArm.L', 'LowerArm.L', wrist, Vector((0.3, 1.0, 0.0)), Y)
    place(P, 'Fist.L', rot(Y, 6) @ delta(P, 'LowerArm.L'))
    curl = 1.0 + 0.2 * drift
    turn(P, 'Fist1.L', rot(Y, 20 * curl))
    turn(P, 'Fist2.L', rot(Y, 25 * curl))
    turn(P, 'Thumb1.L', rot(Z, -10 * curl))
    return ()


def pose(t, weapon, shield, live=1.0):
    """Armature-space matrices of every bone at loop time t, and how far the pitchfork
    slid down through the hand."""
    P = {}
    body(P, t, live)
    left_idle = left_arm(P, t, shield, live)
    slide = right_arm(P, t, weapon, live)
    fill(P, left_idle)
    return P, slide


# ---------------------------------------------------------------- actions

def key_action(arm, name, weapon, shield):
    """Keys the loop on every frame (frame LOOP + 1 repeats frame 1) and reports how far
    the planted points wander: ankles and, with the pitchfork, the butt of the shaft."""
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = action
    last = {}
    planted = {}
    butt = WEAPON @ Vector((0, 0, -0.85))
    for f in range(LOOP + 1):
        P, _ = pose(f / LOOP, weapon, shield)
        points = {side: P['LowerLeg.' + side] @ Vector((0, (TAIL['LowerLeg.' + side] - head_rest('LowerLeg.' + side)).length, 0))
                  for side in 'LR'}
        points.update({'foot' + side: P['Foot.' + side].translation for side in 'LR'})
        if weapon == 'fork':
            points['butt'] = P['Weapon.R'] @ REST['Weapon.R'].inverted() @ butt
        for k, v in points.items():
            planted.setdefault(k, []).append(v)
        for pb in arm.pose.bones:
            loc, q, sc = basis_of(P, pb.name).decompose()
            if pb.name in last and q.dot(last[pb.name]) < 0:
                q.negate()
            last[pb.name] = q
            pb.rotation_mode = 'QUATERNION'
            pb.location, pb.rotation_quaternion, pb.scale = loc, q, sc
            for path in ('location', 'rotation_quaternion', 'scale'):
                pb.keyframe_insert(path, frame=f + 1, group=pb.name)
    for fc in action_fcurves(action):
        for k in fc.keyframe_points:
            k.interpolation = 'LINEAR'
    drift = {k: max((v - vs[0]).length for v in vs) for k, vs in planted.items()}
    print("KEYED %-18s drift %s" % (name, " ".join("%s %.4f" % kv for kv in drift.items())))
    return action


def action_fcurves(action):
    if hasattr(action, 'fcurves') and len(action.fcurves):
        return list(action.fcurves)
    out = []
    for layer in getattr(action, 'layers', []):
        for strip in layer.strips:
            for bag in strip.channelbags:
                out.extend(bag.fcurves)
    return out


def apply_static(arm, P):
    """Shows pose P on the armature (no keys), for previews and checks."""
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.matrix_basis = basis_of(P, pb.name)
    bpy.context.view_layer.update()


# ---------------------------------------------------------------- checks / previews

def check_pose(label, arm, objects, P):
    """Prints what the eye checks too: feet planted, fork butt on the floor, intersections."""
    deps = bpy.context.evaluated_depsgraph_get()

    def tree(o):
        ev = o.evaluated_get(deps)
        me = ev.to_mesh()
        vs = [o.matrix_world @ v.co for v in me.vertices]
        ps = [tuple(p.vertices) for p in me.polygons]
        ev.to_mesh_clear()
        return BVHTree.FromPolygons(vs, ps), vs, ps

    trees = {o.name: tree(o) for o in objects}
    for name, (_, vs, _) in trees.items():
        if name.startswith('Weapon_Pitchfork'):
            print("CHECK %-18s fork lowest z %.3f" % (label, min(v.z for v in vs)))
    names = list(trees)
    for i, a in enumerate(names):
        for b in names[i + 1:]:
            if not ({a, b} & {o.name for o in objects if o.name.startswith(('Weapon_', 'Shield_'))}):
                continue
            hits = trees[a][0].overlap(trees[b][0])
            if hits:
                zs = [trees[a][1][i].z for h, _ in hits for i in trees[a][2][h]]
                print("CHECK %-18s %-26s x %-26s %4d  z %.2f..%.2f" % (label, a, b, len(hits), min(zs), max(zs)))


def linear(c):
    return tuple(v ** 2.2 for v in c[:3]) + (1.0,)


def setup_render():
    """EEVEE stage like the hub's: dark teal backdrop, plinth top at the soles, soft key,
    cool rim. Unity is gamma space, so flat material colours go in as c**2.2."""
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in [e.identifier for e in
        bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE_NEXT'
    scene.render.resolution_x, scene.render.resolution_y = 640, 720
    scene.render.film_transparent = False
    scene.view_settings.view_transform = 'Standard'
    world = bpy.data.worlds.new('Menu')
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs['Color'].default_value = linear((0.1, 0.19, 0.2))
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = 1.0
    for m in bpy.data.materials:
        bsdf = m.node_tree.nodes.get('Principled BSDF') if m.use_nodes and m.node_tree else None
        if bsdf and not bsdf.inputs['Base Color'].is_linked:
            bsdf.inputs['Base Color'].default_value = linear(bsdf.inputs['Base Color'].default_value)
    for name, rx, rz, energy, colour in (('Key', 50, -35, 3.2, (1, 0.93, 0.82)), ('Fill', 75, 40, 1.2, (0.85, 0.92, 1)),
                                          ('Rim', 60, 160, 2.0, (0.55, 0.9, 0.85)), ('Face', 95, -10, 0.8, (1, 0.95, 0.9))):
        light = bpy.data.lights.new(name, 'SUN')
        light.energy = energy
        light.color = colour
        light.angle = D(20)
        lo = bpy.data.objects.new(name, light)
        lo.rotation_euler = (D(rx), 0, D(rz))
        scene.collection.objects.link(lo)
    bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=1.12, depth=0.08, location=(0, 0, FLOOR - 0.04))
    plinth = bpy.context.active_object
    plinth.name = 'Plinth'
    mat = bpy.data.materials.new('Plinth')
    mat.diffuse_color = linear((0.21, 0.38, 0.36))
    mat.use_nodes = True
    mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = linear((0.21, 0.38, 0.36))
    plinth.data.materials.append(mat)
    cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam'))
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 3.4
    scene.collection.objects.link(cam)
    scene.camera = cam
    return cam


def aim(cam, yaw, pitch=CAM_PITCH, centre=Vector((0, 0, 1.25)), scale=3.4):
    d = Vector((-math.sin(D(yaw)), -math.cos(D(yaw)), 0)) * math.cos(D(pitch)) + Z * math.sin(D(pitch))
    cam.location = centre + d * 12
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cam.data.ortho_scale = scale


# Outfits as UnitGear dresses them: armor tier, weapon object.
OUTFITS = {
    'commoner': (0, 'Weapon_Pitchfork'), 'commoner_sword': (0, 'Weapon_Sword_Bronze'),
    'bronze_fork': (1, 'Weapon_Pitchfork'), 'bronze': (1, 'Weapon_Sword_Bronze'),
    'silver': (2, 'Weapon_Sword_Steel'), 'gold': (3, 'Weapon_Sword_Gilded'),
}
ARMOR = [None, 'Armor_Leather', 'Armor_Iron', 'Armor_Gilded']
SHIELDS = [None, 'Shield_Bronze_Round', 'Shield_Iron_Round', 'Shield_Gilded_Tall']


def dress(outfit):
    """Shows one outfit; returns its visible meshes."""
    tier, weapon = OUTFITS[outfit]
    shown = {'Commoner_Head', weapon}
    if tier < 2:
        shown.add('Commoner_Hair')
    if tier == 0:
        shown.add('Commoner_Body')
    else:
        shown |= {'Warrior_Body_Armored', SHIELDS[tier]}
        shown |= {o.name for o in bpy.data.collections[ARMOR[tier]].objects}
    meshes = []
    for o in bpy.data.objects:
        if o.type == 'MESH' and o.name != 'Plinth':
            o.hide_render = o.name not in shown
            if not o.hide_render:
                meshes.append(o)
    for c in bpy.data.collections:
        c.hide_render = False
    return meshes


def variant_of(outfit):
    tier, weapon = OUTFITS[outfit]
    return ('fork' if 'Pitchfork' in weapon else 'sword'), tier > 0


def render(cam, label, views):
    scene = bpy.context.scene
    paths = []
    for name, yaw, pitch, centre, scale in views:
        aim(cam, yaw, pitch, centre, scale)
        scene.render.filepath = os.path.join(PREVIEWS, "Menu_%s_%s.png" % (label, name))
        bpy.ops.render.render(write_still=True)
        paths.append(scene.render.filepath)
    return paths


def montage(paths, out):
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
    img = bpy.data.images.new('montage', w, h, alpha=True)
    img.pixels.foreach_set(strip.ravel())
    img.filepath_raw = out
    img.file_format = 'PNG'
    img.save()
    print("PREVIEW", out)


STATIC_VIEWS = [('menu', CAM_YAW, CAM_PITCH, Vector((0, 0, 1.25)), 3.3), ('front', 0, 5, Vector((0, 0, 1.25)), 3.3),
                ('side', 90, 5, Vector((0, 0, 1.25)), 3.3), ('back', 200, 8, Vector((0, 0, 1.25)), 3.3)]


# ---------------------------------------------------------------- export

def export(arm, actions):
    for a in list(bpy.data.actions):
        if a not in actions:
            bpy.data.actions.remove(a)
    holder = bpy.data.objects.new('Menu_Clips', None)   # a second root keeps "CharacterArmature/" in Unity's clip paths
    bpy.context.scene.collection.objects.link(holder)
    for o in bpy.data.objects:
        o.select_set(o in (arm, holder))
    bpy.context.view_layer.objects.active = arm
    arm.animation_data.action = actions[0]
    path = os.path.join(MODELS, 'Warrior_Menu.fbx')
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'EMPTY'}, add_leaf_bones=False,
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                             use_armature_deform_only=False)
    bpy.data.objects.remove(holder)
    print("EXPORTED", path, [a.name for a in actions])


def main():
    arm = load_rig()
    bpy.context.scene.render.fps = FPS
    cam = setup_render() if RENDER else None
    outfits = [a for a in sys.argv[sys.argv.index('--') + 1:] if a in OUTFITS] if '--' in sys.argv else []
    outfits = outfits or ['commoner', 'silver']
    os.makedirs(PREVIEWS, exist_ok=True)
    for weapon in ('fork', 'sword'):
        pose(0.0, weapon, False, live=0.0)     # solves the grips on the static pose
    for outfit in outfits:
        weapon, shield = variant_of(outfit)
        P, slide = pose(0.0, weapon, shield, live=0.0)
        apply_static(arm, P)
        meshes = dress(outfit)
        if weapon == 'fork':
            print("FORK slide %.3f" % slide)
        check_pose(outfit, arm, meshes, P)
        if RENDER:
            montage(render(cam, outfit, STATIC_VIEWS), os.path.join(PREVIEWS, "Menu_%s.png" % outfit))
            hands = [('grip', CAM_YAW, CAM_PITCH, P['Fist.R'].translation, 0.9), ('grip_out', 80, 5, P['Fist.R'].translation, 0.9),
                     ('left', CAM_YAW, CAM_PITCH, P['Fist.L'].translation, 1.1), ('left_out', -80, 5, P['Fist.L'].translation, 1.1)]
            montage(render(cam, outfit, hands), os.path.join(PREVIEWS, "Menu_%s_hands.png" % outfit))
    if '--static' in sys.argv:
        return
    actions = [key_action(arm, name, weapon, shield) for name, weapon, shield in VARIANTS]
    export(arm, actions)
    bpy.context.preferences.filepaths.save_version = 0     # no .blend1 next to it
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
    print("SAVED", BLEND_OUT)


if __name__ == "__main__":
    main()
