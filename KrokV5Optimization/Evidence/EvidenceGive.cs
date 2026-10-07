using HarmonyLib;
using Together;
using UnityEngine;

namespace KrokV5Optimization;

// logs give-item offers.
[HarmonyPatch(typeof(ClientMain), "_PLRINT_GiveItem")]
internal static class EvidenceGive
{
	static void Postfix(NetBody target, Item item)
	{
		if (item == null)
			return;
		int sync = ItemSyncId.Of(item);
		string hover = "";
		try
		{
			hover = PlayerCamera.ItemHoverDescription(item).Item1;
		}
		catch
		{
		}
		string who = target != null ? target.bodyName : "?";
		BugLog.Write("give:" + sync, "give item=" + item.id + " sync=" + sync + " hover=\"" + hover + "\" to=" + who);
	}
}
