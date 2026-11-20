# Memories of the Future: handover for artists

This is the tour of the project: what's in it, where each thing lives, and how to change the
sound, the painting and the look of the mug yourself. Nothing here needs code.

If you only read one other document, make it the
[Sound Design guide](MemoriesOfTheFuture/Assets/Audio/README%20-%20Sound%20Design.md).

---

## 1. The two pieces

**The mug.** A real mug on a table, tracked by the headset's cameras.

1. Reach towards it: it starts to **breathe**, louder and higher the closer your finger gets, while
   the background sound drains away.
2. Touch it: a beat of total **silence**, then it **screams**, and the room's colour crushes for a
   moment.
3. Lean over and look in: the inside opens into a bottomless **tunnel** fading into darkness, with
   a low drone. (The client's glowing **rune** can be shown at the bottom: it's switched off for now.)
4. Pick it up and hold it: **dread** for 5 seconds (the room greys, edges glow red, a heartbeat
   quickens, **echoes** ripple out of the mug), then the **dream** spreads from the mug: a dome,
   embers, whispers circling you, a ghostly scan of the room.
5. Put it down: you **wake**. Everything eases out, and the mug screams again (or stays silent).

**The painting.** A real painting on the wall, found by four hand-drawn square markers at its
corners. The headset lays a virtual copy exactly over it.

- By default it **changes only when you aren't looking**: look at it, look away, look back, and it
  has moved on a stage. On its last change it screams from where it hangs, behind or beside you.
- Two other triggers are there to compare: walk up to it (**Proximity**) or stare at it (**Gaze
  dwell**), and it plays its transformation video.

---

## 2. Your first ten minutes

You don't need a headset to try everything.

1. Open the project in Unity (see the [README](README.md) for what to install).
2. In the **Project** window, open `Assets/Scenes/03-DesktopTesting`.
3. Press **Play** at the top. Put headphones on.
4. Read the welcome card, then press **Enter**.

You're standing in a plain stand-in room with the mug on a table in front of you and the painting
on the wall to your right. The cards top left say what's happening and what to try next, and every
key is listed bottom left (**H** hides them). The ones you'll use most:

| Key | Does |
| --- | --- |
| Click the view, then move the mouse | Look around (**Esc** frees the mouse) |
| **W A S D** | Walk |
| **1** / **2** | Jump to the mug / the painting |
| **↑** / **↓** (or scroll) | Reach your hand towards the mug / pull it back |
| **E** | Lean over the mug to look in |
| **G** | Pick the mug up / put it down (the dream) |
| **Tab** | The panel: **Sound**, **Timings** and **Help** |

There is also a **desktop app** of this room, which runs without Unity:
- Mac: `MemoriesOfTheFuture/Builds/macOS/MemoriesOfTheFuture-DesktopTesting.app`
- Windows: `MemoriesOfTheFuture\Builds\Windows\MemoriesOfTheFuture-DesktopTesting\MemoriesOfTheFuture-DesktopTesting.exe`
  (keep the whole folder together: the `.exe` needs the files beside it)

Builds aren't in Git, so each is made on a machine of that kind (see *Rebuilding the desktop app*).

The desktop room is for content, timing and levels. **Spatial sound, colour and scale must be
checked on the headset.**

---

## 3. Where everything lives

All paths are inside `MemoriesOfTheFuture/Assets/`.

| Folder | What's in it | Do you edit it? |
| --- | --- | --- |
| `Scenes/` | The four scenes (below) | Yes, carefully (see *Saving your work*) |
| `Audio/` | Every sound file, the **Sound Slots**, the **Mixer**, the **Drop** folder, and the two sound guides | **Yes**: this is yours |
| `Artwork/Stages/` | The painting's stages 1 to 4 (`stage1.jpg` ...) | **Yes** |
| `Artwork/Video/` | `painting.mp4` (the transformation video) and its first frame | **Yes** |
| `Artwork/Images/` | The app icon and reference photos of the frame | Yes |
| `Mug/Textures/` | `Rune.png` (the glowing rune) and the proxy mug's printed design | **Yes** |
| `Mug/Materials/` | The look of the tunnel, rune, echoes, dream dome, embers and the desktop room | **Yes**: see *The look of the mug* |
| `Mug/Splats/` | `RoomScan_400k.ply`, the scanned room that appears in the dream | Swap with care |
| `Mug/Prefabs/` | `ProxyMug`, the virtual stand-in for the real mug | Rarely |
| `Models/` | The mug model (`zipinkent-cambridge-mug.blend`, real size) | Rarely |
| `Mug/Audio/` | Placeholder sounds (breath, heartbeat) | Replace through the Sound Slots |
| `Mug/`, `PictureChanges/`, `Shared/` (Scripts, Shaders, Editor, UI) | The code | No |
| `Oculus/`, `XR/`, `XRI/`, `Resources/`, `Settings/`, `Plugins/`, `Samples/`, `MRTemplateAssets/`, `CompositionLayers/`, `Gsplat/`, `TextMesh Pro/` | Meta, Unity and package setup | **No**: leave these alone |

### The scenes

| Scene | What it is | Settings for the mug | Settings for the painting |
| --- | --- | --- | --- |
| `01-PictureChanges` | The painting, on the headset | | `PictureChanges` object |
| `02-Mug` | The mug, on the headset | `MugRoot/CupDetection` object | |
| `03-DesktopTesting` | Both, on the desktop | `MugRoot/CupDesktop` object | `PictureChanges` object |
| `00-Boot` | Starts the headset app | | |

Select the object in the **Hierarchy** and its settings show in the **Inspector**. Every setting has
a tooltip: hover over its name.

The headset scenes and the desktop scene are **separate copies**. A change in `02-Mug` doesn't
appear in `03-DesktopTesting` by itself; make it in both if you want it in both. Sound Slots, the
Mixer, materials and textures are shared, so changes to those show everywhere at once.

---

## 4. Changing the sound

Everything in the experience that makes a noise is a **Sound Slot**: the Scream, Breath, Drone,
Whisper, the Background bed, and the painting's Scream and Soundtrack. A slot holds the file, its
level, pitch, mixer group and how it sits in space. The code only decides *when* it plays.

Open **Memories > Sound Design** in the menu bar. From that one window you can:

- **Try a new file**: name it after the slot (`scream_take3.wav`, `breath_close.wav`), click
  **Drop Folder**, copy it in. It becomes the one that plays, and the old ones stay in the slot's
  list.
- **Mix**: level, pitch, solo and mute per slot. Changes save as you go, **even while playing**.
- **Open the Mixer**: one fader per group (Ambience, Mug, Dream, Painting, UI), effects, and a
  **snapshot** for each moment (Room, Approach, LookingIn, Dream) that the game slides between.
- **Add a new sound at a place in the room** (a hum in a corner, a heartbeat in the mug):
  **New Slot**, then **Add Sound to Scene**, then choose *when* it plays (always, while near, while
  holding the mug, while dreaming, when it screams...) and *where* (in the room, on the mug, by
  your head).

**In the desktop app**, press **Tab** for the same controls. Drop test files in the Sounds folder
(**Open sounds folder** opens it: `~/MemoriesOfTheFuture/Sounds` on a Mac,
`C:\Users\<you>\MemoriesOfTheFuture\Sounds` on Windows) and press **L** to hear them. **Save mix** keeps your levels. A
developer, or you in Unity, then brings that mix into the project with
**Memories > Audio > Import Mix From Desktop App**.

Full detail, step by step:
- [Sound Design](MemoriesOfTheFuture/Assets/Audio/README%20-%20Sound%20Design.md): slots, the mixer, snapshots, the Mac app's Sound panel, troubleshooting.
- [Adding Spatial Sound](MemoriesOfTheFuture/Assets/Audio/README%20-%20Adding%20Spatial%20Sound.md): a new sound at a place in the room, with three working examples in the desktop room.

---

## 5. Changing the painting

All on the **`PictureChanges`** object (in `01-PictureChanges`, and again in `03-DesktopTesting`).

| To change | Do this |
| --- | --- |
| **The stages** | Replace `Artwork/Stages/stage1.jpg` to `stage4.jpg` with the painted stages (same names, and Unity swaps them in). For more or fewer stages, edit the **Stages** list on the **Painting Stages** component. The current files are placeholders: frames grabbed from the video. |
| **The starting image** (stage 0) | **Picture Canvas > Still Image** |
| **The transformation video** | Replace `Artwork/Video/painting.mp4` |
| **How long you look / look away before it changes** | **Painting Stages > Seen Dwell** and **Unseen Time** |
| **How close you must come before it goes past stage 1** | **Painting Stages > Notice Distance** and **Far Max Stage** |
| **Scream on every change, not just the last** | Untick **Painting Stages > Swap Sound Only On Last** |
| **The scream / soundtrack** | The **Painting Scream** and **Painting Soundtrack** Sound Slots |

Make the stages the same size and framing as each other and as the still: the swap is a hard cut,
so any shift between them shows.

**Try it on the desktop:** press **2** to stand in front of the painting. Look at it, turn away with
the mouse, turn back. **P** starts it again from the first stage, **V** plays the video now, and
**T** switches the trigger. **Tab > Timings** lets you try timings live (see below).

On the headset, the hand menu's **Reset**, **Animate**, **Rescan**, **Test** and **Trigger** do the
same. **Test** lets you hang a virtual painting on any wall with a pinch, for trying it without the
real board. More in the [painting guide](MemoriesOfTheFuture/Assets/PictureChanges/README.md).

---

## 6. The look of the mug

Each effect is a **material** in `Assets/Mug/Materials/`. Select one in the Project window and its
settings appear in the Inspector, with friendly names. Changes show straight away, even while
playing, and are kept.

| Effect | Material | Things to try |
| --- | --- | --- |
| The rune at the bottom of the tunnel (**off by default**: tick **Show Rune** on **Mug Interior** to bring it back) | `MugRuneGlow` (image: `Mug/Textures/Rune.png`) | **Glow colour**, **Intensity**. To redraw the rune, replace `Rune.png` with a PNG whose strokes are solid and everything else transparent |
| The tunnel inside the mug | `MugWell` | **Colour at the rim**, **Colour deep down**, **Depth to full dark**, **Ring spacing** and **Ring strength** (the bands down the shaft) |
| Echoes around the held mug | `CupEchoes` | **Echo growth** (how far the ghost copies swell), **Echoes per second**, **Ripple strength**, **Monochrome**, **Tint**, **Wavefront colour** |
| The dream's dome | `DreamDome` | **Void colour** up and down, **Spiral colour**, **Edge softness** |
| Embers rising from the mug | `DreamEmber` | **Intensity** (brightness) |
| The stand-in mug | `ProxyMug` (image: `Mug/Textures/ProxyMug_Albedo.png`) | Only for checking alignment: it's hidden on the headset |
| The desktop room | `DesktopRoom_Wall`, `_Floor`, `_Table` ... | Desktop only |

Leave **Reveal**, **Dream depth**, **Spread**, the echoes' **Intensity**, and anything named
*Stencil*, *ZTest*, *Centre* or *Direction* alone: the code moves those while it plays, so a value
typed in is overwritten.

The materials ending in **`_Desktop`** (`MugWell_Desktop`, `MugRuneGlow_Desktop`,
`MugPortalMask_Desktop`) are the desktop room's copies of the tunnel and rune. Change the original
for the headset, and make the same change to the `_Desktop` copy to see it on the Mac.

### Timings and strengths of the mug

On **`MugRoot/CupDetection`** (headset) or **`MugRoot/CupDesktop`** (desktop):

| Component | Settings |
| --- | --- |
| **Mug Scream** | **Approach Start** (how far away the breath begins), **Silence Beat** (the silence before the scream), **Breath Pitch**, how much the background drains and ducks |
| **Room Drain** | How hard and how long the room's colour crushes when it screams |
| **Mug Interior** | **Max Distance** and **Gaze Angle** (how close and how directly you must look in), **Open Time** and **Close Time**, **Well Depth** |
| **Dream State** | **Hold Before Dream** (the dread, 5 s), **Deepen Time** (8 s), **Wake Fade Time** (2.5 s), **Wake Silence**, the dread's drain and red edge glow, the heartbeat rate |
| **Cup Echoes** | **Radius** of the echoes around the mug, and their **Max Intensity** |

**Try timings live first:** in the desktop room press **Tab > Timings**. The sliders there (breath
start, silence before the scream, dread length, time to go fully in, waking fade, the painting's
look and look-away times) change the running experience straight away. They **aren't saved**:
note the values you like, then type them into the Inspector, or pass them to the developer.

---

## 7. Testing on the headset

- **Builds** go to `MemoriesOfTheFuture/Builds/Android/MemoriesOfTheFuture.apk`. Ask the developer
  for a fresh build and to install it, or install it yourself with Meta Quest Developer Hub.
- The app opens whichever piece was used last. To switch, turn your **left palm towards your
  face**: the hand menu appears beside it. Press buttons with your right index finger. The bottom
  row is **Painting** and **Mug**.
- The menu shows only the buttons for artists and visitors. The rest are developer tools, hidden
  unless **Developer Buttons** is ticked on the scene's **Hand Menu**.

| Menu (Mug) | Does |
| --- | --- |
| Inside | Keep the tunnel open without looking in (to check it) |
| Dream | Start the dread and dream now, without holding the mug |
| Wake | Wake-up with a scream, or silent |

| Menu (Painting) | Does |
| --- | --- |
| Reset | Back to the first stage |
| Animate | Play the transformation video now |
| Rescan | Find the painting again |
| Test | Hang a virtual painting on any wall (no board needed) |
| Trigger | Unseen swap / Proximity / Gaze dwell |

### Rebuilding the desktop app

1. On a Mac: **Memories > Desktop Testing > Build macOS Desktop Testing App**.
   On Windows: **Memories > Desktop Testing > Build Windows Desktop Testing App**.
   It takes a few minutes and writes into `Builds/macOS/` or `Builds/Windows/`. If the Console
   says a build module is missing, add it in Unity Hub (**Installs > ⚙ > Add modules**:
   *Mac Build Support (Mono)* or *Windows Build Support (Mono)*) and restart Unity.
2. Then **Memories > Desktop Testing > Switch Back To Meta Quest**, so the next headset build
   isn't a desktop one.

Don't use **Memories > Desktop Testing > Create Desktop Testing Scene** unless you mean to: it
rebuilds `03-DesktopTesting` from scratch, and anything added to that scene by hand is lost.

---

## 8. Saving your work (Git)

- Use **GitHub Desktop**, and work on **your own branch**. Ask the developer to merge it.
- Always commit a file **together with its `.meta` file** (Unity makes one for every file). Without
  it, everything that uses the file loses track of it.
- Commit what you changed: sound files, Sound Slots (`Audio/Slots`), the Mixer, images, materials,
  and any scene you edited.
- **Scenes don't merge.** If two people change the same scene on the same day, one person's changes
  get lost. Agree who has which scene. Slots, materials and the mixer are separate files, so
  several people can work on those at once.
- Unity can undo (**Cmd+Z** on a Mac, **Ctrl+Z** on Windows), and Git keeps every version you've
  committed.

### Working on Windows

The project works the same on Windows and Mac. A few differences:

- **Clone to a short folder**, such as `C:\Dev\MotF`. Some of Unity's package files have very long
  paths, and Windows refuses paths over 260 characters, which shows up as package errors the first
  time Unity opens the project.
- Where these guides say **Cmd**, use **Ctrl** (Cmd+S is Ctrl+S, Cmd+D is Ctrl+D).
- **Finder** is **File Explorer**: the **Drop Folder** and **Open sounds folder** buttons open it.
- The desktop app's files live in `C:\Users\<you>\MemoriesOfTheFuture` (Sounds folder,
  `SoundMix.json`) instead of `~/MemoriesOfTheFuture`.
- Line endings are handled for you (the project's `.gitattributes`), so GitHub Desktop shows only
  the files you actually changed. If it ever lists lots of files you didn't touch, tell the
  developer rather than committing them.

---

## 9. Good to know

- **Blender must be installed**, or the mug model won't load (Unity reads it from the `.blend`).
- **Placeholders** still in the project: the Breath and Heartbeat sounds (`Mug/Audio/`), the
  painting stages (frames from the video), and the drone (the breath pitched down). The mug's
  printed design was estimated from phone photos and should be checked against the real mug.
- **The headset scenes' origin is where the visitor starts.** A sound placed 3 m in front of the
  origin in `02-Mug` is 3 m in front of wherever they put the headset on. The real room's walls
  aren't in the scene.
- **The mug is easier to track on a dark surface.** A white mug on a white table is hard for the
  cameras to find. A dark cloth or tray under it helps.
- For how each piece works in depth (tracking, detection, every setting), see the
  [mug guide](MemoriesOfTheFuture/Assets/Mug/README.md) and the
  [painting guide](MemoriesOfTheFuture/Assets/PictureChanges/README.md).
