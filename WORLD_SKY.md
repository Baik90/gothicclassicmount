# Gothic world sky

The mounted `WORLD.ZEN` scene automatically includes `Gothic Sky and Barrier`,
with a `SkyBox2D`, ambient light and a daylight child. The barrier itself is a
separate, two-sided hemisphere model centered over the colony, using additive
animated lightning from the original barrier texture. Other worlds, including
interiors, do not receive this sky. Existing scene lights are retained.

For an existing scene, use **Editor > Gothic Classic Mount > Import World Sky
and Barrier**, or `gothic_import_world_sky`. Save locally authored scenes after
importing. Repeating the import does not duplicate the sky or lights.

The `gothic_sky` shader uses the original mounted `SKYDAY_LAYER0_A0-C.TEX`,
`SKYDAY_LAYER1_A0-C.TEX` and `BARRIERE-C.TEX` textures. Clouds drift and the blue
barrier slowly pulses. This is a visual sky effect, without barrier collision,
damage, thunder audio or a day/night cycle.

The generated material is registered at
`mount://gothicclassic/_gothicruntime/materials/gothic_classic/colony_sky.vmat`.
Default cloud speed and barrier strength are set in `Editor/GothicWorldSky.cs`.
The scene mesh is also registered as a mounted resource so its serialized model
and collider references can load again after closing the scene or editor.

Reload regression check: with the imported WORLD scene open, run
`gothic_validate_world_reload`. This serializes its objects, refreshes the mount,
then loads the saved JSON into a temporary scene. It checks every renderer's
model and transform and the sky material. It leaves the active scene's objects
and user edits intact. World prefab source IDs are assigned after the engine's
PrefabBuilder has finished, so they survive remounts. Previously registered
scene definitions are rebuilt when the mount reloads to avoid stale contents.
