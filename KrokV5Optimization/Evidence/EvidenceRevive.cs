using HarmonyLib;
using Together;
using UnityEngine;

namespace KrokV5Optimization;

// logs the respawn position against the body afterwards.
[HarmonyPatch(typeof(ScavPlayer), nameof(ScavPlayer.Server_RespawnCharacter))]
internal static class EvidenceRevive
{
	static void Postfix(ScavPlayer __instance, Vector2 pos)
	{
		Vector2 actual = Vector2.zero;
		if (__instance != null && __instance.body != null)
			actual = __instance.body.transform.position;
		string who = __instance != null ? __instance.playerName : "?";
		float gap = Vector2.Distance(pos, actual);
		BugLog.Write("revive:" + who, "revive who=" + who + " asked=" + pos.x.ToString("0.0") + "," + pos.y.ToString("0.0") + " body=" + actual.x.ToString("0.0") + "," + actual.y.ToString("0.0") + " gap=" + gap.ToString("0.0"));
	}
}
