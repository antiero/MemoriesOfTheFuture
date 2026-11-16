# 02-CupSpike: starter scene

Empty mixed-reality starting point for the Cup Spike explorations. It has the same platform setup as `01-PictureChanges`, with none of that scene's content.

## Scene

| Object | What it does |
| --- | --- |
| `Directional Light` | Basic key light for virtual content |
| `MRUK` | Meta MR Utility Kit (room / scene understanding) |
| `PassthroughCamera` | `PassthroughCameraAccess` at 1280x960, for any camera-feed work (e.g. finding the cup) |
| `OVRCameraRig` | Head and hands, with Insight passthrough on, an `OVRPassthroughLayer`, and the `OVRComprehensiveInteractionRig` |
| `CupSpikeRoot` | Empty parent for spike content, about 1 m up and 0.6 m in front of the origin |

## Conventions

- Put scripts in `Assets/CupSpike/Scripts` under the `CupSpike` namespace, and materials and other assets in sibling folders.
- Keep spike content under `CupSpikeRoot` so it can be moved or turned off in one place.
- The scene is in Build Settings at index 1. To run it on device, move it to index 0 or untick `01-PictureChanges`.
