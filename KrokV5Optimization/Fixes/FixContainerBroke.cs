using System.Collections.Generic;
using HarmonyLib;
using Together;
using UnityEngine;

namespace KrokV5Optimization;

/// <summary>
/// a broken container spilled on every machine. only the host copy is the real item.
/// on a client, destroy unregistered children before the spill. synced children still spill and the slot update puts them back.
/// </summary>
[HarmonyPatch(typeof(Container), "ContainerBroke")]
internal static class FixContainerBroke
{
	// client only. host spill is the real item. unregistered children are local ghosts.
	static void Prefix(Container __instance)
	{
		if (__instance == null || !Net.IsRunning || Net.IsServer)
			return;
		var local = new List<GameObject>();
		foreach (Transform child in __instance.transform)
		{
			if (child == null || child.GetComponent<Item>() == null)
				continue;
			// synced children stay. the later slot update parents those.
			if (NetObjectRegistry.IsRegistered(child.gameObject))
				continue;
			local.Add(child.gameObject);
		}
		for (int i = 0; i < local.Count; i++)
		{
			local[i].transform.SetParent(null);
			Object.Destroy(local[i]);
		}
	}
}
