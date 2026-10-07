using System.Reflection;
using HarmonyLib;
using Together;
using UnityEngine;

namespace KrokV5Optimization;

// carry probe. logs RequestCarryPerson and the follow teleport.
internal static class EvidenceCarry
{
	private static MethodInfo _lookup;

	internal static void Arm(Harmony harmony)
	{
		MethodInfo method = AccessTools.Method(typeof(ServerMain), "ServerReceiver__RequestCarryPerson");
		if (method == null)
			Plugin.Log.LogWarning("[KrokV5Opt] carry probe missing ServerReceiver__RequestCarryPerson");
		else
			harmony.Patch(method, postfix: new HarmonyMethod(typeof(EvidenceCarry), nameof(OnRequest)));

		_lookup = AccessTools.Method(typeof(ScavPlayer), "TryGetPlayerAndNetBodyFromClientId", new[]
		{
			typeof(knetid),
			typeof(ScavPlayer).MakeByRefType(),
			typeof(NetBody).MakeByRefType()
		});
		if (_lookup == null)
			Plugin.Log.LogWarning("[KrokV5Opt] carry probe missing TryGetPlayerAndNetBodyFromClientId");

		MethodInfo follow = AccessTools.Method(typeof(SyncBody), "ApplyBodyTeleportPacket", new[] { typeof(NetBody), typeof(BodyTeleportPacket) });
		if (follow == null)
			Plugin.Log.LogWarning("[KrokV5Opt] carry follow probe missing ApplyBodyTeleportPacket");
		else
			harmony.Patch(follow, postfix: new HarmonyMethod(typeof(EvidenceCarry), nameof(OnTeleport)));
	}

	static void OnRequest(knetid clientId)
	{
		MethodInfo lookup = _lookup;
		if (lookup == null)
			return;
		object[] args = lookup.GetParameters().Length >= 3
			? new object[] { clientId, null, null }
			: new object[] { clientId, null };
		if (!(lookup.Invoke(null, args) is true))
			return;
		ScavPlayer plr = args[1] as ScavPlayer;
		if (plr == null)
			return;
		NetBody pb = args.Length > 2 ? args[2] as NetBody : null;
		if (pb == null)
			pb = plr.GetType().GetField("playerbody")?.GetValue(plr) as NetBody;
		if (pb == null)
			return;
		NetBody carried = pb.carrying_person;
		string carriedName = carried != null ? carried.bodyName : "none";
		int carriedId = carried != null ? carried.netId : 0;
		bool carriedLocal = carried != null && carried.IsLocal;
		Vector2 carrierPos = pb.transform.position;
		Vector2 carriedPos = carried != null ? (Vector2)carried.transform.position : Vector2.zero;
		BugLog.Write("carry:" + plr.playerName, "carry by=" + plr.playerName + " carried=" + carriedName + " net=" + carriedId + " carriedLocal=" + carriedLocal + " carrierPos=" + carrierPos.x.ToString("0.0") + "," + carrierPos.y.ToString("0.0") + " carriedPos=" + carriedPos.x.ToString("0.0") + "," + carriedPos.y.ToString("0.0"));
	}

	static void OnTeleport(NetBody nb)
	{
		if (nb == null || !nb.IsPiggybacking())
			return;
		int piggy = 0;
		if (nb.piggybacking_on != null)
			piggy = nb.piggybacking_on.netId;
		Vector2 pos = nb.transform.position;
		BugLog.Write("follow:" + nb.netId, "carry packet body=" + nb.bodyName + " net=" + nb.netId + " local=" + nb.IsLocal + " piggyId=" + piggy + " pos=" + pos.x.ToString("0.0") + "," + pos.y.ToString("0.0"));
	}
}
