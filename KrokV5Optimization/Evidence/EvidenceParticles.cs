using System;
using System.Reflection;
using HarmonyLib;

namespace KrokV5Optimization;

// FixParticles owns RenderFluids. counts live particle systems and RenderFluids calls.
internal static class EvidenceParticles
{
	internal static long FluidRender;

	internal static string Evidence() => "particles=" + Live() + " fluidRender=" + FluidRender;

	internal static void Arm(Harmony harmony)
	{
		MethodInfo method = AccessTools.Method(typeof(FluidManager), "RenderFluids");
		if (method == null)
		{
			Plugin.Log.LogWarning("[KrokV5Opt] particle probe missing RenderFluids");
			return;
		}
		harmony.Patch(method, prefix: new HarmonyMethod(typeof(EvidenceParticles), nameof(Prefix)));
	}

	static void Prefix() => FluidRender++;

	private static int Live()
	{
		try
		{
			Type particle = AccessTools.TypeByName("UnityEngine.ParticleSystem");
			if (particle == null)
				return 0;
			MethodInfo find = AccessTools.Method(typeof(UnityEngine.Object), "FindObjectsOfType", new Type[0]);
			if (find == null || !find.IsGenericMethod)
				return 0;
			Array systems = find.MakeGenericMethod(particle).Invoke(null, null) as Array;
			return systems == null ? 0 : systems.Length;
		}
		catch
		{
			return 0;
		}
	}
}
