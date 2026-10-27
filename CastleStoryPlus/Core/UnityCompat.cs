namespace CastleStoryPlus.Core;

// Unity API that differs between the game's Unity 5.6 and the Unity 6 build (UNITY6, see Directory.Build.props).
internal static class UnityCompat
{
	// An id for the object while it lives. Unity 6 has no GetInstanceID (EntityId instead, no longer an int); there
	// it is the EntityId's hash, unique enough for keys and the developer API.
	internal static int InstanceId(this UnityEngine.Object obj)
	{
#if UNITY6
		return obj.GetEntityId().GetHashCode();
#else
		return obj.GetInstanceID();
#endif
	}

	// Whether the object was made at runtime rather than loaded from the game's files: in 5.6 those have negative
	// instance ids. Unity 6 has no such tell, so there nothing counts as made at runtime (never destroys an asset).
	internal static bool MadeAtRuntime(UnityEngine.Object obj)
	{
#if UNITY6
		return false;
#else
		return obj.GetInstanceID() < 0;
#endif
	}

	// The engine's own font, for text the mod makes itself: Unity 6 renamed Arial.ttf to LegacyRuntime.ttf.
	internal static UnityEngine.Font BuiltinFont()
	{
#if UNITY6
		return UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf");
#else
		return UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>("Arial.ttf");
#endif
	}
}
