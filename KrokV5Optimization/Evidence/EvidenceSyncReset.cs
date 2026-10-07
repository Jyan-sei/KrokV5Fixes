using System.Reflection;
using HarmonyLib;
using Together;

namespace KrokV5Optimization;

// logs syncreset before and after the registry size, including the keep-inventory path.
internal static class EvidenceSyncReset
{
	internal static void Arm(Harmony harmony)
	{
		Patch(harmony, AccessTools.Method(typeof(NetObjectRegistry), nameof(NetObjectRegistry.UnregisterAllObjects)), nameof(BeforeClear), nameof(AfterClear));
		Patch(harmony, AccessTools.Method(typeof(NetObjectRegistry), nameof(NetObjectRegistry.UnregisterAllObjectsButKeepInventory)), nameof(BeforeKeep), nameof(AfterKeep));
	}

	private static void Patch(Harmony harmony, MethodInfo method, string before, string after)
	{
		if (method == null)
		{
			Plugin.Log.LogWarning("[KrokV5Opt] syncreset probe missing a method");
			return;
		}
		harmony.Patch(method, prefix: new HarmonyMethod(typeof(EvidenceSyncReset), before), postfix: new HarmonyMethod(typeof(EvidenceSyncReset), after));
	}

	static void BeforeClear() => Note("syncreset all before");
	static void AfterClear() => Note("syncreset all after");
	static void BeforeKeep() => Note("syncreset keep-inventory before");
	static void AfterKeep() => Note("syncreset keep-inventory after");

	private static void Note(string label)
	{
		int n = 0;
		try
		{
			if (NetObjectRegistry.SyncRegistry != null)
				n = NetObjectRegistry.SyncRegistry.Count;
		}
		catch
		{
		}
		Plugin.Log.LogInfo("[KrokV5Opt] " + label + " syncLive=" + n);
	}
}
