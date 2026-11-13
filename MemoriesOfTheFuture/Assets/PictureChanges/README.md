# 01-PictureChanges: glyph-tracked painting

The board is found from its four hand-drawn corner glyphs in the Passthrough Camera feed (no QR codes).

```
PassthroughCameraAccess (1280x960, 15 fps)
  -> greyscale -> GlyphDetector (worker thread): dark ink blobs on clean white patches, best board-shaped quad
  -> 4 rays via ViewportPointToRay -> PlanarPose (homography PnP against BoardLayout)
  -> BoardPoseEstimator: distance/angle filter, median outlier rejection, average 30 samples, lock
  -> PictureCanvas: place + size quad, OVRSpatialAnchor, trigger, still -> painting.mp4
```

## Scene

| Object | What it does |
| --- | --- |
| OVRCameraRig | Core rig + OVRManager (passthrough on) + OVRPassthroughLayer; Interaction SDK comprehensive rig as child (Locomotor disabled, not needed in MR) |
| MRUK | Scene/room data (wall snapping later) |
| PassthroughCamera | `PassthroughCameraAccess`, left camera |
| PaintingRoot / PaintingQuad | Anchored root; unit quad scaled to the painted area, URP Unlit |
| PictureChanges | Permissions bootstrap, `BoardPoseEstimator`, `VideoPlayer` (painting.mp4), `PictureCanvas` |

## Before the first device build

1. **Measure the board** and enter it on `BoardPoseEstimator > Layout`:
   - `glyphSpacing`: centre-to-centre of the glyph *ink*, TL→TR and TL→BL (defaults 234 x 310 mm are estimated from a photo).
   - `paintingSize` / `paintingOffset`: the painted area and its offset from the glyph-rectangle centre.
   Wrong spacing scales the distance estimate, so the canvas lands in front of or behind the wall.
2. Switch the build target to Android and run Meta's Project Setup Tool (Edit > Project Settings > Meta XR).
3. The project also has Unity's AR Foundation Meta OpenXR features enabled (template leftovers). If passthrough or permissions conflict, disable those in OpenXR settings and keep the Meta XR Feature.

## Check on device

- `BoardPoseEstimator > Image Rows Top First`: if the canvas lands upside-down or mirrored vertically, toggle it.
- Turn on `Log Each Detection` and watch logcat for `[BoardPose]` to see detection distance and rate.
- `PictureCanvas > Show Alignment Grid`: 10 mm grid with a red border, for measuring registration error.
- Controls: hold left grip + left stick = nudge 1 mm; B = rescan.

## Tuning in the editor

`Tools > Picture Changes > Run Glyph Detector On Photos` runs the detector on every `.jpg` under `Assets/Artwork/` and writes annotated PNGs to `DebugOutput/`. Add headset camera captures there to tune `GlyphDetector.Settings`.
