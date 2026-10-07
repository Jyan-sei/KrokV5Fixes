using System;
using System.Reflection;
using HarmonyLib;

namespace KrokV5Optimization;

// counts the liquid ToDictionary in the sync packet write. that call also builds the Linq set and the byte/ushort enumerator.
internal static class EvidenceLiquidDictionary
{
	internal static long Hits;

	internal static string Evidence() => "packetWrite=" + Hits;

	internal static void Arm(Harmony harmony)
	{
		Type syncInfo = AccessTools.TypeByName("Together.SyncInfo");
		MethodInfo write = AccessTools.Method(syncInfo, "WriteObjectIntoPacket");
		if (write == null)
			write = Find(syncInfo);
		if (write == null)
		{
			Plugin.Log.LogWarning("[KrokV5Opt] liquid dictionary probe missing the ToDictionary method");
			return;
		}
		harmony.Patch(write, prefix: new HarmonyMethod(typeof(EvidenceLiquidDictionary), nameof(Prefix)));
	}

	static void Prefix() => Hits++;

	private static MethodInfo Find(Type syncInfo)
	{
		if (syncInfo == null)
			return null;
		foreach (MethodInfo method in syncInfo.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
		{
			MethodBody body = method.GetMethodBody();
			byte[] il = body?.GetILAsByteArray();
			if (il == null)
				continue;
			for (int i = 0; i < il.Length - 4; i++)
			{
				if (il[i] != 0x28 && il[i] != 0x6F)
					continue;
				int token = BitConverter.ToInt32(il, i + 1);
				try
				{
					MethodBase resolved = method.Module.ResolveMethod(token);
					if (resolved != null && resolved.Name == "ToDictionary")
						return method;
				}
				catch
				{
				}
			}
		}
		return null;
	}
}
