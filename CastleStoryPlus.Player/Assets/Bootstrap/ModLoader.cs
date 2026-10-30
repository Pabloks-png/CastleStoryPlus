namespace CastleStoryPlus.Player
{
	// Where BepInEx starts its chainloader (the Castle Story Plus mod and other plugins): its preloader adds the start to
	// this type's static constructor (tools/player-bepinex.sh sets the entry point). BepInEx's default, Unity's
	// Application, is initialized before the player is ready: before the game's folder is the working directory, and so
	// early that a plugin adding components crashes Unity 6. Bootstrap starts it once the game's content is loaded, before
	// the game's first scene. Without BepInEx this does nothing.
	public static class ModLoader
	{
		public static bool Started { get; private set; }

		public static void Start()
		{
			Started = true;
		}
	}
}
