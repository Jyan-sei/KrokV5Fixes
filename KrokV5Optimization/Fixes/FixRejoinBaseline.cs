using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CasualtiesTogetherUtils;
using HarmonyLib;
using LiteEntitySystem;
using Together;
using UnityEngine;

namespace KrokV5Optimization;

/// <summary>
/// the accept-time full snapshot is before FinishedWorldgen, so saved gear is not on the body yet.
/// queue one more full send when they report worldgen finished. an object they already have is updated. a missing one is created.
/// </summary>
internal static class FixRejoinBaseline
{
	static readonly List<byte> Pending = new List<byte>();
	static FieldInfo StateField;
	static FieldInfo SentField;
	static FieldInfo RequestedField;
	static PropertyInfo ManagerProperty;
	static object RequestBaselineState;

	// FinishedWorldgen queues the player. the next OnLogicTick actually asks for the baseline, after the body exists.
	internal static void Arm(Harmony harmony)
	{
		StateField = AccessTools.Field(typeof(NetPlayer), "State");
		SentField = AccessTools.Field(typeof(NetPlayer), "FirstBaselineSent");
		RequestedField = AccessTools.Field(typeof(ServerEntityManager), "_firstBaselineRequestedPlayers");
		ManagerProperty = AccessTools.Property(typeof(Net), "ServerEntityManager");
		if (StateField == null || SentField == null || RequestedField == null || ManagerProperty == null)
			throw new MissingFieldException("rejoin baseline fields");
		RequestBaselineState = Enum.Parse(StateField.FieldType, "RequestBaseline");

		MethodInfo done = AccessTools.Method(typeof(WorldgenPatches), "ServerReceiver_FinishedWorldgen");
		MethodInfo tick = AccessTools.Method(typeof(ServerEntityManager), "OnLogicTick");
		if (done == null || tick == null)
			throw new MissingMethodException("rejoin baseline hook");

		harmony.Patch(done,
			prefix: new HarmonyMethod(typeof(FixRejoinBaseline), nameof(Mark)),
			postfix: new HarmonyMethod(typeof(FixRejoinBaseline), nameof(After)));
		harmony.Patch(tick, prefix: new HarmonyMethod(typeof(FixRejoinBaseline), nameof(BeforeTick)));
		Plugin.Log.LogInfo("[KrokV5Opt] rejoin baseline hooked");
	}

	// true only for the packet that flips finished_worldgen from false to true. a repeat does not queue another snapshot.
	static void Mark(knetid clientId, out bool __state)
	{
		__state = false;
		if (!Util.IsInWorld())
			return;
		if (!ScavPlayer.TryGetPlayerFromClientId(clientId, out ScavPlayer plr) || plr == null)
			return;
		if (plr.ServerPlayerState == null || plr.ServerPlayerState.finished_worldgen)
			return;
		__state = true;
	}

	// stock has set finished_worldgen. if the body is not spawned yet, wait. the gear is on the body.
	static void After(knetid clientId, bool __state)
	{
		if (!__state)
			return;
		if (!ScavPlayer.TryGetPlayerFromClientId(clientId, out ScavPlayer plr) || plr == null)
			return;
		if (plr.body == null)
		{
			((MonoBehaviour)plr).StartCoroutine(WaitForBody(plr));
			return;
		}
		Queue(plr);
	}

	// one extra frame after the body appears, so saved items have been loaded onto it.
	static IEnumerator WaitForBody(ScavPlayer plr)
	{
		float until = Time.realtimeSinceStartup + 30f;
		while (plr != null && plr.body == null && Time.realtimeSinceStartup < until)
			yield return null;
		if (plr == null || plr.body == null)
			yield break;
		yield return null;
		Queue(plr);
	}

	// player ids fit in a byte. 0 is not a player.
	static void Queue(ScavPlayer plr)
	{
		ushort id = plr.playerId;
		if (id == 0 || id > 255)
			return;
		byte key = (byte)id;
		if (!Pending.Contains(key))
			Pending.Add(key);
	}

	// republish container bytes first, then put the player back into the "wants a full baseline" state.
	static void BeforeTick()
	{
		if (Pending.Count == 0)
			return;
		FixInvSync.Refresh();
		ServerEntityManager manager = ManagerProperty.GetValue(null) as ServerEntityManager;
		if (manager == null)
			return;
		IList list = RequestedField.GetValue(manager) as IList;
		if (list == null)
			return;
		for (int i = 0; i < Pending.Count; i++)
		{
			NetPlayer player = manager.GetPlayer(Pending[i]);
			if (player == null)
				continue;
			if (!list.Contains(player))
				list.Add(player);
			// RequestBaseline + FirstBaselineSent false is what OnLogicTick treats as "send everything".
			StateField.SetValue(player, RequestBaselineState);
			SentField.SetValue(player, false);
			Plugin.Log?.LogInfo("[KrokV5Opt] rejoin baseline player=" + player.Id);
		}
		Pending.Clear();
	}
}
