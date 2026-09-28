# Audio credits

Every sound in Vibe Factory is a recorded clip. None of them are generated in code.

## Music

The music is by Kevin MacLeod (incompetech.com). It is licensed under Creative Commons:
By Attribution 4.0 License, http://creativecommons.org/licenses/by/4.0/

| File | Track | Where it plays |
| --- | --- | --- |
| `music/dreamer.ogg` | "Dreamer" | Main menu |
| `music/airport_lounge.ogg` | "Airport Lounge" | In game |
| `music/chill_wave.ogg` | "Chill Wave" | In game |
| `music/lobby_time.ogg` | "Lobby Time" | In game |

Changes: the tracks were converted from MP3 to Ogg Vorbis and loudness-normalised
(EBU R128, -19 LUFS) so they sit quietly under the game. Nothing else was edited.

## Sound effects

The effects are by Kenney (www.kenney.nl), released under CC0 1.0 Universal (public
domain, https://creativecommons.org/publicdomain/zero/1.0/). No credit is required,
but it is given gladly.

| Game sound | Kenney pack | Source clips |
| --- | --- | --- |
| `place_light_0..4` (belts, splitters) | Impact Sounds | `impactPlate_light_000..004` |
| `place_heavy_0..4` (machines) | Impact Sounds | `impactMetal_light_000..004` |
| `remove_0..4` | Impact Sounds | `impactWood_light_000..004` |
| `rotate_0..1` | Interface Sounds | `tick_002`, `tick_004` |
| `upgrade_0` | Interface Sounds | `confirmation_004` |
| `step_done_0` (tutorial) | Interface Sounds | `confirmation_001` |
| `click_0..1` | Interface Sounds | `click_002`, `click_003` |
| `open_0`, `close_0` (windows) | Interface Sounds | `open_001`, `close_001` |
| `toggle_0` | Interface Sounds | `toggle_002` |
| `select_0` | Interface Sounds | `select_002` |
| `error_0` | Interface Sounds | `error_004` |
| `sell_0..4` | Casino Audio | `chips-stack-1`, `-2`, `-4`, `-5`, `-6` |
| `tier_unlocked_0` | Music Jingles | `jingles_SAX07` |
| `contract_done_0` (order filled) | Music Jingles | `jingles_STEEL01` |
| `milestone_0` | Music Jingles | `jingles_PIZZI07` |

The clips were kept as they are (Ogg Vorbis, unchanged). Volume, pitch variation and
cool-downs are set in `scripts/Audio/AudioManager.cs`.
