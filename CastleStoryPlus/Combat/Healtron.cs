using System.Collections.Generic;
using BepInEx.Configuration;
using Brix.Engine;
using Brix.Game;
using Brix.Game.AI;
using Brix.Game.AI.Damage;
using CastleStoryPlus.Core;
using CastleStoryPlus.Giant;
using CastleStoryPlus.Workers;
using UnityEngine;
using UnityEngine.Networking;

namespace CastleStoryPlus.Combat;

// Healtron: an ordinary corruptron with a mod flag (WorkerStats.Variant, saved and synced like a giant's tier) that
// heals its allies. Raids sometimes hire one (HealtronHirePatch). Every PulseSeconds a pulse heals every ally within
// PulseRadius (itself too) by PulseHealPercent of its health, and a beam heals the most wounded ally within BeamRange
// by BeamHealPerSecond of its health per second. It still fights, but its hits do DamageMultiplier of the damage.
// Healing is done by the host; every peer tints the healer green and draws the beam and the pulse ring from the
// synced flag and health (a client picks the beam's target by the same rule, and pulses on its own clock).
[Feature(Features.Healtron, Features.HealtronInfo)]
internal class Healtron : MonoBehaviour
{
	private class Healer
	{
		public Labor Labor;

		public Labor Target;

		public float NextPick;

		public float NextTick;

		public float NextPulse;

		public float PulseStart = -1f;

		public LineRenderer Beam;

		public LineRenderer Ring;
	}

	internal const int VariantHealtron = 1;

	private const float ScanSeconds = 0.5f;

	private const float UnitListSeconds = 1f;

	private const float PickSeconds = 1f;

	private const float TickSeconds = 0.5f;

	private const float RingSeconds = 0.6f;

	private const int RingSegments = 32;

	private const float BeamWidth = 0.07f;

	private const float RingWidth = 0.12f;

	// Height of the beam's ends over a unit's feet (about its chest).
	private const float ChestHeight = 0.8f;

	private static readonly Color BeamColor = new Color(0.3f, 1f, 0.4f, 0.75f);

	internal static ConfigEntry<float> Chance;

	internal static ConfigEntry<int> MaxPerRaid;

	internal static ConfigEntry<int> FromWave;

	internal static ConfigEntry<float> PulseSeconds;

	internal static ConfigEntry<float> PulseRadius;

	internal static ConfigEntry<float> PulseHealPercent;

	internal static ConfigEntry<float> BeamRange;

	internal static ConfigEntry<float> BeamHealPerSecond;

	internal static ConfigEntry<float> DamageMultiplier;

	private static bool _enabled;

	// Set when a unit's variant changes, so the next frame looks for new or lost healers at once.
	private static bool _dirty;

	// Units whose variant was set (made, received or loaded) and that are no healer yet, e.g. a loaded one not yet
	// active. Not cleared on leaving a game: a save may load its stats before that; destroyed ones are pruned.
	private static readonly HashSet<CharacterState> Candidates = new HashSet<CharacterState>();

	private static readonly Dictionary<CharacterState, Healer> Healers = new Dictionary<CharacterState, Healer>();

	// Units spawned during the game (taken from the pool) that no raid has rolled for yet. A unit is rolled for once,
	// when a raid first hires it; units of a loaded save are spawned while it loads and keep the flag of the save.
	// Bounded by the pooled units, which are reused.
	private static readonly HashSet<Labor> Fresh = new HashSet<Labor>();

	private static Material _material;

	private readonly List<Labor> _units = new List<Labor>();

	private readonly List<CharacterState> _gone = new List<CharacterState>();

	private readonly List<CharacterState> _found = new List<CharacterState>();

	// The game left: the units of the list went with its scene.
	private static bool _left;

	private float _nextScan;

	private float _nextUnitList;

	private static void Enable()
	{
		Chance = Plugin.Cfg.Bind("Healtron", "Chance", 0.15f, "Chance that a corruptron hired into a raid is a Healtron.");
		MaxPerRaid = Plugin.Cfg.Bind("Healtron", "MaxPerRaid", 2, "Most Healtrons in one raid.");
		FromWave = Plugin.Cfg.Bind("Healtron", "FromWave", 3, "Invasion: Healtrons come from this wave on (raids without a wave number, e.g. in conquest, always may have them).");
		PulseSeconds = Plugin.Cfg.Bind("Healtron", "PulseSeconds", 4f, "Seconds between two healing pulses of a Healtron.");
		PulseRadius = Plugin.Cfg.Bind("Healtron", "PulseRadius", 6f, "Radius of the healing pulse in voxels.");
		PulseHealPercent = Plugin.Cfg.Bind("Healtron", "PulseHealPercent", 0.08f, "Share of an ally's maximum health that one pulse heals (0.08 = 8%).");
		BeamRange = Plugin.Cfg.Bind("Healtron", "BeamRange", 10f, "Range of the healing beam in voxels.");
		BeamHealPerSecond = Plugin.Cfg.Bind("Healtron", "BeamHealPerSecond", 0.05f, "Share of the target's maximum health the beam heals per second (0.05 = 5%).");
		DamageMultiplier = Plugin.Cfg.Bind("Healtron", "DamageMultiplier", 0.4f, "Damage of a Healtron's own attacks, times a corruptron's.");
		_enabled = true;
		WorkerStats.VariantChanged += (CharacterState state) =>
		{
			Candidates.Add(state);
			_dirty = true;
		};
		GameSession.OnLeave(() =>
		{
			// The lines and the units went with the scene.
			Healers.Clear();
			Fresh.Clear();
			_left = true;
		});
		Plugin.Root.AddComponent<Healtron>();
	}

	public static bool IsHealtron(Labor labor)
	{
		if (!_enabled || labor == null)
		{
			return false;
		}
		WorkerStats stats = WorkerStats.Peek(labor.state);
		return stats != null && stats.Variant == VariantHealtron;
	}

	public static bool IsHealtron(GameObject go)
	{
		return _enabled && go != null && IsHealtron(go.GetComponent<Labor>());
	}

	// ---- server: who becomes a Healtron

	// Makes a corruptron a Healtron (also used by the DevTools).
	public static void Make(Labor labor)
	{
		if (!_enabled || !NetworkServer.active || labor == null || labor.state == null)
		{
			return;
		}
		Fresh.Remove(labor);
		WorkerStats.Modify(labor.state, (WorkerStats s) => s.Variant = VariantHealtron);
		WorkerStats.RaiseVariantChanged(labor.state);
	}

	// The unit is never rolled for (DevTools spawns plain corruptrons this way).
	public static void MarkRolled(Labor labor)
	{
		Fresh.Remove(labor);
	}

	internal static void OnRaidHire(RaidProject raid, Labor labor)
	{
		if (!NetworkServer.active || labor == null || !Fresh.Remove(labor))
		{
			return;
		}
		if (labor.Occupation == null || labor.Occupation.CurrentOccupation != Occupation.Type.Corruptron || IsHealtron(labor))
		{
			return;
		}
		// raid.waveNumber is set by the invasion script before it hires the wave; 0 when the mode has no waves.
		int wave = (raid.data != null) ? raid.data.WaveNumber : 0;
		if (wave > 0 && wave < FromWave.Value)
		{
			return;
		}
		if (CountIn(raid) >= MaxPerRaid.Value || Random.value >= Chance.Value)
		{
			return;
		}
		Make(labor);
		Plugin.Log.LogInfo("healtron: a corruptron of raid " + raid.name + ((wave > 0) ? (" (wave " + wave + ")") : string.Empty) + " is a Healtron");
	}

	private static int CountIn(RaidProject raid)
	{
		int count = 0;
		foreach (Labor unit in raid.Workforce)
		{
			if (IsHealtron(unit))
			{
				count++;
			}
		}
		return count;
	}

	// A unit taken from the pool (new or reused) starts as an ordinary unit, in its own colours. Taken during the
	// game (not while a save loads), it may be rolled for when a raid hires it.
	internal static void OnRequested(Labor labor)
	{
		if (labor == null)
		{
			return;
		}
		if (labor.state != null && Healers.TryGetValue(labor.state, out Healer healer))
		{
			Remove(labor.state, healer);
		}
		if (NetworkServer.active && Architecte.commence)
		{
			Fresh.Add(labor);
		}
		else
		{
			Fresh.Remove(labor);
		}
	}

	// ---- every peer

	private void Update()
	{
		if (_left)
		{
			_left = false;
			_units.Clear();
			_nextUnitList = 0f;
		}
		float now = Time.time;
		if (_dirty || now >= _nextScan)
		{
			_dirty = false;
			_nextScan = now + ScanSeconds;
			Scan();
		}
		if (Healers.Count == 0)
		{
			return;
		}
		if (now >= _nextUnitList)
		{
			_nextUnitList = now + UnitListSeconds;
			Live<Labor>.Active(_units);
		}
		bool server = NetworkServer.active;
		foreach (Healer healer in Healers.Values)
		{
			Tick(healer, now, server);
		}
	}

	// Healers that came (flag set, loaded or received) and went (died, pooled, flag lost).
	private void Scan()
	{
		if (Candidates.Count == 0 && Healers.Count == 0)
		{
			return;
		}
		// Candidates that became healers, and ones that are no Healtron (any more) or destroyed, leave the set.
		_found.Clear();
		foreach (CharacterState state in Candidates)
		{
			if (state == null)
			{
				_found.Add(state);
				continue;
			}
			Labor labor = state.GetComponent<Labor>();
			if (labor == null || !IsHealtron(labor))
			{
				_found.Add(state);
			}
			else if (labor.gameObject.activeInHierarchy && GiantBricktron.IsAlive(labor))
			{
				if (!Healers.ContainsKey(state))
				{
					Add(state, labor);
				}
				_found.Add(state);
			}
		}
		foreach (CharacterState state in _found)
		{
			Candidates.Remove(state);
		}
		_gone.Clear();
		foreach (KeyValuePair<CharacterState, Healer> entry in Healers)
		{
			Labor labor = entry.Value.Labor;
			if (entry.Key == null || labor == null || !labor.gameObject.activeInHierarchy || !GiantBricktron.IsAlive(labor) || !IsHealtron(labor))
			{
				_gone.Add(entry.Key);
			}
		}
		foreach (CharacterState state in _gone)
		{
			Labor labor = Healers[state].Labor;
			Remove(state, Healers[state]);
			// Still a Healtron (put away or dead, not yet reused): a healer again if it comes back.
			if (state != null && labor != null && IsHealtron(labor))
			{
				Candidates.Add(state);
			}
		}
	}

	private static void Add(CharacterState state, Labor labor)
	{
		float now = Time.time;
		Healer healer = new Healer
		{
			Labor = labor,
			NextPick = now,
			NextTick = now + TickSeconds,
			// Spread, so the healers of a raid do not all pulse at once.
			NextPulse = now + PulseSeconds.Value * Random.Range(0.5f, 1f)
		};
		Healers[state] = healer;
		GlowTint.Apply(labor.gameObject, GlowTint.Green);
	}

	private static void Remove(CharacterState state, Healer healer)
	{
		Healers.Remove(state);
		if (healer.Beam != null)
		{
			Destroy(healer.Beam.gameObject);
		}
		if (healer.Ring != null)
		{
			Destroy(healer.Ring.gameObject);
		}
		if (healer.Labor != null)
		{
			// The tint goes with its captured materials: a level up of a reused unit swaps the body's material.
			GlowTint.Apply(healer.Labor.gameObject, null);
			GlowTint glow = healer.Labor.GetComponent<GlowTint>();
			if (glow != null)
			{
				Destroy(glow);
			}
		}
	}

	private void Tick(Healer healer, float now, bool server)
	{
		Labor labor = healer.Labor;
		if (labor == null)
		{
			return;
		}
		bool active = CanAct(labor);
		if (now >= healer.NextPick)
		{
			healer.NextPick = now + PickSeconds;
			healer.Target = active ? MostWounded(labor) : null;
		}
		if (healer.Target != null && !IsWounded(healer.Target))
		{
			healer.Target = null;
		}
		if (now >= healer.NextTick)
		{
			healer.NextTick = now + TickSeconds;
			if (server && active && healer.Target != null)
			{
				DamageReceiver receiver = healer.Target.GetComponent<DamageReceiver>();
				receiver.Heal(receiver.MaxHP * BeamHealPerSecond.Value * TickSeconds);
			}
		}
		if (now >= healer.NextPulse)
		{
			healer.NextPulse = now + Mathf.Max(0.5f, PulseSeconds.Value);
			if (active)
			{
				if (server)
				{
					Pulse(labor);
				}
				healer.PulseStart = now;
			}
		}
		DrawBeam(healer, active);
		DrawRing(healer, now, active);
	}

	// Dead, stunned, thrown or carried healers do nothing.
	private static bool CanAct(Labor labor)
	{
		if (!labor.gameObject.activeInHierarchy || !GiantBricktron.IsAlive(labor))
		{
			return false;
		}
		Locomotion4 locomotion = (labor.navigation != null) ? labor.navigation.Locomotion : null;
		if (locomotion == null)
		{
			return true;
		}
		return !locomotion.IsDead && !locomotion.IsGrabbed && !locomotion.IsAirborne && locomotion.state != Locomotion4.State.Stunned && locomotion.state != Locomotion4.State.Flailing;
	}

	private static bool IsAlly(Labor healer, Labor unit)
	{
		if (unit == healer)
		{
			return true;
		}
		Faction faction = healer.faction;
		return faction != null && (unit.faction == faction || Affiliation.IsAllied(healer.gameObject, unit.gameObject));
	}

	private static bool IsWounded(Labor unit)
	{
		if (unit == null || !unit.gameObject.activeInHierarchy || !GiantBricktron.IsAlive(unit))
		{
			return false;
		}
		DamageReceiver receiver = unit.GetComponent<DamageReceiver>();
		return receiver != null && receiver.MaxHP > 0f && receiver.CurrentHP > 0f && receiver.CurrentHP < receiver.MaxHP;
	}

	// The ally within BeamRange (not itself) with the lowest share of its health, below full.
	private Labor MostWounded(Labor healer)
	{
		Vector3 position = healer.transform.position;
		float range = BeamRange.Value;
		Labor best = null;
		float bestShare = 1f;
		foreach (Labor unit in _units)
		{
			if (unit == null || unit == healer || (unit.transform.position - position).sqrMagnitude > range * range)
			{
				continue;
			}
			if (!IsWounded(unit) || !IsAlly(healer, unit))
			{
				continue;
			}
			DamageReceiver receiver = unit.GetComponent<DamageReceiver>();
			float share = receiver.CurrentHP / receiver.MaxHP;
			if (share < bestShare)
			{
				best = unit;
				bestShare = share;
			}
		}
		return best;
	}

	// Server: every living ally within PulseRadius, the healer too.
	private void Pulse(Labor healer)
	{
		Vector3 position = healer.transform.position;
		float radius = PulseRadius.Value;
		foreach (Labor unit in _units)
		{
			if (unit == null || (unit.transform.position - position).sqrMagnitude > radius * radius)
			{
				continue;
			}
			if (!IsWounded(unit) || !IsAlly(healer, unit))
			{
				continue;
			}
			DamageReceiver receiver = unit.GetComponent<DamageReceiver>();
			receiver.Heal(receiver.MaxHP * PulseHealPercent.Value);
		}
	}

	// ---- looks

	private static void DrawBeam(Healer healer, bool active)
	{
		bool show = active && healer.Target != null;
		if (!show)
		{
			if (healer.Beam != null)
			{
				healer.Beam.enabled = false;
			}
			return;
		}
		if (healer.Beam == null)
		{
			healer.Beam = NewLine("CastleStoryPlusHealtronBeam", BeamWidth);
			healer.Beam.positionCount = 2;
			healer.Beam.startColor = BeamColor;
			healer.Beam.endColor = BeamColor;
		}
		healer.Beam.SetPosition(0, healer.Labor.transform.position + Vector3.up * ChestHeight);
		healer.Beam.SetPosition(1, healer.Target.transform.position + Vector3.up * ChestHeight);
		healer.Beam.enabled = true;
	}

	// A green ring that grows from the healer to the pulse's radius and fades.
	private static void DrawRing(Healer healer, float now, bool active)
	{
		if (!active)
		{
			healer.PulseStart = -1f;
		}
		float t = (healer.PulseStart < 0f) ? 1f : (now - healer.PulseStart) / RingSeconds;
		if (t >= 1f || t < 0f)
		{
			if (healer.Ring != null)
			{
				healer.Ring.enabled = false;
			}
			return;
		}
		if (healer.Ring == null)
		{
			healer.Ring = NewLine("CastleStoryPlusHealtronPulse", RingWidth);
			healer.Ring.positionCount = RingSegments + 1;
		}
		Color color = GlowTint.Green;
		color.a = 1f - t;
		healer.Ring.startColor = color;
		healer.Ring.endColor = color;
		float radius = Mathf.Lerp(0.5f, PulseRadius.Value, t);
		Vector3 center = healer.Labor.transform.position + Vector3.up * 0.3f;
		for (int i = 0; i <= RingSegments; i++)
		{
			float angle = (i % RingSegments) * 2f * Mathf.PI / RingSegments;
			healer.Ring.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
		}
		healer.Ring.enabled = true;
	}

	// The lines live in the game scene and go with it.
	private static LineRenderer NewLine(string name, float width)
	{
		GameObject go = new GameObject(name);
		LineRenderer line = go.AddComponent<LineRenderer>();
		if (_material == null)
		{
			Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
			_material = new Material(shader);
		}
		line.sharedMaterial = _material;
		line.useWorldSpace = true;
		line.startWidth = width;
		line.endWidth = width;
		line.enabled = false;
		return line;
	}
}
