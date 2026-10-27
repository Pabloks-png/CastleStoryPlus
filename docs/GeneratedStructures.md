# Generated structures

The rules for every structure the mod builds out of the game's blocks by itself, such as the treasure ruins
(`CastleStoryPlus/Treasure/TreasureRuins.cs`). New generated buildings must follow them. That file is the reference
implementation.

## Building blocks

- Use the game's own built blocks and spawn them with `Transactor.SpawnUNet`. They are saved, dug and climbed like
  player buildings.
  - The built block's key is the `BlueprintResult` of its blueprint (`PlacedBlock.<name>`).
  - A `Blocks.*` key is the loose block, which falls and is carried like a resource. It is what a block that loses its
    support turns into. Never spawn one as part of a structure. They belong to `Faction.DefaultNeutral` unless the structure is a player's.
- Do not add new serialized component types, so that saves stay compatible.

## Masonry: 1x2 bricks in a bond

- Do not build walls and floors from 1x1 blocks only. First work out the structure cell by cell. Then lay each layer in
  1x2 bricks (`Stone_DoubleBrick`), as a mason would:
  - the bricks run along x on even layers and along z on odd ones;
  - the joints move one cell from layer to layer: a brick starts where `(x or z) + y` is even, so a wall's joints are
    staggered.
- Where the bond's direction does not fit, use the other direction. A cell that no brick fits is a single
  `Stone_Brick`.
- Merlons (`Stone_Merlon`, 1 wide and 2 high) go on top of the laid walls.
- To place a block that fills more than one cell, do what the game does with a finished blueprint (`BlockEngine`):
  - rotation: `Quaternion.Euler(0, 90 * turns, 0)`;
  - position: `root - rotation * CenterOffset`, where `CenterOffset` comes from the blueprint's `BaseVoxelTransform`;
  - cells: read them from `VolumePreprocessor.GetIndexedVSettings(blueprintKey, rotationIndex)`, with
    `PreprocessedRotations.RotationToDiscreteIndex`. `FreeVolume.Make` with the finished block's key gives no cells.

## Standing on the ground

- The ground's smooth surface layer (the crust, `Voxel.IsCrust`) is no support: a block resting on it falls. Put the
  lowest blocks in the crust's cells, as a player's block built on grass does. The block then rests on the terrain
  under the crust.
- Never remove or change terrain voxels. Digging the crust away leaves the ground under it as no support too, and it
  changes the walkable ground under the bricktrons' path finding.
- Fill the floor down to the ground where the ground dips, so that nothing hangs in the air.

## Building order

- The game sees a block as resting on another only from the frame after that one was made. A whole structure spawned
  in one frame collapses into a heap.
- Make a block at once only when everything under it is ground (terrain that is not crust).
- Queue every other block until all the blocks under it are at least one frame old. Lay the queue lowest first,
  every frame on the server.
- Drop a queued block when it is still waiting after a timeout.

## Visibility

- Neutral objects that are not units show through the fog of war (`RuinsInFog`). Generated structures of the neutral
  faction are seen by every player.
