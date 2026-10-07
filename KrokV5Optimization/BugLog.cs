using System.Collections.Generic;
using UnityEngine;

namespace KrokV5Optimization;

internal static class BugLog
{
	private static readonly Dictionary<string, float> Next = new Dictionary<string, float>();

	internal static void Write(string key, string line)
	{
		if (Plugin.BugLogEnabled != null && !Plugin.BugLogEnabled.Value)
			return;
		float now = Time.realtimeSinceStartup;
		if (Next.TryGetValue(key, out float at) && now < at)
			return;
		Next[key] = now + 2f;
		Plugin.Log.LogInfo("[KrokV5Opt] " + line);
	}
}
