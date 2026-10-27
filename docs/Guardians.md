# Guardians

Rules for the beasts that guard the treasure chests, and for every new one. The four existing guardians follow them
and are the reference:

| Guardian | Animal | Material | Base unit |
|---|---|---|---|
| Spirestalker | giraffe | blue crystal spires along its neck | corruptron |
| Cindermane | lion | a mane of fire and embers | corruptron |
| Boulderback | gorilla | a back and fists of stone | biftron |
| Ironhorn | rhino | riveted iron plates and a steel horn | biftron |

The models are designed in `tools/beasts/beasts.py`. The game loads them through `CastleStoryPlus/Treasure/`: `BeastKinds`
defines each kind, `BeastMesh` loads the model, `BeastBody` animates it and `BeastHud` draws the icons and health bars.

## How a guardian looks

- **A real animal twisted by one material** of the game's world, such as crystal, fire, stone or iron. The animal must
  be easy to recognise from its silhouette. The material marks it as a guardian and hints at its strength.
- **Its own silhouette.** A new guardian must not be mistaken for another one, or for a bricktron, corruptron or
  biftron, from the game's usual camera height. Give it a different body shape (tall, long, broad or low) and a
  different gait.
- **Smooth, chunky and toy-like,** to fit Castle Story's style:
  - built from a few rounded shapes melted together (round cones, ellipsoids, rounded boxes), with no fine detail
    smaller than about 0.03 blocks;
  - legs, neck and head thick enough to read from far away.
- **Size in blocks:** at least as tall as a bricktron, and no more than about 3 blocks tall. It must fit the paths,
  the gates and the ruins. Ground level is y = 0.
- **Colours:**
  - **coat:** a base colour and a darker shade of it;
  - **material:** one or two colours that stand out against grass, rock and snow;
  - **hooves, claws or skin:** a dark colour;
  - **eyes:** a small, bright glowing colour, the same family as the material.

  Avoid the factions' team colours, so that a guardian is never taken for a player's unit.
- **One pattern** baked into the texture, for example spots, a silverback or plates (`PATTERNS` in `beasts.py`), with
  a little surface noise and ambient occlusion from the bake.

## Model rules

- Units are blocks: x forward, y up, z to the side. The beast faces +x.
- Every moving part is its own part with a pivot where it turns:
  - `Body`, `Head`, `Tail`, and `Arm` where there are arms;
  - legs as `LegF`/`LegB` with an `L`/`R` side.

  Build both sides with `mirror`.
- Bake with `bake.py` to `CastleStoryPlus/Assets/Beasts/<Name>.json` and `<Name>.png`. The texture atlas is
  2048x2048.
- Make the icon with `icons.py`: `<Name>_icon.png`, 64x64, the beast seen from the side (yaw 0.35, pitch 0.15), on a
  transparent background with a dark outline. It is the off-screen marker, so it must show the animal, not a
  corruptron.
- Check the model with `preview.py` (three angles) and `texview.py` (the baked texture) before adding it to the game.

## Behaviour that belongs to the look

- **A gait of its own,** worked out from the unit's speed in `BeastBody`, and a breathing or looking-about idle while
  standing. Set `StepsPerBlock` so that its feet do not slide.
- **An attack of its own** that shows the material or the animal. For example, the Ironhorn lowers its head and
  charges with its horn. The attack has a wind-up while the unit cranks and the blow when it swings.
- **A corpse that lies still,** tipped over.
- **A health bar over its head** (`BeastHud`). Set the bar height to fit the model: 4.6 for the Spirestalker, 3 for
  the others.

## In the game

- A guardian is drawn over an ordinary enemy unit (a corruptron or biftron), which does the walking, path finding and
  fighting.
- Each kind gets `Health`, `Damage` and `Speed` config entries under `[Treasure.<Name>]`.
- Do not add new serialized components: the model is rebuilt on every peer from the unit, and nothing of it is saved.
- Guardians roam round their chest: the guard raid always patrols, to random spots within 9 blocks of it. They never
  break objects or the ruin's blocks to get through (`TreasureChests`, `TreasureRuins`).
- Guardians are calm until a player's unit hits one. They fight only the players' units, never corruptrons or each
  other (`BeastTemper`).
