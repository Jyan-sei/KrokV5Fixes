using System.Collections.Generic;
using HarmonyLib;
using Together;
using UnityEngine;

namespace KrokV5Optimization;

// logs a spider whose tracker left sim range. once per drop.
[HarmonyPatch(typeof(SyncInfoGameObjectTracker), nameof(SyncInfoGameObjectTracker.UpdateDistanceChecks))]
internal static class EvidenceSimRange
{
	private static readonly HashSet<int> Out = new HashSet<int>();

	static void Postfix(SyncInfoGameObjectTracker __instance)
	{
		if (__instance == null)
			return;
		int id = __instance.GetInstanceID();
		if (__instance.isInSimulationRange)
		{
			Out.Remove(id);
			return;
		}
		if (!Out.Add(id))
			return;
		if (__instance.GetComponent<SpiderHandler>() == null && __instance.GetComponentInChildren<SpiderHandler>() == null)
			return;
		Vector2 pos = __instance.transform.position;
		BugLog.Write("sim:" + id, "spider left sim range pos=" + pos.x.ToString("0.0") + "," + pos.y.ToString("0.0") + " sync=" + __instance.syncId);
	}
}
