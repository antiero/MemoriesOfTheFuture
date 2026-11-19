"""
Clean up the Object Capture mug scan for the Quest.

Run headless from Terminal:
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python Scans/Mug/clean_mug.py

Input : Scans/Mug/Mug.usdz (Apple Object Capture: mesh + tex0 / norm0 / ao0)
Output: MemoriesOfTheFuture/Assets/CupSpike/Models/Mug/
          Mug_Visual.fbx   watertight, ~3k tris, UVs, baked textures
          Mug_Proxy.fbx    watertight, ~500 tris, for occlusion / trigger zone
          Mug_Albedo.png, Mug_Normal.png, Mug_AO.png  (2048)
        Scans/Mug/out/
          report.json, preview_*.png, mug_clean.blend
"""
import bpy, bmesh, os, sys, json, math, traceback
from mathutils import Vector

# ---------------------------------------------------------------- settings
VOXEL_SIZE = 0.0015       # m: closes scan holes, keeps the handle hole open
VISUAL_TRIS = 3000
PROXY_TRIS = 500
TEX_SIZE = 2048
CAGE_EXTRUSION = 0.004    # m
MAX_RAY = 0.012           # m

def find_root():
    here = os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else ""
    for c in (os.path.join(here, "..", ".."), os.path.expanduser("~/Documents/GitHub/MemoriesOfTheFuture")):
        c = os.path.abspath(c)
        if os.path.exists(os.path.join(c, "Scans", "Mug", "Mug.usdz")):
            return c
    raise RuntimeError("Can't find Scans/Mug/Mug.usdz")

ROOT = find_root()
SRC = os.path.join(ROOT, "Scans", "Mug", "Mug.usdz")
OUT = os.path.join(ROOT, "Scans", "Mug", "out")
UNITY = os.path.join(ROOT, "MemoriesOfTheFuture", "Assets", "CupSpike", "Models", "Mug")
os.makedirs(OUT, exist_ok=True)
os.makedirs(UNITY, exist_ok=True)

report = {"source": SRC, "settings": {"voxel": VOXEL_SIZE, "visual_tris": VISUAL_TRIS,
          "proxy_tris": PROXY_TRIS, "tex": TEX_SIZE}, "steps": [], "errors": []}

def log(msg):
    print("[clean_mug]", msg)
    report["steps"].append(msg)

def save_report():
    with open(os.path.join(OUT, "report.json"), "w") as f:
        json.dump(report, f, indent=2)

# ---------------------------------------------------------------- helpers
def only_select(*objs, active=None):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or objs[0]

def tri_count(obj):
    me = obj.data
    me.calc_loop_triangles()
    return len(me.loop_triangles)

def mesh_stats(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    boundary = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    multi = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    nonman_v = sum(1 for v in bm.verts if not v.is_manifold)
    islands = 0
    seen = set()
    for f in bm.faces:
        if f.index in seen:
            continue
        islands += 1
        stack = [f]
        while stack:
            g = stack.pop()
            if g.index in seen:
                continue
            seen.add(g.index)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        stack.append(h)
    dims = list(obj.dimensions)
    bm.free()
    return {"verts": len(obj.data.vertices), "tris": tri_count(obj),
            "boundary_edges": boundary, "multi_face_edges": multi,
            "non_manifold_verts": nonman_v, "pieces": islands,
            "watertight": boundary == 0 and multi == 0 and nonman_v == 0,
            "dimensions_m": [round(d, 4) for d in dims]}

def decimate_to(obj, target):
    ratio = min(1.0, target / max(1, tri_count(obj)))
    m = obj.modifiers.new("Decimate", "DECIMATE")
    m.decimate_type = "COLLAPSE"
    m.ratio = ratio
    m.use_collapse_triangulate = True
    only_select(obj)
    bpy.ops.object.modifier_apply(modifier=m.name)

def voxel_remesh(obj, size):
    obj.data.remesh_voxel_size = size
    obj.data.remesh_voxel_adaptivity = 0.0
    only_select(obj)
    bpy.ops.object.voxel_remesh()

def keep_largest_piece(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    seen, groups = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        grp, stack = [], [f]
        while stack:
            g = stack.pop()
            if g.index in seen:
                continue
            seen.add(g.index); grp.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        stack.append(h)
        groups.append(grp)
    removed = 0
    if len(groups) > 1:
        groups.sort(key=len, reverse=True)
        doomed = [f for g in groups[1:] for f in g]
        removed = len(groups) - 1
        bmesh.ops.delete(bm, geom=doomed, context="FACES")
        loose = [v for v in bm.verts if not v.link_faces]
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return removed

def make_image(name, non_color=False):
    if name in bpy.data.images:
        bpy.data.images.remove(bpy.data.images[name])
    img = bpy.data.images.new(name, TEX_SIZE, TEX_SIZE, alpha=False, float_buffer=False)
    if non_color:
        img.colorspace_settings.name = "Non-Color"
    return img

def save_image(img, path):
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()

def load_image(path, non_color=False):
    img = bpy.data.images.load(path, check_existing=True)
    if non_color:
        img.colorspace_settings.name = "Non-Color"
    return img

# ---------------------------------------------------------------- 1. import
try:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.usd_import(filepath=SRC)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise RuntimeError("No mesh in the USDZ")
    only_select(*meshes)
    if len(meshes) > 1:
        bpy.ops.object.join()
    src = bpy.context.view_layer.objects.active
    # unparent, bake transforms
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in [o for o in bpy.context.scene.objects if o.type != "MESH"]:
        bpy.data.objects.remove(o, do_unlink=True)
    src.name = "Mug_Scan"

    # origin: bottom centre, at world origin
    ws = [src.matrix_world @ v.co for v in src.data.vertices]
    mn = Vector((min(p.x for p in ws), min(p.y for p in ws), min(p.z for p in ws)))
    mx = Vector((max(p.x for p in ws), max(p.y for p in ws), max(p.z for p in ws)))
    bpy.context.scene.cursor.location = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
    only_select(src)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    src.location = (0, 0, 0)
    bpy.ops.object.transform_apply(location=True)

    report["scan"] = mesh_stats(src)
    report["scan"]["uv_layers"] = [uv.name for uv in src.data.uv_layers]
    report["scan"]["materials"] = [m.name for m in src.data.materials if m]
    log(f"Imported scan: {report['scan']}")
except Exception:
    report["errors"].append("import: " + traceback.format_exc()); save_report(); raise

# textures come from the USDZ (Blender unpacks them); find by name on loaded images, else in the zip
def find_tex(tag):
    for img in bpy.data.images:
        if tag in img.name.lower() or tag in (img.filepath or "").lower():
            return img
    return None

# ---------------------------------------------------------------- previews
def setup_preview_scene():
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.show_cavity = True
    sc.render.resolution_x = sc.render.resolution_y = 900
    sc.render.film_transparent = False
    sc.world = sc.world or bpy.data.worlds.new("World")
    d = max(report["scan"]["dimensions_m"])
    h = report["scan"]["dimensions_m"][2]
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    sc.collection.objects.link(cam)
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = d * 1.35
    target = bpy.data.objects.new("PreviewTarget", None)
    sc.collection.objects.link(target)
    target.location = (0, 0, h / 2)
    c = cam.constraints.new("TRACK_TO")
    c.target = target
    c.track_axis = "TRACK_NEGATIVE_Z"; c.up_axis = "UP_Y"
    sc.camera = cam
    return cam, target, d, h

def render_preview(obj_to_show, name, color_type="TEXTURE", views=((1, -1.2, 0.6), (-1.0, -0.3, 1.6))):
    sc = bpy.context.scene
    cam = sc.camera
    d = max(report["scan"]["dimensions_m"])
    h = report["scan"]["dimensions_m"][2]
    for o in sc.objects:
        if o.type == "MESH":
            o.hide_render = (o != obj_to_show)
    sc.display.shading.color_type = color_type
    sc.display.shading.single_color = (0.75, 0.75, 0.72)
    for i, v in enumerate(views):
        cam.location = Vector(v).normalized() * d * 4 + Vector((0, 0, h / 2))
        sc.render.filepath = os.path.join(OUT, f"preview_{name}_{i}.png")
        bpy.ops.render.render(write_still=True)

try:
    setup_preview_scene()
    render_preview(src, "0_scan")
    render_preview(src, "0_scan_solid", color_type="SINGLE")
    log("Rendered scan previews")
except Exception:
    report["errors"].append("preview scan: " + traceback.format_exc())

# ---------------------------------------------------------------- 2. watertight high-res
try:
    hi = src.copy(); hi.data = src.data.copy(); hi.name = "Mug_Watertight"
    bpy.context.scene.collection.objects.link(hi)
    only_select(hi)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.00002)
    bpy.ops.mesh.select_all(action="DESELECT")
    bpy.ops.mesh.select_non_manifold(extend=False, use_wire=True, use_boundary=True,
                                     use_multi_face=False, use_non_contiguous=False, use_verts=False)
    bpy.ops.mesh.fill_holes(sides=0)
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    voxel_remesh(hi, VOXEL_SIZE)
    report["floaters_removed"] = keep_largest_piece(hi)
    report["watertight_hi"] = mesh_stats(hi)
    log(f"Voxel remesh: {report['watertight_hi']}")
except Exception:
    report["errors"].append("remesh: " + traceback.format_exc()); save_report(); raise

# ---------------------------------------------------------------- 3. visual + proxy
def make_lod(name, tris):
    o = hi.copy(); o.data = hi.data.copy(); o.name = name
    bpy.context.scene.collection.objects.link(o)
    decimate_to(o, tris)
    o.data.materials.clear()
    return o

try:
    vis = make_lod("Mug_Visual", VISUAL_TRIS)
    only_select(vis)
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(50))
    except Exception:
        bpy.ops.object.shade_smooth()
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")
    report["visual"] = mesh_stats(vis)
    log(f"Visual LOD: {report['visual']}")

    prx = make_lod("Mug_Proxy", PROXY_TRIS)
    only_select(prx)
    bpy.ops.object.shade_flat()
    report["proxy"] = mesh_stats(prx)
    log(f"Proxy LOD: {report['proxy']}")
except Exception:
    report["errors"].append("lods: " + traceback.format_exc()); save_report(); raise

# ---------------------------------------------------------------- 4. bake scan textures onto the visual mesh
def src_material(kind):
    """Material on the scan that exposes one texture for baking."""
    mat = bpy.data.materials.new(f"BakeSrc_{kind}")
    mat.use_nodes = True
    nt = mat.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    if kind == "normal":
        bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
        nm = nt.nodes.new("ShaderNodeNormalMap")
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = TEX["norm"]
        nt.links.new(tex.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
        nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
    else:
        em = nt.nodes.new("ShaderNodeEmission")
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = TEX[kind]
        nt.links.new(tex.outputs["Color"], em.inputs["Color"])
        nt.links.new(em.outputs[0], out.inputs["Surface"])
    return mat

def target_material(img):
    mat = bpy.data.materials.new("BakeTarget")
    mat.use_nodes = True
    nt = mat.node_tree
    node = nt.nodes.new("ShaderNodeTexImage"); node.image = img
    for n in nt.nodes: n.select = False
    node.select = True; nt.nodes.active = node
    return mat

def bake(kind, img, bake_type):
    src.data.materials.clear(); src.data.materials.append(src_material(kind))
    vis.data.materials.clear(); vis.data.materials.append(target_material(img))
    for o in bpy.context.scene.objects:
        o.hide_render = False
    only_select(src, vis, active=vis)
    kw = dict(type=bake_type, use_selected_to_active=True, cage_extrusion=CAGE_EXTRUSION,
              max_ray_distance=MAX_RAY, margin=8)
    if bake_type == "NORMAL":
        kw.update(normal_space="TANGENT")
    bpy.ops.object.bake(**kw)

try:
    TEX = {"tex": find_tex("tex0"), "norm": find_tex("norm0"), "ao": find_tex("ao0")}
    if TEX["tex"] is None:
        # fall back: textures shipped next to the script (unzipped from the USDZ)
        d = os.path.join(ROOT, "Scans", "Mug", "textures")
        for k, f, nc in (("tex", "tex0", False), ("norm", "norm0", True), ("ao", "ao0", True)):
            for fn in os.listdir(d):
                if f in fn:
                    TEX[k] = load_image(os.path.join(d, fn), nc)
    for k in ("norm", "ao"):
        if TEX[k] is not None:
            TEX[k].colorspace_settings.name = "Non-Color"
    report["textures_found"] = {k: (v.name if v else None) for k, v in TEX.items()}
    log(f"Scan textures: {report['textures_found']}")

    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 1
    sc.render.bake.use_pass_direct = False
    sc.render.bake.use_pass_indirect = False

    albedo = make_image("Mug_Albedo")
    bake("tex", albedo, "EMIT"); save_image(albedo, os.path.join(UNITY, "Mug_Albedo.png"))
    log("Baked albedo")
    if TEX["ao"] is not None:
        ao = make_image("Mug_AO", non_color=True)
        bake("ao", ao, "EMIT"); save_image(ao, os.path.join(UNITY, "Mug_AO.png"))
        log("Baked AO")
    if TEX["norm"] is not None:
        sc.cycles.samples = 4
        normal = make_image("Mug_Normal", non_color=True)
        bake("normal", normal, "NORMAL"); save_image(normal, os.path.join(UNITY, "Mug_Normal.png"))
        log("Baked normal")
except Exception:
    report["errors"].append("bake: " + traceback.format_exc())

# ---------------------------------------------------------------- 5. final material + export
try:
    mat = bpy.data.materials.new("Mug")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = 0.35
    a = nt.nodes.new("ShaderNodeTexImage"); a.image = load_image(os.path.join(UNITY, "Mug_Albedo.png"))
    nt.links.new(a.outputs["Color"], bsdf.inputs["Base Color"])
    npath = os.path.join(UNITY, "Mug_Normal.png")
    if os.path.exists(npath):
        n = nt.nodes.new("ShaderNodeTexImage"); n.image = load_image(npath, True)
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(n.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    nt.nodes.active = a
    vis.data.materials.clear(); vis.data.materials.append(mat)
    prx.data.materials.clear()

    common = dict(use_selection=True, object_types={"MESH"}, use_mesh_modifiers=True,
                  apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                  bake_space_transform=True, axis_forward="-Z", axis_up="Y",
                  path_mode="RELATIVE", embed_textures=False, mesh_smooth_type="FACE")
    only_select(vis)
    bpy.ops.export_scene.fbx(filepath=os.path.join(UNITY, "Mug_Visual.fbx"), **common)
    only_select(prx)
    bpy.ops.export_scene.fbx(filepath=os.path.join(UNITY, "Mug_Proxy.fbx"), **common)
    log("Exported FBX")
except Exception:
    report["errors"].append("export: " + traceback.format_exc())

# ---------------------------------------------------------------- 6. previews of the result
try:
    render_preview(vis, "1_visual")
    render_preview(vis, "1_visual_solid", color_type="SINGLE")
    render_preview(prx, "2_proxy_solid", color_type="SINGLE")
    log("Rendered result previews")
except Exception:
    report["errors"].append("preview result: " + traceback.format_exc())

try:
    for o in (src, hi, vis, prx):
        o.hide_render = False
    hi.hide_set(True); prx.location.x = max(report["scan"]["dimensions_m"]) * 1.5
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "mug_clean.blend"))
    prx.location.x = 0
except Exception:
    report["errors"].append("save blend: " + traceback.format_exc())

log("Done")
save_report()
