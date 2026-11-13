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

## Controls (controllers or hands)

| Action | Controller | Hand |
| --- | --- | --- |
| Capture detector frame | A | right index pinch, hold 0.6 s |
| Rescan | B | left index pinch, hold 1 s |
| Toggle debug view | Y | right middle-finger pinch, hold 0.6 s |
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
