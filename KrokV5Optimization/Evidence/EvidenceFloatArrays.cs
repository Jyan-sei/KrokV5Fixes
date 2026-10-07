using System.Reflection;
using HarmonyLib;
using Together;

namespace KrokV5Optimization;

// counts StreamedAudioOutput.Update while a clip is playing. that path still does new float[num], and StartPlayback does new float[sample_rate].
internal static class EvidenceFloatArrays
{
	internal static long Hits;

	private static readonly PropertyInfo Source = AccessTools.Property(typeof(StreamedAudioOutput), "audioSource");
	private static PropertyInfo _playing;

	internal static string Evidence() => "audioFloat=" + Hits;

	internal static void Arm(Harmony harmony)
	{
		MethodInfo method = AccessTools.Method(typeof(StreamedAudioOutput), "Update");
		if (method == null)
		{
			Plugin.Log.LogWarning("[KrokV5Opt] float[] probe missing StreamedAudioOutput.Update");
			return;
		}
		harmony.Patch(method, postfix: new HarmonyMethod(typeof(EvidenceFloatArrays), nameof(Postfix)));
	}

	static void Postfix(StreamedAudioOutput __instance)
	{
		try
		{
			object source = Source?.GetValue(__instance);
			if (source == null)
				return;
			if (_playing == null)
				_playing = source.GetType().GetProperty("isPlaying");
			if (_playing != null && _playing.GetValue(source) is bool on && on)
				Hits++;
		}
		catch
		{
		}
	}
}
