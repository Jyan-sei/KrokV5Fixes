using System;
using System.Reflection;
using HarmonyLib;

namespace KrokV5Optimization;

// counts ItemGetContainerInfo allocs the WriteContainerInfo cache did not skip.
internal static class EvidenceContainerInfo
{
	internal static long Hits;

	internal static string Evidence() => "containerInfo=" + Hits;

	internal static void Arm(Harmony harmony)
	{
		Type itemSync = AccessTools.TypeByName("Together.ItemSync");
		MethodInfo method = AccessTools.Method(itemSync, "ItemGetContainerInfo", new[] { typeof(Item) });
		if (method == null)
		{
			Plugin.Log.LogWarning("[KrokV5Opt] container probe missing ItemGetContainerInfo");
			return;
		}
		harmony.Patch(method, postfix: new HarmonyMethod(typeof(EvidenceContainerInfo), nameof(Postfix)));
	}

	static void Postfix() => Hits++;
}
