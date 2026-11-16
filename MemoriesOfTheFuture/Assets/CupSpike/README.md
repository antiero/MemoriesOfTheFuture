# 02-CupSpike: starter scene

Empty mixed-reality starting point for the Cup Spike explorations. It has the same platform setup as `01-PictureChanges`, with none of that scene's content.

## Scene

| Object | What it does |
| --- | --- |
| `Directional Light` | Basic key light for virtual content |
| `MRUK` | Meta MR Utility Kit (room / scene understanding) |
| `PassthroughCamera` | `PassthroughCameraAccess` at 1280x960, for any camera-feed work (e.g. finding the cup) |
| `OVRCameraRig` | Head and hands, with Insight passthrough on, an `OVRPassthroughLayer`, and the `OVRComprehensiveInteractionRig` |
| `CupSpikeRoot` | Parent for spike content, about 1 m up and 0.6 m in front of the origin |
| `CupSpikeRoot/CupDetection` | Permissions (`PictureChangesBootstrap`), `EnvironmentRaycastManager`, `CupDetector`, `CupDebugView`, `CupSpikeMenu` |
| `HandMenu` | Left hand menu (`HandMenu` + `SceneMenu`), same as in `01-PictureChanges` but with only the scene buttons |
| `CupSpikeRoot/ProxyMug` | To-scale stand-in for the client's mug (prefab instance), turned so the logo faces the origin |

## The scream (spike plan, Block 1)

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
- **When it fires:** never while the mug is held; that's Block 4. It re-arms once the scream has finished, 2 s have passed, and every fingertip is 5 cm outside the touch zone.
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

Debug: a faint ring at mid-height marks the 30 cm approach zone and brightens as the breath builds. The touch zone shows as two rings at the mug's base and rim:

| Ring colour | Meaning |
| --- | --- |
| Red | Armed |
| White | Just fired |
| Orange | Re-arming |
| Grey | Held |

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

**Model:** `Models/yolo11n.onnx` + `Models/yolo11n_classes.txt` (COCO, `cup` = 41) from Unity's `unity/inference-engine-yolo` repo on Hugging Face. Ultralytics YOLO is **AGPL-3.0** (the repo lists GPL-3.0). That's fine for this spike, but anything delivered to the client needs an Ultralytics licence or a permissively licensed model (for example YOLOX, Apache-2.0).

### Debugging

The debug panel is **on by default**. It floats below your line of sight and shows:
- the frame the model last analysed, with a green box on the cup (orange when its score is below the threshold) and a grey box on the best object of any other class
- the status, analysis rate and time per frame, the cup and best-class scores, the depth and size distances and which one is used, the tracking source (camera or hand) and the nearest palm distance
- the reason a detection was ignored, if one was

In the world, a yellow dot marks the estimated cup centre and a cyan dot marks where the depth ray hit. The console logs every change of state with the prefix `[CupDetector]`.

Hand menu (left palm up), top row:

| Button | Does |
| --- | --- |
| Debug | Panel and markers on/off |
| Proxy | Show/hide the proxy mug (tracking keeps running) |
| Freeze | Hold the proxy where it is, to walk round it and check alignment |
| Dist | Distance source: Auto → depth only → size only (lit when not Auto; the panel shows which) |
| Hands | Show the virtual hands and the rig's rays and reticles (hidden by default, so only your real hands show in passthrough; tracking keeps running) |
| Tilt | Held mug tilt: Damped → Upright → Rigid (lit when not Damped; the panel shows hand vs mug tilt) |
| Zone | Touch distance 3 cm ↔ 10 cm (lit at 10 cm) |
| Pin | Pin the scream zone by pinching at the mug / back to tracking (lit while pinned) |
| Miss / False | Log a reach that didn't fire / a fire without a reach |

In the Editor, MRUK's mock camera shows colour bars. Set **Editor Test Image** on `CupDetector` to a frame from a headset recording to test the whole chain without the headset (depth isn't available in the Editor, so it uses the size estimate).

## Proxy mug

`Prefabs/ProxyMug.prefab` is the virtual copy of the real cup, for placing content, occlusion and checking alignment against the real mug.

- Mesh: `Assets/Models/zipinkent-cambridge-mug.blend`, already at real scale: 95 mm tall, 80 mm outer diameter, 119 mm including the handle, wall about 3.4 mm at the rim. Unity imports `.blend` files through Blender, so every machine that opens the project needs Blender installed (or export an FBX instead).
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

- Put scripts in `Assets/CupSpike/Scripts` under the `CupSpike` namespace, and materials and other assets in sibling folders.
- Keep spike content under `CupSpikeRoot` so it can be moved or turned off in one place.
- On the headset, switch to this scene from the left hand menu (**Cup**). See "Switching scenes" in `Assets/Shared/README.md`.
