# Adding spatial sound

How to add a new sound that comes from a place in the room: a fox outside the window, a hum in a
corner, a heartbeat inside the mug. No code is needed. For everything else about sound (the mixer,
snapshots, trying files in the desktop app), see `README - Sound Design.md` in this folder.

## What "spatial" means here

A spatial sound comes from **one point**. Turn your head and it stays where it is; walk towards it
and it gets louder. On the headset, Meta's spatialiser makes it sound as if it's in the room with
you, including the room's own echoes.

Every sound is a **Sound Slot** (the file, level and space settings) played by a **Sound Emitter**
(where it is in the room and when it plays). You make one of each.

## Start with the examples

Open **03-DesktopTesting** and press **Play** (headphones help). Three examples are in the room, in
the **Sound Examples** object in the Hierarchy:

| Example | Where | When | Try this |
|---|---|---|---|
| **Fox Outside** | Fixed: the window on the north wall | Always | Turn around and walk about: it stays at the window |
| **Corner Hum** | Fixed: the speaker in the south-west corner | While Near (1.6 m) | Walk up to the corner: it fades in, and fades out as you leave |
| **Held Mug Heartbeat** | On the mug | While Holding Mug | Press **1** then **G** to pick the mug up: it builds with the dread and follows the mug |

Press **O** to see where every sound is. Each one is a ball with its name and level above it. The
ball lights up blue and swells while the sound plays, and is a grey dot while it's silent. Sounds
that play in your ears (the stereo Background) have no place, so they have no ball.

Their slots are in `Assets/Audio/Slots/Examples`. The files are placeholders: replace them, or copy
an example to start your own (below).

## Adding your own, step by step

### 1. Prepare the file

- **Mono** WAV, 48 kHz, 16 or 24 bit. A spatial sound is a single point, so a stereo file gets
  folded to mono anyway.
- Trim any silence from the ends of a loop, so it loops without a gap.
- Name it after your sound: `fox_outside.wav`, `heartbeat_v2.wav`.

### 2. Make a Sound Slot

1. **Memories > Sound Design** (menu bar), then **New Slot**. Rename it in the Project window
   (`Clock Tick`, say).
2. Press **⋯** on its box and fill in:
   - **Drop Name**: the start of your file names, for example `clock`. Files called `clock...`
     dropped into the Drop Folder then go into this slot by themselves.
   - **Notes**: what it is and when it plays.
   - **Mixer**: the group it belongs with (Ambience, Mug, Dream, Painting).
3. Add the file: drag it onto the slot's box, or put it in the Drop Folder (**Drop Folder** button).

### 3. Set how it sits in space

Still under **⋯**:

| Setting | What it does | Start with |
|---|---|---|
| **Spatial** | On: comes from a point. Off: plain stereo in your ears | On |
| **Min Distance** | Full volume within this distance (m) | 0.3 for something small (a mug), 1 for a room-filling sound |
| **Max Distance** | It stops getting quieter beyond this (m) | 5 to 10 indoors |
| **Rolloff** | How fast it falls off between the two | Logarithmic (natural) |
| **Room Acoustics** | Gets the real room's reflections on the headset | On |
| **Reverb Send** | More (or less) room echo for this sound only | 0 dB |
| **Level dB** | Loudness. Spatial sounds can go up to +20 dB | Adjust by ear |

### 4. Put it in the scene

1. Open the scene: **03-DesktopTesting** to try it on the Mac, **02-CupSpike** for the headset.
2. Select your slot in the Project window, then **Add Sound to Scene** in the Sound Design window.
   A **Sound - Clock Tick** object appears, already pointing at your slot.
3. Move it to where the sound should come from (W in the Scene view, then drag the arrows).
4. In the Inspector, choose:

**When** it plays:

| When | Plays | Good for |
|---|---|---|
| Always | Loops from the start | Ambience fixed in the room |
| While Near | Loops, fades in within **Near Distance** of it | Sounds to discover by walking up |
| While Hand Approaches | Loops, louder as a hand nears the mug | Building tension before the touch |
| While Looking In | Loops, fades in as the mug opens | The tunnel |
| While Holding Mug | Loops, builds with the dread, stays through the dream | Anything about holding the mug |
| While Dreaming | Loops, fades in as the dream deepens | The dream world |
| When Mug Screams | Once, each time it screams | Layers on the scream |
| When Waking | Once, each time you wake | The wake-up |

**Where** it is:

| Where | The sound is | Use |
|---|---|---|
| Here In The Scene | Where you put the object | Fixed places in the room |
| On The Mug | On the real mug, plus **Offset** (+Y is up the mug), and moves with it | Sounds from or inside the mug |
| On The Listener | At **Offset** from your head, turning with you | Something close behind you: Offset (0, 0, -0.6) |

**Fade** sets how quickly the looping ones fade in and out.

5. Save the scene (**Cmd+S**).

The quickest way to start is to copy an example: select **Sound - Fox Outside** in the Hierarchy,
**Cmd+D**, rename the copy, give it your slot and move it.

### 5. Try it

Press **Play** in 03-DesktopTesting, press **O**, and walk to it. While it plays you can change the
slot's level, pitch and distances in the Sound Design window (or the **Tab** panel in the game)
and hear the change straight away. Slot changes are kept when you stop.

In the Scene view, each emitter is a blue dot. Select it to see its Min and Max Distance as wire
spheres, and, for While Near, its Near Distance in orange.

### 6. On the headset

Spatial sound on a laptop is only a rough guide, so **always check on the headset**. Things to know:

- In **02-CupSpike** the scene is the real room, and its origin is **where the visitor starts**.
  A sound placed 3 m in front of the origin is 3 m in front of where they put the headset on.
  The real room's walls aren't in the scene, so place fixed sounds by distance from the start,
  not by walls.
- **On The Mug** and **On The Listener** work the same everywhere: they follow the real mug and
  the visitor's head.
- Add your emitter to 02-CupSpike as well as 03-DesktopTesting if you want it in both (or build
  it once in 03, then copy and paste the object across: **Cmd+C** in one scene, **Cmd+V** in the
  other, and check its position).

### 7. Save your work

Commit, on your own branch, together:

- the sound file and its `.meta`
- the slot (`Assets/Audio/Slots/...asset`) and its `.meta`
- the scene you changed

Try not to change the same scene as someone else on the same day; scene files are hard to merge.

## Tips

- **Don't stack too many loops in one place.** The mug already has the Breath, Drone, Scream and
  Heartbeat; press O near it to see them.
- **Small things, small Min Distance.** A tiny Min Distance (0.2 to 0.3 m) makes a sound feel
  like it comes from a small object. A large one makes it feel big and close.
- **Quiet ambience reads as further away.** Keep fixed room sounds a little low; let the visitor
  walk to them.
- **Nothing at your ears.** On The Listener with Offset (0, 0, 0) sits inside your head and
  sounds odd. Keep it at least 0.3 m away.

## If it doesn't work

- **No ball when you press O**: the emitter has no slot, or the slot's **Spatial** is off.
- **Grey ball, "silent"**: its moment hasn't come (While Near: walk closer; While Holding Mug:
  press G), the slot has no file, or its Level is -60 dB.
- **It plays from the wrong place on the headset**: see *On the headset* above. The origin is
  where the visitor starts.
- **Rebuilding the desktop room** (*CupSpike > Create Desktop Testing Scene*) starts the scene
  afresh: the three examples come back, but emitters you added to 03 by hand don't. Copy yours to
  a safe scene first, or re-add them. *CupSpike > Add Sound Examples To Open Scene* puts back just
  the examples.
