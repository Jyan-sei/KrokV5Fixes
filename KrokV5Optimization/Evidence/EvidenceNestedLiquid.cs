using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Together;
using UnityEngine;

namespace KrokV5Optimization;

// logs a liquid container parented under another item.
// only when liquid, ml, or parent changes, and only if Probe.InvTrace is on.
internal static class EvidenceNestedLiquid
{
	private struct Seen
	{
		internal int Ml;
		internal int Parent;
		internal string Liquid;
	}

	internal static long Hits;

	private static readonly Dictionary<int, Seen> Last = new Dictionary<int, Seen>();

	internal static string Evidence() => "liquidNested=" + Hits;

	internal static void Arm(Harmony harmony)
	{
		Type itemSync = AccessTools.TypeByName("Together.ItemSync");
		MethodInfo method = AccessTools.Method(itemSync, "ItemGetContainerInfo", new[] { typeof(Item) });
		if (method == null)
		{
			Plugin.Log.LogWarning("[KrokV5Opt] nested liquid probe missing ItemGetContainerInfo");
			return;
		}
		harmony.Patch(method, postfix: new HarmonyMethod(typeof(EvidenceNestedLiquid), nameof(Postfix)));
	}

	static void Postfix(Item item, object __result)
	{
		if (item == null || __result == null || !Flag(__result, "has_container"))
			return;
		WaterContainerItem water = item.GetComponent<WaterContainerItem>();
		if (water == null || water.stack == null || water.stack.Count == 0)
			return;
		Transform parent = item.transform.parent;
		if (parent == null || parent.GetComponent<Item>() == null)
			return;
		float ml = 0f;
		string liquid = "";
		for (int i = 0; i < water.stack.Count; i++)
		{
			ml += water.stack[i].amount;
			if (liquid.Length == 0)
				liquid = water.stack[i].liquidId;
		}
		Hits++;
		int sync = ItemSyncId.Of(item);
		int mlWhole = (int)ml;
		int parentId = parent.GetInstanceID();
		if (Last.TryGetValue(sync, out Seen seen) && seen.Ml == mlWhole && seen.Parent == parentId && seen.Liquid == liquid)
			return;
		if (Last.Count > 4000)
			Last.Clear();
		Last[sync] = new Seen { Ml = mlWhole, Parent = parentId, Liquid = liquid };
		if (Plugin.InvTrace != null && Plugin.InvTrace.Value)
			BugLog.Write("liquid:" + sync, "nested liquid item=" + item.id + " sync=" + sync + " liquid=" + liquid + " ml=" + mlWhole + " wearing=" + Flag(__result, "is_wearing"));
	}

	private static bool Flag(object box, string field)
	{
		FieldInfo info = box.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		return info != null && info.GetValue(box) is true;
	}
}
