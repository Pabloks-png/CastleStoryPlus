using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Brix.Game;
using Brix.Game.AI;
using Brix.IO.Serialization.Contracts;
using CastleStoryPlus.Core;
using HarmonyLib;
using Newtonsoft.Json.Serialization;
using UnityEngine;
using UnityEngine.Networking;

namespace CastleStoryPlus.Army;

// Call to arms configuration of the local player.
// Quotas persist in PlayerPrefs. Rally points are kept per team (Faction): the server holds them, saves them with the
// world (CallToArmsSave) and syncs them to the clients (CallToArmsRallyNetwork); a client sets its own team's points
// locally at once and tells the server (CallToArmsNetwork.SendSetRally). A new game has new Faction objects, so its
// rally points start empty.
internal static class CallToArmsSettings
{
	public const int MaxQuota = 99;

	private const string QuotaPrefix = "CallToArms.Quota.";

	private sealed class Rally
	{
		public bool HasRanged;

		public Vector3 Ranged;

		public bool HasMelee;

		public Vector3 Melee;
	}

	private static readonly Dictionary<Faction, Rally> Rallies = new Dictionary<Faction, Rally>();

	public static bool HasRanged
	{
		get
		{
			Rally rally = Of(LocalFaction(), create: false);
			return rally != null && rally.HasRanged;
		}
	}

	public static bool HasMelee
	{
		get
		{
			Rally rally = Of(LocalFaction(), create: false);
			return rally != null && rally.HasMelee;
		}
	}

	public static Vector3 Ranged
	{
		get
		{
			Rally rally = Of(LocalFaction(), create: false);
			return (rally != null) ? rally.Ranged : Vector3.zero;
		}
	}

	public static Vector3 Melee
	{
		get
		{
			Rally rally = Of(LocalFaction(), create: false);
			return (rally != null) ? rally.Melee : Vector3.zero;
		}
	}

	public static bool AnyQuota
	{
		get
		{
			foreach (Occupation.Job job in CallToArms.SoldierJobs)
			{
				if (GetQuota(job) > 0)
				{
					return true;
				}
			}
			return false;
		}
	}

	public static int GetQuota(Occupation.Job job)
	{
		return Mathf.Clamp(PlayerPrefs.GetInt(QuotaPrefix + job, 0), 0, MaxQuota);
	}

	public static void SetQuota(Occupation.Job job, int value)
	{
		PlayerPrefs.SetInt(QuotaPrefix + job, Mathf.Clamp(value, 0, MaxQuota));
		PlayerPrefs.Save();
	}

	public static void SetRally(bool ranged, Vector3 position)
	{
		SetLocal(ranged, set: true, position);
	}

	public static void ClearRally(bool ranged)
	{
		SetLocal(ranged, set: false, Vector3.zero);
	}

	private static void SetLocal(bool ranged, bool set, Vector3 position)
	{
		Faction faction = LocalFaction();
		if (faction == null)
		{
			return;
		}
		Set(faction, ranged, set, position);
		if (!NetworkServer.active)
		{
			CallToArmsNetwork.SendSetRally(ranged, set, position);
		}
	}

	// Sets or clears one of a team's rally points. On the server it is synced to the clients.
	internal static void Set(Faction faction, bool ranged, bool set, Vector3 position)
	{
		Rally rally = Of(faction, create: true);
		if (rally == null)
		{
			return;
		}
		if (ranged)
		{
			rally.HasRanged = set;
			if (set)
			{
				rally.Ranged = position;
			}
		}
		else
		{
			rally.HasMelee = set;
			if (set)
			{
				rally.Melee = position;
			}
		}
		if (NetworkServer.active)
		{
			faction.SetDirtyBit(CallToArmsRallyNetwork.DirtyBit);
		}
	}

	private static Faction LocalFaction()
	{
		return (User.LocalUser != null) ? User.LocalUser.faction : null;
	}

	private static Rally Of(Faction faction, bool create)
	{
		if (faction == null)
		{
			return null;
		}
		if (!Rallies.TryGetValue(faction, out Rally rally) && create)
		{
			// Teams of a game that was left.
			List<Faction> gone = new List<Faction>();
			foreach (Faction key in Rallies.Keys)
			{
				if (key == null)
				{
					gone.Add(key);
				}
			}
			foreach (Faction key in gone)
			{
				Rallies.Remove(key);
			}
			rally = new Rally();
			Rallies[faction] = rally;
		}
		return rally;
	}

	// Saves: "r:x,y,z;m:x,y,z" with only the points that are set; empty when none is.
	internal static string Encode(Faction faction)
	{
		Rally rally = Of(faction, create: false);
		if (rally == null)
		{
			return string.Empty;
		}
		StringBuilder text = new StringBuilder();
		if (rally.HasRanged)
		{
			text.Append("r:").Append(Format(rally.Ranged));
		}
		if (rally.HasMelee)
		{
			if (text.Length > 0)
			{
				text.Append(';');
			}
			text.Append("m:").Append(Format(rally.Melee));
		}
		return text.ToString();
	}

	internal static void Decode(Faction faction, string text)
	{
		if (faction == null || string.IsNullOrEmpty(text))
		{
			return;
		}
		foreach (string part in text.Split(';'))
		{
			if (part.Length < 3 || part[1] != ':' || (part[0] != 'r' && part[0] != 'm'))
			{
				continue;
			}
			if (TryParse(part.Substring(2), out Vector3 position))
			{
				Set(faction, part[0] == 'r', set: true, position);
			}
		}
	}

	private static string Format(Vector3 position)
	{
		return position.x.ToString("R", CultureInfo.InvariantCulture) + "," + position.y.ToString("R", CultureInfo.InvariantCulture) + "," + position.z.ToString("R", CultureInfo.InvariantCulture);
	}

	private static bool TryParse(string text, out Vector3 position)
	{
		position = Vector3.zero;
		string[] parts = text.Split(',');
		if (parts.Length != 3)
		{
			return false;
		}
		float[] values = new float[3];
		for (int i = 0; i < 3; i++)
		{
			if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || float.IsNaN(values[i]) || float.IsInfinity(values[i]))
			{
				return false;
			}
		}
		position = new Vector3(values[0], values[1], values[2]);
		return true;
	}

	// Network: a flags byte (CallToArms.FlagRanged / FlagMelee), then both points.
	internal static void Write(Faction faction, NetworkWriter writer)
	{
		Rally rally = Of(faction, create: false);
		int flags = (rally == null) ? 0 : ((rally.HasRanged ? CallToArms.FlagRanged : 0) | (rally.HasMelee ? CallToArms.FlagMelee : 0));
		writer.Write((byte)flags);
		writer.Write((rally != null) ? rally.Ranged : Vector3.zero);
		writer.Write((rally != null) ? rally.Melee : Vector3.zero);
	}

	internal static void Read(Faction faction, NetworkReader reader)
	{
		int flags = reader.ReadByte();
		Vector3 ranged = reader.ReadVector3();
		Vector3 melee = reader.ReadVector3();
		Rally rally = Of(faction, create: flags != 0);
		if (rally == null)
		{
			return;
		}
		rally.HasRanged = (flags & CallToArms.FlagRanged) != 0;
		rally.Ranged = ranged;
		rally.HasMelee = (flags & CallToArms.FlagMelee) != 0;
		rally.Melee = melee;
	}
}

// Saves the team's rally points as an extra property on the existing Faction component ("callToArmsRally":
// "r:x,y,z;m:x,y,z"). The unmodded game ignores unknown properties, so saves stay loadable without the mod.
[Feature(Features.CallToArms, Features.CallToArmsInfo)]
[HarmonyPatch(typeof(DefaultGameContractResolver), nameof(DefaultGameContractResolver.GenerateDefaultMonoBehaviourContract))]
internal static class CallToArmsSave
{
	private const string Property = "callToArmsRally";

	private class RallyProvider : IValueProvider
	{
		public object GetValue(object target)
		{
			return CallToArmsSettings.Encode(target as Faction);
		}

		public void SetValue(object target, object value)
		{
			CallToArmsSettings.Decode(target as Faction, Convert.ToString(value));
		}
	}

	private static void Postfix(Type objectType, JsonObjectContract __result)
	{
		if (objectType != typeof(Faction) || __result == null || __result.Properties.Contains(Property))
		{
			return;
		}
		__result.Properties.AddProperty(new JsonProperty
		{
			PropertyName = Property,
			UnderlyingName = Property,
			PropertyType = typeof(string),
			DeclaringType = typeof(Faction),
			ValueProvider = new RallyProvider(),
			Readable = true,
			Writable = true
		});
	}
}

// Syncs the rally points by appending them to Faction's own network data, in both the initial state and every
// update. Both ends must run the mod (modded and unmodded peers cannot play together).
[Feature(Features.CallToArms, Features.CallToArmsInfo)]
[HarmonyPatch(typeof(Faction), nameof(Faction.OnSerialize))]
internal static class CallToArmsRallyNetwork
{
	// A sync var bit the game does not use (Faction uses 0x01-0x1000); setting it makes UNET send an update.
	public const uint DirtyBit = 0x20000000u;

	private static void Postfix(Faction __instance, NetworkWriter writer, ref bool __result)
	{
		CallToArmsSettings.Write(__instance, writer);
		__result = true;
	}
}

[Feature(Features.CallToArms, Features.CallToArmsInfo)]
[HarmonyPatch(typeof(Faction), nameof(Faction.OnDeserialize))]
internal static class CallToArmsRallyNetworkRead
{
	private static void Postfix(Faction __instance, NetworkReader reader)
	{
		CallToArmsSettings.Read(__instance, reader);
	}
}
