using System.Reflection;
using HarmonyLib;
using Together;
using UnityEngine;

namespace KrokV5Optimization;

// logs shower activation, players inside 8m, and the nearest horizontal distance.
[HarmonyPatch(typeof(LifepodController), "ActivateShower")]
internal static class EvidenceShower
{
	static void Postfix(LifepodController __instance)
	{
		if (__instance == null)
			return;
		Vector2 pos = __instance.transform.position;
		int near = 0;
		float nearest = 999f;
		string names = "";
		try
		{
			foreach (NetBody body in ScavPlayer.GetPlayerBodiesInRadius(pos, 16f))
			{
				if (body == null)
					continue;
				float dx = Mathf.Abs(pos.x - body.transform.position.x);
				if (dx < nearest)
					nearest = dx;
				if (dx < 8f && pos.y > body.transform.position.y)
				{
					near++;
					if (body.plr != null)
						names = names.Length == 0 ? body.plr.playerName : names + "," + body.plr.playerName;
				}
			}
		}
		catch
		{
		}
		BugLog.Write("shower", "shower pos=" + pos.x.ToString("0.0") + "," + pos.y.ToString("0.0") + " playersInStream=" + near + " nearestDx=" + nearest.ToString("0.0") + " names=" + names);
	}
}
