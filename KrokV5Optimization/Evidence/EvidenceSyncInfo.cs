using System;
using System.Reflection;
using HarmonyLib;
using Together;

namespace KrokV5Optimization;

// SyncInfo lifetime. syncCtor climbing while syncLive stays flat means they are replaced, not freed.
internal static class EvidenceSyncInfo
{
	internal static long Ctor;

	internal static string Evidence()
	{
		int live = 0;
		try
		{
			if (NetObjectRegistry.SyncRegistry != null)
				live = NetObjectRegistry.SyncRegistry.Count;
		}
		catch
		{
		}
		return "syncCtor=" + Ctor + " syncLive=" + live;
	}

	internal static void Arm(Harmony harmony)
	{
		Type syncInfo = AccessTools.TypeByName("Together.SyncInfo");
		if (syncInfo == null)
			return;
		foreach (ConstructorInfo ctor in syncInfo.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
		{
			try
			{
				harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(EvidenceSyncInfo), nameof(OnCtor)));
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[KrokV5Opt] syncinfo ctor probe skipped: " + ex.Message);
			}
		}
	}

	static void OnCtor() => Ctor++;
}
