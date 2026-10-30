using CastleStoryPlus.Core;
using HarmonyLib;
using UnityEngine;

namespace CastleStoryPlus.Performance;

// Every object that makes sound (some 300 in a base: each unit, workshop, door) tells Wwise where it is every frame
// (AkGameObj.Update). It read its transform's position, forward and up up to three times each to see whether it had
// moved: a dozen calls into Unity per object per frame. It now reads position and rotation once and works out forward
// and up from the rotation, which is what Unity does; Wwise gets the same values, only when they change, as before.
[Feature(Features.FrameSavers, Features.FrameSaversInfo)]
[HarmonyPatch(typeof(AkGameObj), "Update")]
internal static class SoundObjectPositions
{
	private static bool Prefix(AkGameObj __instance)
	{
		Transform transform = __instance.transform;
		Vector3 position;
		Quaternion rotation;
#if UNITY6
		transform.GetPositionAndRotation(out position, out rotation);
#else
		position = transform.position;
		rotation = transform.rotation;
#endif
		if (__instance.isEnvironmentAware && __instance.m_envData != null)
		{
			__instance.m_envData.UpdateAuxSend(__instance.gameObject, position);
		}
		AkGameObjPositionData data = __instance.m_posData;
		if (__instance.isStaticObject || data == null)
		{
			return false;
		}
		if (__instance.m_positionOffsetData != null)
		{
			position += rotation * __instance.m_positionOffsetData.positionOffset;
		}
		Vector3 forward = rotation * Vector3.forward;
		Vector3 up = rotation * Vector3.up;
		if (data.position == position && data.forward == forward && data.up == up)
		{
			return false;
		}
		data.position = position;
		data.forward = forward;
		data.up = up;
		AkSoundEngine.SetObjectPosition(__instance.gameObject, position.x, position.y, position.z, forward.x, forward.y, forward.z, up.x, up.y, up.z);
		return false;
	}
}
