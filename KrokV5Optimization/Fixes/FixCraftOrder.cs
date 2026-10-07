using System.Reflection;
using CasualtiesTogetherUtils;
using HarmonyLib;
using Together;

namespace KrokV5Optimization;

/// <summary>
/// host Recipe_TryMake already spends the items and spawns the result, then TellTheServerThatICrafted asks the server to craft again.
/// skip that second request when Net.IsServer. clients still send it.
/// </summary>
internal static class FixCraftOrder
{
	// host skip on the client send, plus a log on the server craft result.
	internal static void Arm(Harmony harmony)
	{
		MethodInfo tell = AccessTools.Method(typeof(PlayerCamera_TryCraft_MultiplayerPatch), "TellTheServerThatICrafted");
		if (tell == null)
		{
			Plugin.Log.LogWarning("[KrokV5Opt] craft gate missing TellTheServerThatICrafted");
			return;
		}
		harmony.Patch(tell, prefix: new HarmonyMethod(typeof(FixCraftOrder), nameof(SkipOnHost)));
		MethodInfo server = AccessTools.Method(typeof(PlayerCamera_TryCraft_MultiplayerPatch), "Server_CraftItemForClient");
		if (server != null)
			harmony.Patch(server, postfix: new HarmonyMethod(typeof(FixCraftOrder), nameof(LogResult)));
	}

	// false drops TellTheServerThatICrafted. clients return true and still send.
	static bool SkipOnHost()
	{
		if (!Net.IsServer)
			return true;
		LogLocal();
		return false;
	}

	// server side of the same craft. __result is whether Server_CraftItemForClient accepted it.
	static void LogResult(NetBody pb, Recipe recipe, bool __result)
	{
		Write(recipe, pb != null && pb.plr != null ? pb.plr.playerName : "?", pb != null && pb.IsLocal, __result);
	}

	// host never hits LogResult, so record the local craft from the camera's selected recipe.
	private static void LogLocal()
	{
		Recipe recipe = null;
		PlayerCamera cam = PlayerCamera.main;
		if (cam != null && cam.selectedRecipe >= 0 && cam.selectedRecipe < Recipes.recipes.Count)
			recipe = Recipes.recipes[cam.selectedRecipe];
		Write(recipe, "host", true, true);
	}

	// one line per recipe+result, deduped for 2 seconds inside BugLog.
	private static void Write(Recipe recipe, string who, bool local, bool accepted)
	{
		string name = recipe != null ? recipe.fullName : "?";
		int index = recipe != null ? recipe.index : -1;
		BugLog.Write("craft:" + index + ":" + accepted, "craft recipe=" + index + " " + name + " by=" + who + " local=" + local + " accepted=" + accepted);
	}
}
