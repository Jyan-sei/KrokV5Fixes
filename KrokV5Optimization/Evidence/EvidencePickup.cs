using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Together;
using UnityEngine;

namespace KrokV5Optimization;

// logs ForcePickupItem and SafeUnloadItem. quiet unless Probe.InvTrace.
internal static class EvidencePickup
{
	internal static void Arm(Harmony harmony)
	{
		Type itemSync = AccessTools.TypeByName("Together.ItemSync");
		Patch(harmony, AccessTools.Method(itemSync, "ForcePickupItem"), nameof(Pickup));
		Patch(harmony, AccessTools.Method(itemSync, "SafeUnloadItem", new[] { typeof(Item) }), nameof(Unload));
	}

	private static void Patch(Harmony harmony, MethodInfo method, string postfix)
	{
		if (method == null)
		{
			Plugin.Log.LogWarning("[KrokV5Opt] pickup probe missing " + postfix);
			return;
		}
		harmony.Patch(method, postfix: new HarmonyMethod(typeof(EvidencePickup), postfix));
	}

	static void Pickup(NetBody body, Item item)
	{
		try
		{
			if (Plugin.InvTrace == null || !Plugin.InvTrace.Value || item == null)
				return;
			int sync = ItemSyncId.Of(item);
			string who = body != null && body.plr != null ? body.plr.playerName : "?";
			BugLog.Write("pickup:" + sync, "pickup item=" + item.id + " sync=" + sync + " by=" + who);
		}
		catch (Exception)
		{
		}
	}

	static string Caller()
	{
		StackFrame[] frames = new StackTrace(2, false).GetFrames();
		if (frames == null)
			return "?";
		for (int i = 0; i < frames.Length && i < 12; i++)
		{
			MethodBase method = frames[i].GetMethod();
			if (method == null)
				continue;
			string type = method.DeclaringType != null ? method.DeclaringType.Name : "";
			if (type.Length == 0 || type.StartsWith("Harmony") || type.StartsWith("Evidence"))
				continue;
			return type + "." + method.Name;
		}
		return "?";
	}

	static void Unload(Item item)
	{
		try
		{
			if (Plugin.InvTrace == null || !Plugin.InvTrace.Value || item == null)
				return;
			int sync = ItemSyncId.Of(item);
			BugLog.Write("unload:" + item.id + ":" + sync, "unload item=" + item.id + " sync=" + sync + " from=" + Caller());
		}
		catch (Exception)
		{
		}
	}
}
