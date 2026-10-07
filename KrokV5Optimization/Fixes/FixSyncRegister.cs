using HarmonyLib;
using Together;
using UnityEngine;

namespace KrokV5Optimization;

/// <summary>
/// TryGetSyncInfoOrRegister created the SyncInfo and then returned false, so the container id was stored as 0.
/// return the SyncInfo it just created.
/// </summary>
[HarmonyPatch(typeof(NetObjectRegistry))]
internal static class FixSyncRegister
{
	// GameObject overload. the live build's signature sometimes misses this patch; the Component one still runs.
	[HarmonyPatch(nameof(NetObjectRegistry.TryGetSyncInfoOrRegister), typeof(GameObject))]
	[HarmonyPostfix]
	static void AfterGameObject(GameObject obj, ref SyncInfo si, ref bool __result)
	{
		Recover(obj, ref si, ref __result);
	}

	// Component overload. most item lookups come through here.
	[HarmonyPatch(nameof(NetObjectRegistry.TryGetSyncInfoOrRegister), typeof(Component))]
	[HarmonyPostfix]
	static void AfterComponent(Component obj, ref SyncInfo si, ref bool __result)
	{
		if (obj == null)
			return;
		Recover(obj.gameObject, ref si, ref __result);
	}

	// only when stock returned false. if the registry already has this object, that create succeeded and the false was a lie.
	private static void Recover(GameObject obj, ref SyncInfo si, ref bool result)
	{
		if (result || obj == null || !Net.IsServer)
			return;
		if (!NetObjectRegistry.SyncRegistry.TryGetValue(obj, out SyncInfo created) || created == null)
			return;
		si = created;
		result = true;
	}
}
