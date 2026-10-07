using System.Reflection;
using HarmonyLib;

namespace KrokV5Optimization;

// string probe. ScrapEaterScript.Update rebuilds build.description every frame.
// UIBullshit.OnGUI and Body.Update are the other steady sites.
internal static class EvidenceStrings
{
	internal static long ScrapDesc;
	internal static long BodyUpdate;

	internal static string Evidence() => "scrapDesc=" + ScrapDesc + " bodyUpdate=" + BodyUpdate;

	internal static void Arm(Harmony harmony)
	{
		Patch(harmony, AccessTools.Method(typeof(ScrapEaterScript), "Update"), nameof(OnScrap));
		Patch(harmony, AccessTools.Method(typeof(Body), "Update"), nameof(OnBody));
	}

	private static void Patch(Harmony harmony, MethodInfo method, string postfix)
	{
		if (method == null)
		{
			Plugin.Log.LogWarning("[KrokV5Opt] string probe missing " + postfix);
			return;
		}
		harmony.Patch(method, postfix: new HarmonyMethod(typeof(EvidenceStrings), postfix));
	}

	static void OnScrap() => ScrapDesc++;

	static void OnBody() => BodyUpdate++;
}
