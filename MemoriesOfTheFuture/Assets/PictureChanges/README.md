# 01-PictureChanges: marker-tracked painting

The board is found from four hand-drawn **square markers** (a thick black square around each corner glyph) in the Passthrough Camera feed. No QR codes.

```
PassthroughCameraAccess (1280x960, 15 fps)
  -> greyscale -> SquareMarkerDetector (worker thread):
       bright enclosed interiors with a thick dark ring -> sub-pixel edge fit -> 4 inner corners each
       -> best board-shaped set of 4 markers
  -> 16 rays via ViewportPointToRay -> PlanarPose (least-squares homography PnP against BoardLayout)
  -> BoardPoseEstimator: distance/angle filter, median outlier rejection, average 30 samples, lock
  -> PictureCanvas: place + size quad, OVRSpatialAnchor, trigger, still -> painting.mp4
```

`GlyphDetector` (the first attempt, glyphs without squares) is kept for reference; it was too weak at headset distances (glyphs ~20 px, strokes 1-2 px, lots of false matches in the painting and room).

## Drawing the markers

- One square per corner, around the glyph, **thick black line** (felt tip / marker, about 5 mm).
- Square outer size about **40-45 mm**. All four the same size.
- The line must be **closed all the way round** (no gaps at the corners).
- Keep the inside light: the glyph can touch the line, but don't fill the square.
- Keep the squares upright (edges parallel to the board edges).

## Measure and enter (BoardPoseEstimator > Layout)

| Field | What to measure |
| --- | --- |
| `glyphSpacing` | Centre-to-centre of the squares: x = TL→TR, y = TL→BL (metres) |
| `markerSize` | Outer side of one square (metres) |
| `markerLine` | Line width (metres) |
| `paintingSize` | Painted area the virtual painting covers (metres) |
| `paintingOffset` | Painting centre relative to the centre of the four squares (x right, y up) |

Wrong spacing/size scales the distance estimate, so the painting lands in front of or behind the board.

## Scene

| Object | What it does |
| --- | --- |
| OVRCameraRig | Core rig + OVRManager (passthrough on) + OVRPassthroughLayer; Interaction SDK comprehensive rig as child (Locomotor disabled) |
| MRUK | Scene/room data (wall snapping later) |
| PassthroughCamera | `PassthroughCameraAccess`, left camera |
| PaintingRoot / PaintingQuad | Anchored root; unit quad scaled to the painted area, URP Unlit |
| PictureChanges | Permissions, `BoardPoseEstimator`, `VideoPlayer` (painting.mp4), `PictureCanvas`, `PictureChangesInput` |
| BoardDebugView | In-headset detector panel, status/hint text, world overlay of the detected board |

## Visitor experience

1. **Find the painting**: a quiet prompt floats low in view until the board is seen.
2. **Locking on**: soft corner brackets appear around the real painting and close in as the estimate settles; a chime when it locks.
3. **Reveal**: the virtual painting appears with a watercolour-bleed wash (`PictureChanges/Painting` shader: feathered edges, exposure/saturation matched to passthrough, fine grain).
4. **Step closer**: an invitation below the painting until the visitor approaches and faces it; then the movie plays once and holds its last frame.

## Hand menu (left palm up)

Turn your **left palm towards your face**: the menu fades in just off the palm (Pezzi / Meta hand-menu convention, with enter/exit hysteresis). Press with your **right index finger**.

| Button | Does |
| --- | --- |
| Replay | Back to the still; the transformation can trigger again |
| Rescan | Forget the placement and find the board again (in Test mode: re-place the virtual painting) |
| Test | Test mode on/off (below) |
| Debug | Detector panel + world overlay (off by default) |
| Grid | 10 mm alignment grid instead of the painting |
| Capture | Save the detector's current camera frame |

## Test mode (no physical painting needed)

For debugging the proximity interaction without the board. Turn on **Test** in the hand menu, then point with the right hand at a wall
(Quest scene model via MRUK) and **pinch** to hang a virtual painting there (no scene model: it is placed 1.2 m in front of you).
Camera detection is suspended; everything downstream runs for real.

A readout (lower right) shows live distance and angle, Near/Far, Facing/Away, dwell progress, movie state and recent events.

`ViewerPresence` events (also logged as `[Presence] ...` and exposed as UnityEvents):

| Event | When |
| --- | --- |
| Approach / Leave | Head within 1.5 m of the painting centre / beyond 1.7 m |
| FaceToward / FaceAway | Head direction within 25° of the painting / beyond 35° |
| Triggered | Near and Facing for 0.5 s (once, until Replay) |

## Controls (controllers or hands)

| Action | Controller | Hand |
| --- | --- | --- |
| Capture detector frame | A | hand menu |
| Rescan | B | hand menu (pinch shortcuts off by default) |
| Toggle debug view | Y | hand menu |
| Nudge painting 1 mm | left grip + left stick | - |

Frames are also auto-captured every 2 s while searching (max 40 per session) to
`/sdcard/Android/data/com.fiveoftwelve.memoriesofthefuture/files/captures/<session>/`, with `camera.json` intrinsics.

## Debugging and tuning

- Logcat `[BoardPose] STATUS` every 2 s: camera, frames, detections, samples, rejections, lock blocker, hint.
- Debug view overlay colours: orange = seen but rejected (too far / oblique), cyan = sampling, green = locked.
- `PictureCanvas > Show Alignment Grid`: 10 mm grid with a red border, for measuring registration error.
- Pull captures: `adb pull /sdcard/Android/data/com.fiveoftwelve.memoriesofthefuture/files/captures DebugOutput/headset/`
- `Tools > Picture Changes > Run Square Detector On Headset Captures`: runs detector + pose on everything in
  `DebugOutput/headset`, writes annotated `*_squares.png` to `DebugOutput/`.
- `SquareDetectorTest.RunSynthetic(...)` draws squares onto a capture digitally, to test before drawing for real.
