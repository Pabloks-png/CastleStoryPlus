using Brix.Game;
using Brix.Game.AI;
using Brix.Game.Network;
using CastleStoryPlus.Core;
using CastleStoryPlus.Workers;
using UnityEngine;
using UnityEngine.Networking;

namespace CastleStoryPlus.Army;

// Three UNET commands on the player's UNetProjectCmd: call a worker to arms (kit choice + rally points),
// set a worker's call to arms role, and set or clear one of the player's team rally points (kept on the server so
// they are saved and synced, see CallToArmsSettings). Registered by hand, like the game's weaved commands.
[Feature(Features.CallToArms, Features.CallToArmsInfo)]
internal static class CallToArmsNetwork
{
	private const int RallyHash = 1129595218;

	private const int RoleHash = 1129595219;

	private const int SetRallyHash = 1129595236;

	private static void Enable()
	{
		NetworkBehaviour.RegisterCommandDelegate(typeof(UNetProjectCmd), RallyHash, InvokeRally);
		NetworkBehaviour.RegisterCommandDelegate(typeof(UNetProjectCmd), RoleHash, InvokeSetRole);
		NetworkBehaviour.RegisterCommandDelegate(typeof(UNetProjectCmd), SetRallyHash, InvokeSetRally);
	}

	public static void SendRally(GameObject laborGO, GameObject crystalGO, int job, Vector3 ranged, Vector3 melee, int flags)
	{
		UNetProjectCmd cmd = User.LocalUser.GetComponent<UNetProjectCmd>();
		if (cmd.isServer)
		{
			Rally(cmd, laborGO, crystalGO, job, ranged, melee, flags);
			return;
		}
		NetworkWriter writer = Begin(cmd, RallyHash);
		writer.Write(laborGO);
		writer.Write(crystalGO);
		writer.WritePackedUInt32((uint)(job + 1));
		writer.Write(ranged);
		writer.Write(melee);
		writer.WritePackedUInt32((uint)flags);
		cmd.SendCommandInternal(writer, 0, "CmdCallToArmsRally");
	}

	public static void SendSetRole(GameObject laborGO, int role)
	{
		UNetProjectCmd cmd = User.LocalUser.GetComponent<UNetProjectCmd>();
		if (cmd.isServer)
		{
			SetRole(cmd, laborGO, role);
			return;
		}
		NetworkWriter writer = Begin(cmd, RoleHash);
		writer.Write(laborGO);
		writer.WritePackedUInt32((uint)role);
		cmd.SendCommandInternal(writer, 0, "CmdSetCallToArmsRole");
	}

	// Client: tells the server about a rally point of the player's team (set, or cleared when set is false).
	public static void SendSetRally(bool ranged, bool set, Vector3 position)
	{
		UNetProjectCmd cmd = (User.LocalUser != null) ? User.LocalUser.GetComponent<UNetProjectCmd>() : null;
		if (cmd == null || cmd.isServer)
		{
			return;
		}
		NetworkWriter writer = Begin(cmd, SetRallyHash);
		writer.Write(ranged);
		writer.Write(set);
		writer.Write(position);
		cmd.SendCommandInternal(writer, 0, "CmdSetCallToArmsRally");
	}

	private static NetworkWriter Begin(UNetProjectCmd cmd, int hash)
	{
		NetworkWriter writer = new NetworkWriter();
		writer.Write((short)0);
		writer.Write((short)5);
		writer.WritePackedUInt32((uint)hash);
		writer.Write(cmd.GetComponent<NetworkIdentity>().netId);
		return writer;
	}

	private static void InvokeRally(NetworkBehaviour obj, NetworkReader reader)
	{
		if (!NetworkServer.active)
		{
			return;
		}
		Rally((UNetProjectCmd)obj, reader.ReadGameObject(), reader.ReadGameObject(), (int)reader.ReadPackedUInt32() - 1, reader.ReadVector3(), reader.ReadVector3(), (int)reader.ReadPackedUInt32());
	}

	private static void InvokeSetRole(NetworkBehaviour obj, NetworkReader reader)
	{
		if (!NetworkServer.active)
		{
			return;
		}
		SetRole((UNetProjectCmd)obj, reader.ReadGameObject(), (int)reader.ReadPackedUInt32());
	}

	private static void InvokeSetRally(NetworkBehaviour obj, NetworkReader reader)
	{
		if (!NetworkServer.active)
		{
			return;
		}
		bool ranged = reader.ReadBoolean();
		bool set = reader.ReadBoolean();
		Vector3 position = reader.ReadVector3();
		User user = obj.GetComponent<User>();
		Faction faction = (user != null) ? user.faction : null;
		if (faction != null)
		{
			CallToArmsSettings.Set(faction, ranged, set, position);
		}
	}

	private static void Rally(UNetProjectCmd cmd, GameObject laborGO, GameObject crystalGO, int job, Vector3 ranged, Vector3 melee, int flags)
	{
		if (laborGO == null || !Affiliation.IsOwnedBy(laborGO, cmd.gameObject))
		{
			return;
		}
		CallToArms.Orders orders = new CallToArms.Orders
		{
			Crystal = crystalGO,
			Job = job,
			HasRanged = (flags & CallToArms.FlagRanged) != 0,
			Ranged = ranged,
			HasMelee = (flags & CallToArms.FlagMelee) != 0,
			Melee = melee
		};
		CallToArms.EquipAndRally.Send(laborGO.GetComponent<Labor>(), orders, Labor.DecisionSource.User);
	}

	private static void SetRole(UNetProjectCmd cmd, GameObject laborGO, int role)
	{
		if (laborGO == null || !Affiliation.IsOwnedBy(laborGO, cmd.gameObject))
		{
			return;
		}
		Labor labor = laborGO.GetComponent<Labor>();
		if (labor != null && labor.state != null)
		{
			WorkerStats.Modify(labor.state, (WorkerStats s) => s.CallToArmsRole = role);
		}
	}
}
