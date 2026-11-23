# 02-Mug: the screaming mug

The headset scene for the mug: it finds the real mug on the table, screams when touched, opens into a tunnel when you look in, and pulls you into the dream when you hold it. It has the same platform setup as `01-PictureChanges`.

## Scene

| Object | What it does |
| --- | --- |
| `Directional Light` | Basic key light for virtual content |
| `MRUK` | Meta MR Utility Kit (room / scene understanding) |
| `PassthroughCamera` | `PassthroughCameraAccess` at 1280x960, for any camera-feed work (e.g. finding the cup) |
| `OVRCameraRig` | Head and hands, with Insight passthrough on, an `OVRPassthroughLayer`, and the `OVRComprehensiveInteractionRig` |
| `MugRoot` | Parent for the mug content, about 1 m up and 0.6 m in front of the origin |
| `MugRoot/CupDetection` | Permissions (`PictureChangesBootstrap`), `EnvironmentRaycastManager`, `CupDetector`, `CupDebugView`, `MugMenu` |
| `HandMenu` | Left hand menu (`HandMenu` + `SceneMenu`), same as in `01-PictureChanges` but with only the scene buttons |
| `MugRoot/ProxyMug` | To-scale stand-in for the client's mug (prefab instance), turned so the logo faces the origin |

## The scream

`MugScream` (on `CupDetection`): the mug that screams when touched, built as an escalation rather than a single trigger.

1. **Approach** (a fingertip from 30 cm down to the touch zone): a breath from inside the mug, quiet for most of the approach and then rushing in (volume follows the square of how close you are). Its pitch rises from 0.9 to 1.15, and the background drains away by up to 85%.
2. **Pull away** before touching: the breath cuts out dead.
3. **Touch** (a fingertip within **3 cm** of the mug's surface): 0.35 s of total silence, with the background cut too. Then the scream, and `onScream` fires, which triggers **RoomDrain**.

Details:
- **Distance:** measured from either index fingertip to the mug's body, modelled as a 40 mm radius, 95 mm tall cylinder.
- **Zone button:** switches the touch distance between **3 cm** ("contact", the default) and **10 cm** ("almost touching"), and is lit at 10 cm.
- **Breath sound:** `Audio/Breath_placeholder.wav` is a synthesised 4 s loop; replace the Breath clip on `MugScream` with a real one.

**RoomDrain** ("Drain and crush" from the concepts PDF): the passthrough's built-in colour adjustment.
- It takes 0.12 s to reach saturation −1, contrast +0.65 and brightness −0.15.
- It holds for 0.9 s, then recovers over 1.6 s.
- Peak and timings are on the component.
- **Where the zone is:**
  - It follows the tracked mug.
  - If tracking drops, it stays at the last known position, because a mug on a table doesn't move by itself.
  - After a put-down it stays where the hand let go.
- **Pin fallback:** press **Pin**, then pinch at the side of the mug, about half-way up, to fix the zone there. It then works with no detection at all, as the guaranteed demo. Press Pin again to go back to tracking.
- **When it fires:** never while the mug is held (holding it leads to the dream instead). It re-arms once the scream has finished, 2 s have passed, and every fingertip is 5 cm outside the touch zone.
- **Scream:** `Assets/Audio/scream.mp3` (12 s), imported as mono and loaded in memory. It's spatialised with Meta XR Audio from the mug's centre, and it follows the mug if the mug moves mid-scream. A new trigger restarts it rather than playing two at once.
- **Background:** world-locked, from `Assets/Audio/mugAudio_L.mp3` and `mugAudio_R.mp3`, the two channels of `mugAudio.mp3` split to mono. The original is very wide: the side signal is as loud as the mid.
  - The two channels play in sync from two fixed points 3 m left and right of where you start (1 m forward, head height). They're spatialised, so the bed stays in the room as you turn instead of following your head.
  - It loops at 35%, fades in over 3 s, and drains as a hand approaches the mug. It's cut in the silence before the scream and drops to 25% under the scream.
  - The stereo `mugAudio.mp3` is the head-locked fallback if the pair isn't assigned.
- **Room acoustics:** `AudioRoom` (`MetaXRAudioRoomAcousticProperties` + `RoomAcoustics`) gives Meta XR Audio early reflections and reverb sized from the scanned MRUK room. It's capped at 15 × 5 × 15 m, with an 8 × 3 × 6 m default. Without it the sounds have no room around them and feel inside the head. Spatial sound is much clearer on headphones than on the headset speakers.
- **Event:** `onScream` fires with each scream. RoomDrain is connected to it in the scene; add other effects the same way.

**Measuring "almost touching or contact?"**
1. Do 10 reaches at each zone size.
2. Press **Miss** when a reach didn't fire, and **False** when it fired without a reach.

Each scream logs `[MugScream] SCREAM #n` with the fingertip distance. Switching zones logs the running totals, and the debug panel shows them live.

Debug (off at launch; the **Debug** button shows them with the panel): a faint ring at mid-height marks the 30 cm approach zone and brightens as the breath builds. The touch zone shows as two rings at the mug's base and rim:

| Ring colour | Meaning |
| --- | --- |
| Red | Armed |
| White | Just fired |
| Orange | Re-arming |
| Grey | Held |

## Looking inside the mug (rune prototype, step 1)

`MugInterior` (on `CupDetection`): look down into the real mug and something happens at its opening, with a low drone rising from the mug. Look away and it closes. **Look** on `MugInterior` picks what you see:

- **Particle Ring** (the default): glowing gold motes gather on the rim and swirl slowly upward, widening as they rise. The real inside of the mug stays visible. Rate, rise, swirl and colours are under "Particle ring". It uses `Materials/DreamEmber.mat`; if **Ring Material** is empty it borrows the dream's.
- **Tunnel**: the original bottomless tunnel, fading into darkness. On the headset it tended to read as a flat black disc over the opening, which is why it's no longer the default.
- **None**: only the drone.

- **Trigger:** all of these must hold for 0.4 s, after which it opens over 1.5 s and closes in 0.6 s:
  - the head is within 0.9 m
  - the gaze is within 25° of the opening
  - the head is above the opening, within 50° of the mug's own down axis, so it works for a held, tilted mug too
- **How it's drawn** (Tunnel look; the ring is a particle system on the rim, and the occluder is built for every look): children of the proxy mug, so it follows the tracked mug and hides when tracking is lost.
  - `Occluder`: the proxy mesh, depth only (`Mug/MugOccluder`). Virtual things behind the real walls are hidden and passthrough shows through. The visible proxy is now **hidden by default**; the **Proxy** button shows it.
  - `Portal`: a disc across the opening at the rim (`Mug/MugPortalMask`) that marks the stencil buffer.
  - `Well` (+ `Rune`, off by default: **Show Rune** on `MugInterior` draws the client's rune, `Textures/Rune.png`, glowing at the bottom): a tunnel 0.6 m deep, drawn only through the portal and ignoring depth, so it goes "through" the real floor (`Mug/MugWell`, `Mug/MugRuneGlow`). The shaft turns to run away from the viewer, so you always look straight down it; a straight well would only show its bottom from directly above.
- **Drone:** for now, the breath placeholder pitched down to 0.55.
- **Buttons:**
  - **Inside** keeps it open, to check the effect.
  - **Rec** saves camera frames (JPG plus `frames.csv` with camera pose, mug pose and cup box) while looking in, up to 60 per session, for tuning a rune detector in step 2. Pull them with:
    `adb pull /sdcard/Android/data/com.fiveoftwelve.memoriesofthefuture/files/RuneCaptures`
- **Step 2 (not built):** detect the rune's blobs inside the cup box and get its exact pose with `PlanarPose` from 01-PictureChanges. That locks the effect to the real rune, gives the mug's rotation about its axis, and tells mugs with different runes apart.

## Rim lock (held mug lines up with the real opening)

`RimTracker` (on `CupDetection`): while the mug is held with its opening towards you, it finds the real opening in the camera image and corrects the held pose. The well and rune then sit on the real rim.

**Why it's needed:** the held pose comes from the hand. On device, the rim it predicted was **6–10 cm off** the real one, mostly too close to the camera. The error came from the pick-up and lasted the whole hold. Measured on 20 frames of a headset capture (`DebugOutput/rune/RuneCaptures/20261119_120355`).

**How it works:**
1. **Find the opening.** In each camera frame, at half resolution, on a worker thread, and only in a window around the predicted rim, it looks for a bright region whose outline is an ellipse (the white inside and lip, edged by the black band), with dark holes in its middle (the rune).
2. **Locate the rim in 3D.** The ellipse centre gives the direction, and its size against the 40 mm outer radius gives the distance.
3. **Correct the hold.** Half the difference goes into the hand-to-mug offset (`CupDetector.ApplyHeldCorrection`), at most 5 cm at a time. It only acts once two detections in a row agree within 3 cm, and ignores anything more than 15 cm from the prediction.
4. **Keep the correction.** It lasts for the rest of the hold, so the mug stays aligned after you look away.

While it's locking, the older YOLO sideways correction is switched off, because the YOLO box sometimes lands on a different cup.

**Results on device** (`20261119_122042`): after the first lock, the rim was **0.2–1.1 cm** from the real one, with one frame at 3 cm. That capture had 26 corrections in about 8 s.

**Limit:** the opening is hard to find against a **white or light table**, because the white mug merges with it. In that capture the first lock took about 13 s. On a darker surface it locked straight away. For the installation, a dark tablecloth or tray under the mugs would help this, and it would also help the YOLO cup detection.

**Controls and debug:**
- The **Rim** button in the hand menu switches it on and off for comparison. It's lit when on, which is the default.
- The debug panel's blue line shows the state (`LOCKED, corrected … cm`), how many times it has seen the opening, how many corrections it has made, and the last error.
- Logs are prefixed `[RimTracker]`.

**Offline checks:** `Rec` captures can be checked against the prediction by projecting the proxy's rim into each frame. The camera model is in `PassthroughCameraAccess.WorldToViewportPoint`; frame row 0 is the bottom of the image.

## Holding the mug: dread, dream and echoes

`DreamState` (on `CupDetection`). Pick the mug up and keep holding it; put it down to come back.

1. **Dread** (the first **5 s** of holding, `Hold Before Dream`):
   - the passthrough drains towards grey and darkens, with a heartbeat pulse that quickens
   - the real room's edges glow red (passthrough edge rendering)
   - **echoes** ripple out of the mug: the room around it wobbles like heat haze, and grey ghost copies of the mug swell out of it and fade, with faint red wavefronts (`CupEchoes`, below)
   - a few embers rise from the rim, the whispers creep in and the background bed sinks

   Put the mug down during the dread and it all sinks away over 1.5 s, with no wake-up.
2. **Dream:** the dome spreads from the mug, then embers, whispers, the scanned room and ghostly hands, over `Deepen Time` (8 s). It only deepens while you hold the mug.
3. **Waking:** put the mug down. It must read as down for 0.5 s, so a tracking blip doesn't count. Everything then eases out together over **2.5 s** (`Wake Fade Time`). With the Scream wake-up (the default), a beat of silence follows and then the mug screams; the **Wake** button makes it silent. The dream also ends if you walk more than 1.2 m, after `Max Full Time` at full depth, or when you press **Dream** again.

The mug counts as held when `CupDetector` tracks it from the hand. Looking into the mug still opens the tunnel (above), but it no longer starts the dream. **Dream** in the hand menu starts the dread straight away without the mug, quickened to 1 s.

**Echoes** (`CupEchoes`, `Mug/CupEchoes`): a disc 0.3 m in radius, centred on the mug and facing you. It shows the passthrough camera's image projected onto it, warped and drained to grey, and fades out at its edge into the real passthrough and over the mug itself, so the real mug stays crisp. Because the camera image arrives a few frames late, it may slip slightly when you turn your head fast; the warping hides most of that. In the desktop build it warps a half-resolution copy of the view instead. The **Echo** developer button shows the echoes at full strength, held or not, for tuning. Shape and speed are on `Materials/CupEchoes.mat`.

## Cup detection

```
PassthroughCameraAccess (1280x960) + camera pose at capture
  -> YOLO11n (COCO, on-device, Unity Inference Engine, GPU, 640x640, spread over frames)
  -> best "cup" box + best box of any class, worked out on the GPU (11 floats read back)
  -> ray through the box centre -> distance from environment depth, or from the box height
     vs the real mug height (Auto uses depth when the two agree)
  -> cup centre -> ProxyMug placed on it, upright, logo towards you
Pick-up: a gripping hand whose finger model (MugGrip: index and middle fingers in the handle hole)
would put the mug inside the last cup box. The check projects into that camera frame, so the
camera's distance isn't relied on.
Fused pose at pick-up: the mug is still upright on the table, so:
  - sideways and height come from the camera ray through the box
  - distance along that ray comes from the finger model (it agreed with the camera to ~3 cm on device)
  - the handle points at the fingers
From that pose, the handle point (where the fingers go through) is stored relative to the WRIST
joint. Curled fingers are the least reliable joints while gripping; on device they made the proxy
tilt and jump to one side.
While held:
  - The handle point stays locked to the wrist, and the mug pivots about it, so a rotation error
    barely moves it.
  - Turning about the vertical follows the hand fully. Tilt is damped: a hooked mug swings on the
    fingers and stays near upright, and on device a rigid tilt was 20-30 degrees off.
    Damped ignores tilt up to 15 degrees, blends smoothly to the full tilt by 50 degrees, and
    follows exactly beyond that (25 -> ~5, 40 -> ~27, 90 -> 90). An earlier linear 60% damping was
    too weak on device: a ~90 degree tip showed as ~42.
    The Tilt button cycles Damped / Upright / Rigid.
Release: the hand must read open continuously for 0.25 s. A single noisy reading no longer
counts, after it released mugs that were still held.
  - When the camera sees the mug (within 8 cm sideways) while the wrist is nearly still, 35% of
    the sideways error is corrected. Distance isn't touched.
  - A jump over 8 cm in one frame is treated as a glitch and ignored for up to 0.25 s.
Release: the hand stays open for 0.25 s. Tracking dropouts keep the hold, and other cups in the
room can't move or release it.
```

`MugGrip` settings on `CupDetector` (`Hole In Mug`, `Rotation Offset`, curl thresholds) tune the finger model used at pick-up. The correction, glitch and gate settings are under "Holding".

While held, the debug view draws axes at the mug centre: red is handle to body, green is up, blue is the logo side. The panel also shows:
- how far the finger-only model was from the fused pose at pick-up (the size of the error this fixes)
- the total sideways camera correction since pick-up
- the number of glitches ignored

The log has the same figures.

Tested on frames from a headset recording, the mug on the desk scores 0.7-0.93. Held in the hand, it mostly isn't recognised as a cup, which is why the hand takes over. The size-based distance allows for viewing angle: from above, the rim adds to the apparent height. Without that correction, a held mug was placed 0.45 m away on device, much too close. A lamp base scored up to 0.35 as a cup, so the minimum score is 0.45.

**Model:** `Models/yolo11n.onnx` + `Models/yolo11n_classes.txt` (COCO, `cup` = 41) from Unity's `unity/inference-engine-yolo` repo on Hugging Face. Ultralytics YOLO is **AGPL-3.0** (the repo lists GPL-3.0). That is fine for testing, but anything delivered to the client needs an Ultralytics licence or a permissively licensed model (for example YOLOX, Apache-2.0).

### Debugging

The debug panel is **off at launch**, so artists and visitors see only the experience. The **Debug** button in the hand menu turns it on, along with the world markers and the touch-zone rings. It floats below your line of sight and shows:
- the frame the model last analysed, with a green box on the cup (orange when its score is below the threshold) and a grey box on the best object of any other class
- the status, analysis rate and time per frame, the cup and best-class scores, the depth and size distances and which one is used, the tracking source (camera or hand) and the nearest palm distance
- the reason a detection was ignored, if one was

In the world, a yellow dot marks the estimated cup centre and a cyan dot marks where the depth ray hit. The console logs every change of state with the prefix `[CupDetector]`.

Hand menu (left palm up). Artists and visitors see only **Inside, Dream and Wake**, plus the scene row. The buttons below are developer buttons apart from Inside, hidden unless **Developer Buttons** is ticked on the `HandMenu` component:

| Button | Does |
| --- | --- |
| Debug | Panel, markers and touch-zone rings on/off (off at launch) |
| Proxy | Show/hide the proxy mug (tracking keeps running) |
| Freeze | Hold the proxy where it is, to walk round it and check alignment |
| Dist | Distance source: Auto → depth only → size only (lit when not Auto; the panel shows which) |
| Hands | Show the virtual hands and the rig's rays and reticles (hidden by default, so only your real hands show in passthrough; tracking keeps running) |
| Tilt | Held mug tilt: Damped → Upright → Rigid (lit when not Damped; the panel shows hand vs mug tilt) |
| Zone | Touch distance 3 cm ↔ 10 cm (lit at 10 cm) |
| Pin | Pin the scream zone by pinching at the mug / back to tracking (lit while pinned) |
| Miss / False | Log a reach that didn't fire / a fire without a reach |
| Inside | Keep the mug's interior open (without looking in) |
| Rec | Save camera frames while looking into the mug, for the rune detector |
| Rim | Rim lock on/off (lit = on, the default) |
| Echo | Echoes around the mug at full strength, held or not (for tuning) |

In the Editor, MRUK's mock camera shows colour bars. Set **Editor Test Image** on `CupDetector` to a frame from a headset recording to test the whole chain without the headset (depth isn't available in the Editor, so it uses the size estimate).

## Proxy mug

`Prefabs/ProxyMug.prefab` is the virtual copy of the real cup, for placing content, occlusion and checking alignment against the real mug.

- Mesh: `Assets/Models/zipinkent-cambridge-mug.fbx`, already at real scale: 95 mm tall, 80 mm outer diameter, 119 mm including the handle, wall about 3.4 mm at the rim. It's an FBX so the project opens without Blender. The editable original is `Scans/Mug/zipinkent-cambridge-mug.blend` (outside Assets, so Unity ignores it). To change the model, edit that file and re-export over the FBX with Unity's own export script, which keeps every reference in the prefab: `Blender -b Scans/Mug/zipinkent-cambridge-mug.blend -P <Unity.app>/Contents/Resources/AssetImporting/Unity-BlenderToFBX.py -- $PWD/MemoriesOfTheFuture/Assets/Models/zipinkent-cambridge-mug.fbx`.
- Pivot: centre of the base, +Y up. In prefab space the handle points to -X and the logo faces +Z.
- Look: `Materials/ProxyMug.mat` (URP Lit) with `Textures/ProxyMug_Albedo.png`. Smoothness is in the alpha: glossy white ceramic, satin black band and gold print.
- The texture is generated by `Tools~/make_proxy_mug_texture.py` (Unity ignores `~` folders). It draws the design in millimetres on the unrolled wall, then maps it to the mesh's UV strip.

The print values are **estimates from two phone photos** and should be checked against the real mug:

| Value | Estimate |
| --- | --- |
| Black band | 6 mm to 89.5 mm up from the base |
| White gap around the handle | 22 mm wide |
| Shield logo | 50 x 53 mm, centred 48.5 mm up, 90 degrees round from the handle |
| Shield outline, H letter | Traced by eye from the photos; swap in the client's vector logo when we get it |

We have no photo of the back, so the proxy assumes nothing is printed there.

## Conventions

- Put scripts in `Assets/Mug/Scripts` under the `Mug` namespace, and materials and other assets in sibling folders.
- Keep mug content under `MugRoot` so it can be moved or turned off in one place.
- On the headset, switch to this scene from the left hand menu (**Mug**). See "Switching scenes" in `Assets/Shared/README.md`.
