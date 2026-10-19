using System;
using Brix.Game.AI;
using Brix.Game.Components;
using Brix.Game.Rules;
using UnityEngine;

namespace CastleStoryPlus.GameUi;

// The home crystal's energy and new bricktrons (FireflyNest.GetInfo and SpawnUnit, Data_FireflyNest.lua).
internal static class FireflyInfo
{
	private static FireflyNest Nest()
	{
		try
		{
			return FireflyNest.GetAlliedFireflyNest();
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static float Current()
	{
		FireflyNest nest = Nest();
		return (nest != null) ? nest.GetAvailablePurifiedEnergy() : 0f;
	}

	private static float Total()
	{
		FireflyNest nest = Nest();
		return (nest != null) ? GeneralRules.Main.GetHomeCrystalSpawnRequiredEnergy(nest.GetActiveOrSpawnableCharacterCount()) : 0f;
	}

	internal static float Progress()
	{
		float total = Total();
		return (total > 0f) ? Mathf.Clamp01(Current() / total) : 1f;
	}

	internal static string ProgressText()
	{
		return Mathf.FloorToInt(Current()) + "/" + Mathf.FloorToInt(Total());
	}

	internal static int Spawnable()
	{
		FireflyNest nest = Nest();
		return (nest != null) ? (int)nest.newSpawnableFireflyCount : 0;
	}

	internal static int AutoRespawn()
	{
		FireflyNest nest = Nest();
		return (nest != null) ? (int)nest.autoRespawnFireflyCount : 0;
	}

	internal static void SpawnUnit()
	{
		FireflyNest nest = Nest();
		if (nest != null)
		{
			nest.AssignJobRequest(Occupation.Type.Builder, null);
		}
	}
}
