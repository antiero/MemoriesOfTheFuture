# Sound design in Memories of the Future

This guide is for working on the sound without touching code. Everything below happens in the
Unity Editor, mostly in one window: **Memories > Sound Design** (menu bar at the top).

## The idea in one minute

- Every sound in the experience is a **Sound Slot**: for the mug, the Scream, the Breath, the
  Drone, the Whisper and the Background bed; for the painting, the Painting Scream and the
  Painting Soundtrack. Slots live in `Assets/Audio/Slots`. A slot says which file plays,
  how loud, at what pitch, through which mixer group, and how it sits in the room.
- The code decides **when** a sound plays and how it moves (for example, the breath gets louder
  as a hand nears the mug). The slot decides **what it sounds like**. You own the slots.
- Everything passes through one **Audio Mixer** (`Assets/Audio/Memories Mixer`), which works like
  a desk: a fader per group, effects, and a **snapshot** per moment of the experience.
- Changes save as you make them, **including while the game is running**. (Unity normally throws
  away changes made in Play mode. Slots and the mixer are exceptions.)

## What happens, and which sound plays

| Moment | What the visitor does | Sounds | Mixer snapshot |
|---|---|---|---|
| Room | Nothing yet | Background bed (fades in over 3 s) | Room |
| Approach | Brings a finger towards the mug (from 30 cm) | Breath rises in level and pitch; the bed drains away | Approach |
| Touch | Touches the mug | Total silence (0.35 s), then the Scream; the bed ducks under it | Approach / Room |
| Looking in | Looks down into the mug | Drone fades in as the mug opens | LookingIn |
| Dream | Keeps staring for 2 s | Two Whispers circle the head; the bed muffles | Dream |
| Waking | Looks away | Dream snaps off, 0.6 s of silence, then the Scream | Dream to Room |

The painting (scene 01-PictureChanges, and the east wall of the desktop room):

| Moment | What the visitor does | Sounds |
|---|---|---|
| Unseen swap (default) | Looks at the painting, then away: it changes behind their back | On the swap to the last stage, the **Painting Scream** from the painting |
| Proximity / Gaze dwell | Walks up and faces it / looks at it for 2 s: the transformation video plays | **Painting Soundtrack** loops from the painting, fading in over 2 s (out over 1.5 s on replay) |

## Trying a new sound file

1. Name the file after the slot it's for: `scream_take3.wav`, `breath_close.wav`, `drone_v2.wav`,
   `whisper_soft.wav`, `background_left_v2.wav`. (Any name that **starts** with the slot's
   *Drop Name* works. You can also make a folder called `scream` and put files in it.)
2. Click **Drop Folder** in the Sound Design window. Finder opens at `Assets/Audio/Drop`. Copy
   your file in.
3. Go back to Unity. In a moment the Console (bottom of the screen) says
   *"Added 'scream_take3' to Scream"*. The new file is now the one that plays.
4. Press **▶** on the slot to hear it, or **▶ Test on desktop** to hear it in the experience.

The older files stay in the slot's list, so you can go back to them: press **⋯** on the slot and
drag the one you want to the top of *Clips*. You can also drag a file from the Project window
straight onto a slot's box.

To make a slot pick a **random file each time** (several scream takes, say), press **⋯** and set
*Variation* to *Random*.

Files in the Drop Folder get headset-friendly import settings automatically the first time (mono
for 3D sounds, compressed). Keep source files at 48 kHz, 16 or 24 bit WAV if you can.

## Mixing

### Slot level and pitch

In the Sound Design window each slot has:

- **Level dB**: 0 is the file as it is. Spatial sounds can go up to +20 dB.
- **Pitch**: 1 is the original. The number next to it is the change in semitones.
- **Mixer**: which group it goes through.
- **S / M**: solo or mute it, while the game runs. These are for listening only and aren't saved.
- **⋯**: every setting, plus notes. Please use the notes to say what each version is.

### The Audio Mixer

Click **Mixer** in the Sound Design window. You'll see one strip per group:

- **Ambience**: the background bed
- **Mug**: scream, breath, drone
- **Dream**: whispers
- **Painting**: the painting's scream and soundtrack
- **UI**: menu sounds

You can add effects to a group (lowpass, EQ, compressor, echo) with **Add Effect** at the bottom of
its strip.

**Snapshots** (left side of the mixer: Room, Approach, LookingIn, Dream) hold a complete mix for
each moment. The game slides between them as the visitor moves through the experience, so you can
make the dream sound different by changing the mixer while the *Dream* snapshot is selected (for
example, bring Ambience down and add a lowpass to it).

To change a snapshot while the game is running:

1. Press **▶ Test on desktop**, and get to the moment you want (for example, the dream).
2. In the mixer, turn on **Edit in Play Mode** (top of the mixer window).
3. In the Sound Design window, untick **Blending** under *Mix right now*, so the game stops
   moving the mixer while you work.
4. Select the snapshot, adjust, and tick **Blending** again.

Mixer changes made in Play mode with *Edit in Play Mode* on are kept.

## Where sounds are in space

Spatial (3D) sounds come from a point in the room and are rendered by Meta's spatialiser, so they
sound like they're in the room with you. For each spatial slot (press **⋯**):

- **Min Distance**: full volume within this distance.
- **Max Distance**: it stops getting quieter beyond this.
- **Rolloff**: how quickly it falls off in between.
- **Room Acoustics** and **Reverb Send**: how much of the real room's reflections it gets.

Turn **Spatial** off for a plain stereo sound in your ears (the stereo Background bed works like this).

While the game runs, **Show in Scene** draws every playing 3D sound in the Scene view as a circle
labelled with its name and level.

**Spatial sound has to be checked on the headset.** The desktop test is good for levels, timing
and files, but on the Mac you're hearing it through laptop audio, not the Quest's spatialiser.

## Adding a sound of your own

1. **New Slot** in the Sound Design window. Rename it in the Project window and fill in its
   *Drop Name* and *Notes*.
2. **Add Sound to Scene** places a **Sound Emitter** in the open scene. Move it where the sound
   should come from, and in the Inspector choose:
   - **Slot**: your new slot.
   - **When**: Always, While Hand Approaches, While Looking In, While Dreaming (these loop and fade
     with the moment), or When Mug Screams, When Waking (these play once).
   - **Where**: here in the scene, on the mug, or on the listener (with an offset).
3. Save the scene (Cmd+S).

Do this in **02-CupSpike** (the headset scene) and **03-CupDesktop** (the desktop test) if you
want it in both. (Rebuilding the desktop room with *CupSpike > Create Desktop Scene* starts it
afresh, so emitters added to 03 by hand have to be added again.) **Open headset scene** in the Sound Design window opens 02-CupSpike.

## Testing

- **Desktop** (no headset): **▶ Test on desktop** in Unity, or the Mac app. See *The desktop
  room* below. **■ Stop** to stop.
- **Headset**: ask the developer for a build, or build from **02-CupSpike** (the mug) or
  **01-PictureChanges** (the painting).

## The desktop room

Scene **03-CupDesktop** (and the Mac app built from it) is one stand-in room with both
experiences running: the mug on the table, and the painting on the wall to your right (east).
Walk between them.

| Key | What it does |
|---|---|
| Mouse | Look around (click the view first; Esc frees the cursor) |
| W A S D | Walk |
| 1 / 2 | Go to the mug / go to the painting (2.5 m in front of it, facing it) |
| Up / Down, scroll | Move a fingertip towards the mug / away |
| E | Go back to the mug and lean over it; E again leans back. Walking also stands you up |
| F | Start the dream now / wake up |
| M | Wake-up: scream or silent |
| Space | The mug screams now |
| Z | Show the mug's touch zones |
| P | Replay the painting (back to its first stage) |
| T | Painting trigger: unseen swap / proximity / gaze dwell |
| L | Reload sound files from the Sounds folder |
| Tab | The panel: **Sound** and **Timings** |
| H | Hide the guide |

### The Sound panel (Tab)

The same slots as the Sound Design window, live, in the game view, so it works in the Mac app too:

- A **dot** lights green while the slot is playing.
- **S / M**: solo or mute.
- **Level** and **Pitch** sliders.
- **File**: the file that plays. **◀ ▶** step through the slot's files. A file from the Sounds
  folder shows as *(Sounds folder)*; **Built-in** goes back to the slot's own files.
- **...**: spatial on or off, distances, room acoustics and reverb send.
- At the top: the mixer snapshots right now, and **Blending** (untick to hold the mix still).

**In Unity**, panel edits change the slot assets, so they're kept when you stop, just like the
Sound Design window.

**In the Mac app**, press **Save mix**. It writes `~/MemoriesOfTheFuture/SoundMix.json` (your
home folder, then *MemoriesOfTheFuture*), and the app loads it every time it starts, so your mix is
still there tomorrow. To bring it into the project, the developer (or you, in Unity) runs
**Memories > Audio > Import Mix From Desktop App**, or clicks **Import mix** in the Sound Design
window. The Console lists what changed; Cmd+Z undoes it.

### Trying a file in the desktop room

1. **Open folder** in the Sound panel opens `~/MemoriesOfTheFuture/Sounds`.
2. Copy in a file whose name starts with the slot's Drop Name: `scream_take3.wav`,
   `painting_soundtrack_v2.wav`. The newest one wins if several match. A `background` file
   (stereo) replaces the whole bed, left and right. The folder's `README.txt` lists every name.
3. Press **L** (or **Reload files**). Loops switch over straight away; one-shots play the new file
   next time.

Files in this folder are only for trying things out. To keep one, put it in the project's Drop
Folder (see *Trying a new sound file*). Remove it from the Sounds folder and press L to hear the
slot's own files again.

## Saving your work (Git)

- Use **GitHub Desktop**, and work on **your own branch**.
- Commit the slots (`Assets/Audio/Slots`), the mixer (`Assets/Audio/Memories Mixer.mixer`), your
  sound files, and their `.meta` files. Always commit a file and its `.meta` together.
- Try not to both change the **same scene** on the same day: scene files are hard to merge.
  Slots and the mixer are separate files for this reason.

## If something goes wrong

- **No sound at all**: check a slot isn't muted or soloed (S / M), the slot's Level isn't -60, and
  the mixer's Master fader is up. Check *Mute Audio* in the Game view toolbar is off.
- **The Console says a file "doesn't match a Sound Slot"**: rename the file so it starts with a
  slot's Drop Name, or drag it onto the slot in the Sound Design window.
- **A component says "no Sound Slot assigned"**: run **Memories > Audio > Set Up Sound Slots and
  Mixer**. It only fills in what's missing and never overwrites your slots.
- **Anything else**: Cmd+Z undoes changes, and Git has every saved version.
