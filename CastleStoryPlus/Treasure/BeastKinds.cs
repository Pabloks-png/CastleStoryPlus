using BepInEx.Configuration;
using Brix.External.Factories;
using Brix.Game.AI;
using UnityEngine;

namespace CastleStoryPlus.Treasure;

internal enum BeastKind
{
	Spirestalker,
	Cindermane,
	Boulderback,
	Ironhorn
}

// The four treasure guardians: twisted animals drawn as smooth models (BeastMesh) over an ordinary enemy unit, which
// does their walking, path finding and fighting. Each has its base unit and how much tougher, harder hitting and faster it is.
internal sealed class BeastInfo
{
	public BeastKind Kind;

	public string Name;

	public Factory.AssetKey Unit;

	public Occupation.Type Occupation;

	public BeastMesh Model;

	public ConfigEntry<float> Health;

	public ConfigEntry<float> Damage;

	public ConfigEntry<float> Speed;

	// Stride cycles per block walked.
	public float StepsPerBlock = 0.9f;
}

internal static class BeastKinds
{
	internal static readonly BeastInfo[] All = new BeastInfo[4];

	internal static void Bind(ConfigFile cfg)
	{
		All[(int)BeastKind.Spirestalker] = Make(cfg, BeastKind.Spirestalker, "Spirestalker", Test.Corruptron, Occupation.Type.Corruptron, 2.25f, 0.8f, 0.85f);
		All[(int)BeastKind.Cindermane] = Make(cfg, BeastKind.Cindermane, "Cindermane", Test.Corruptron, Occupation.Type.Corruptron, 2.6f, 1.1f, 1.35f);
		All[(int)BeastKind.Boulderback] = Make(cfg, BeastKind.Boulderback, "Boulderback", Test.Biftron, Occupation.Type.Biftron, 1.9f, 1f, 0.75f);
		All[(int)BeastKind.Ironhorn] = Make(cfg, BeastKind.Ironhorn, "Ironhorn", Test.Biftron, Occupation.Type.Biftron, 1.5f, 1.1f, 1.1f);
		All[(int)BeastKind.Spirestalker].StepsPerBlock = 0.45f;
		All[(int)BeastKind.Cindermane].StepsPerBlock = 1.1f;
		All[(int)BeastKind.Boulderback].StepsPerBlock = 0.8f;
		All[(int)BeastKind.Ironhorn].StepsPerBlock = 0.85f;
	}

	private static BeastInfo Make(ConfigFile cfg, BeastKind kind, string name, Factory.AssetKey unit, Occupation.Type occupation, float health, float damage, float speed)
	{
		string section = "Treasure." + name;
		BeastInfo info = new BeastInfo
		{
			Kind = kind,
			Name = name,
			Unit = unit,
			Occupation = occupation,
			Model = new BeastMesh(name),
			Health = cfg.Bind(section, "Health", health, name + ": takes 1/Health of the damage its base unit would."),
			Damage = cfg.Bind(section, "Damage", damage, name + ": its hits do Damage times its base unit's."),
			Speed = cfg.Bind(section, "Speed", speed, name + ": walks and runs Speed times as fast as its base unit.")
		};
		// The health was lowered by a quarter; a config still holding the old default takes the new one.
		if (Mathf.Abs(info.Health.Value - OldHealth(kind)) < 0.001f)
		{
			info.Health.Value = health;
		}
		return info;
	}

	private static float OldHealth(BeastKind kind)
	{
		switch (kind)
		{
			case BeastKind.Spirestalker:
				return 3f;
			case BeastKind.Cindermane:
				return 3.5f;
			case BeastKind.Boulderback:
				return 2.5f;
			default:
				return 2f;
		}
	}
}
