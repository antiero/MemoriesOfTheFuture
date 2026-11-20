# Shared: app-wide scripts

Code used by every scene, in the `MemoriesOfTheFuture` namespace.

## Switching scenes

| Build index | Scene | Role |
| --- | --- | --- |
| 0 | `00-Boot` | Opens the scene that was used last (default `01-PictureChanges`), then is gone |
| 1 | `01-PictureChanges` | Glyph-tracked painting |
| 2 | `02-Mug` | The mug: scream, tunnel and dream |

- **On the headset:** turn your left palm towards your face and press a scene button in the bottom row (**Painting**, **Mug**) with your right index finger. The open scene's button is lit.
- **Remembered between launches:** every scene that loads saves its name in PlayerPrefs (`MemoriesOfTheFuture.LastScene`), so the next launch opens it again. Uninstalling the app or clearing its data resets this to `01-PictureChanges`.
- **In the Editor:** press Play in any scene to run it directly. Play `00-Boot` to test the launch behaviour. Editor PlayerPrefs are separate from the headset's.

| Script | What it does |
| --- | --- |
| `SceneSwitcher` | Loads a scene (async, single), ignores repeat presses while loading, records the last scene |
| `AppBoot` | In `00-Boot`: loads `SceneSwitcher.LastScene` |
| `SceneMenu` | Adds the scene buttons to a scene's `HandMenu` on their own row |

## Adding a scene

1. Add it to the **Meta Quest** build profile's scene list (File > Build Profiles), after `00-Boot`. The profile's list is what the Quest build uses.
2. Give it the same rig as the other scenes, plus a `HandMenu` with a `SceneMenu` (copy the `HandMenu` object from `02-Mug` and re-link the hand and head references).
3. Add an entry (button label + scene name) to the `Scenes` list on **every** scene's `SceneMenu`.
