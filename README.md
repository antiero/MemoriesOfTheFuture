# Memories of the Future

A mixed-reality installation for Meta Quest, built in Unity. Two pieces share one app:

- **The mug**: a real mug on a table. Reach for it and it breathes; touch it and it screams. Look
  inside and it opens into a bottomless tunnel. Pick it up and hold it, and
  the room drains away into a dream until you put it down.
- **The painting**: a real painting on the wall that changes while you aren't looking. It moves on
  a stage each time you look away, and screams from behind you on its last change.

**New to the project? Start with [HANDOVER.md](HANDOVER.md)**: where everything lives, and how to
change the sound, the painting and the look without touching code.

## Opening the project

1. Install **Unity 6000.6.9f1** (Unity 6.6) through Unity Hub, with **Android Build Support** (for
   the headset), and **Mac Build Support (Mono)** or **Windows Build Support (Mono)** for the
   desktop test app on your own machine.
2. Install **Blender**. Unity reads the mug model straight from its `.blend` file, and needs
   Blender to do it.
3. Clone the repository. **On Windows, clone it to a short folder** such as `C:\Dev\MotF`: some of
   Unity's package files have very long paths, and Windows refuses paths over 260 characters.
4. In Unity Hub, **Add > Add project from disk** and choose the `MemoriesOfTheFuture` folder inside
   this repository (the one that contains `Assets`).

Windows and Mac can share the project: `.gitattributes` keeps line endings the same on both, so
nobody's commits show files they didn't change.

## Scenes

All in `MemoriesOfTheFuture/Assets/Scenes`:

| Scene | What it is |
| --- | --- |
| `00-Boot` | Starts the headset app and reopens whichever scene was used last |
| `01-PictureChanges` | The painting, on the headset |
| `02-Mug` | The mug, on the headset |
| `03-DesktopTesting` | Both pieces in a stand-in room, played on a Mac with mouse and keyboard. No headset needed |

On the headset, turn your left palm towards your face to bring up the hand menu. The bottom row
(**Painting**, **Mug**) switches between the two.

## Repository layout

| Folder | Contents |
| --- | --- |
| `MemoriesOfTheFuture/` | The Unity project |
| `MemoriesOfTheFuture/Builds/` | Built apps: `Android/` (headset `.apk`), `macOS/` (desktop test app). Not in Git |
| `Movies/` | Screen recordings from the headset. Not in Git |
| `Scans/` | Scans of the real mug |

## Documentation

| Document | For |
| --- | --- |
| [HANDOVER.md](HANDOVER.md) | Artists and designers: the tour, and how to change things |
| [Sound Design](MemoriesOfTheFuture/Assets/Audio/README%20-%20Sound%20Design.md) | Everything about sound: slots, the mixer, trying new files |
| [Adding Spatial Sound](MemoriesOfTheFuture/Assets/Audio/README%20-%20Adding%20Spatial%20Sound.md) | Step by step: a new sound at a place in the room |
| [The mug](MemoriesOfTheFuture/Assets/Mug/README.md) | How the mug piece works, in detail (tracking, scream, tunnel, dream) |
| [The painting](MemoriesOfTheFuture/Assets/PictureChanges/README.md) | How the painting piece works, in detail (markers, stages, triggers) |
| [Shared](MemoriesOfTheFuture/Assets/Shared/README.md) | Scene switching and adding a scene |
