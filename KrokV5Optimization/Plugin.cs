using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace KrokV5Optimization;

[BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
[BepInDependency("CasualtiesMP", BepInDependency.DependencyFlags.HardDependency)]
public class Plugin : BaseUnityPlugin
{
	internal static ManualLogSource Log;
	internal static ConfigEntry<bool> CompressPooling;
	internal static ConfigEntry<bool> CacheImguiStyles;
	internal static ConfigEntry<bool> BugLogEnabled;
	internal static ConfigEntry<bool> InvTrace;
	internal static ConfigEntry<float> ReportSeconds;

	private Harmony _harmony;
	private float _reportAt;

	private void Awake()
	{
		Log = Logger;
		CompressPooling = Config.Bind("Memory", "CompressPooling", true, "Reuse one gzip/deflate codec instead of a new stream per packet.");
		CacheImguiStyles = Config.Bind("Memory", "CacheImguiStyles", true, "Skip idle skin rebuilds and reuse GUIContent, GUIStyle, and RectOffset.");
		BugLogEnabled = Config.Bind("Probe", "BugLog", true, "Log shower, pickup, craft, give, carry, revive, and sim-range lines.");
		InvTrace = Config.Bind("Probe", "InvTrace", false, "Per-item inventory trace. Off by default.");
		ReportSeconds = Config.Bind("Probe", "ReportSeconds", 10f, "How often to print the memory counters. 0 hides the line.");

		_harmony = new Harmony(PluginInfo.GUID);
		if (CompressPooling.Value)
		{
			Try(typeof(FixUtilCompress));
			Try(typeof(FixUtilDecompress));
			Try(typeof(FixUtilCompressDeflate));
			Try(typeof(FixUtilDecompressDeflate));
			Arm(FixCompressWriter.Arm);
			Try(typeof(FixChunkBlock));
			Try(typeof(FixChunkFluid));
		}
		if (CacheImguiStyles.Value)
		{
			Try(typeof(FixSkinSkip));
			Arm(FixGuiContent.Arm);
			Arm(FixGuiStyle.Arm);
			Arm(FixRectOffset.Arm);
			Log.LogInfo("[KrokV5Opt] imgui rewrite methods=" + ImguiScan.Patched);
		}

		Try(typeof(FixParticles));
		Try(typeof(FixChunkHashes));
		Arm(FixWorldBlocks.Arm);
		Try(typeof(FixLiquidWalk));
		Try(typeof(FixGuiLayout));
		Try(typeof(FixContainerInfo));
		Try(typeof(FixSyringeHold));
		Try(typeof(FixChoicePrompt));
		Try(typeof(FixSyncRegister));
		Try(typeof(FixHostPickup));
		Try(typeof(FixContainerBroke));
		Arm(FixLateBaseline.Arm);
		Arm(FixRejoinBaseline.Arm);
		Arm(FixInvSync.Arm);
		Arm(EvidenceInv.Arm);
		Arm(EvidenceContainerInfo.Arm);
		Arm(EvidenceNestedLiquid.Arm);
		Arm(EvidencePickup.Arm);
		Arm(EvidenceBag.Arm);
		Arm(EvidenceLiquidDictionary.Arm);
		Arm(EvidenceSyncInfo.Arm);
		Arm(EvidenceSyncReset.Arm);
		Arm(EvidenceCarry.Arm);
		Arm(EvidenceParticles.Arm);
		Arm(EvidenceStrings.Arm);
		Try(typeof(FixScrapDesc));
		Try(typeof(FixItemTooltip));
		Try(typeof(FixInvDrag));
		Try(typeof(FixSimRange));
		Arm(EvidenceFloatArrays.Arm);
		Arm(FixCraftOrder.Arm);
		Try(typeof(EvidenceShower));
		Try(typeof(EvidenceGive));
		Try(typeof(EvidenceRevive));
		Try(typeof(EvidenceSimRange));

		_reportAt = Time.realtimeSinceStartup + Mathf.Max(1f, ReportSeconds.Value);
		Log.LogInfo($"[KrokV5Opt] v{PluginInfo.Version} compress={CompressPooling.Value} imgui={CacheImguiStyles.Value}");
	}

	private void LateUpdate()
	{
		FixMenuFps.Tick();
	}

	private void Update()
	{
		FixInvSync.Tick();
		EvidenceInv.Tick();
		if (ReportSeconds.Value <= 0f)
			return;
		if (Time.realtimeSinceStartup < _reportAt)
			return;
		_reportAt = Time.realtimeSinceStartup + ReportSeconds.Value;
		Log.LogInfo("[KrokV5Opt] " + Report.Line());
	}

	private void OnDestroy()
	{
		_harmony?.UnpatchSelf();
	}

	private void Try(Type patch)
	{
		try
		{
			_harmony.PatchAll(patch);
		}
		catch (Exception ex)
		{
			Log.LogWarning("[KrokV5Opt] patch skipped " + patch.Name + ": " + ex.Message);
		}
	}

	private void Arm(Action<Harmony> arm)
	{
		try
		{
			arm(_harmony);
		}
		catch (Exception ex)
		{
			Log.LogWarning("[KrokV5Opt] patch skipped: " + ex.Message);
		}
	}
}
