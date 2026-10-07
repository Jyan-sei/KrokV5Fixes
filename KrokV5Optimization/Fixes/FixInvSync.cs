using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LiteEntitySystem;
using Together;
using UnityEngine;

namespace KrokV5Optimization;

/// <summary>
/// a full snapshot repeats container_data1. a stored 0 empties the bag on other machines while the item is still inside on the host.
/// on FinishedWorldgen, reread the live parent and store that byte again. a slot or bag with a null syncBody keeps the last real byte. a real drop still publishes 0.
/// mark the field by repeating the real parent. do not write 0 to force the mark. the client was applying that 0 and dropping gear that had just landed.
/// on a client, reapply until the bag or body exists and the item is in it. skip SafeUnloadItem when it names no container.
/// do not let a bag byte pull an item out of a slot before LoadItem accepts it. if the id lookup misses, walk the registry.
/// </summary>
internal static class FixInvSync
{
	private struct Wait
	{
		internal SyncInfo Info;
		internal float Until;
	}

	internal static long Hits;
	internal static long Retries;
	static int Warns;

	private static readonly List<SyncInfo> Batch = new List<SyncInfo>(256);
	private static readonly HashSet<SyncInfo> Seen = new HashSet<SyncInfo>();
	private static readonly Dictionary<int, Wait> Pending = new Dictionary<int, Wait>();
	private static readonly List<int> Done = new List<int>();

	private static MethodInfo _getInfo;
	private static MethodInfo _touch;
	private static PropertyInfo _manager;
	private static bool _touchDead;
	private static MethodInfo _apply;
	private static MethodInfo _force;
	private static FieldInfo _hasContainer;
	private static FieldInfo _extra;
	private static FieldInfo _knet;
	private static FieldInfo _id;
	private static bool _ready;
	private static bool _dead;
	private static bool _ticking;

	// this file's counters on the 10s report.
	internal static string Evidence() => "invSync=" + Hits + " retry=" + Retries;

	// client: skip bag applies that would unslot an item, and skip empty unloads.
	// host republish is Refresh, called from FixRejoinBaseline when worldgen finishes.
	internal static void Arm(Harmony harmony)
	{
		_apply = AccessTools.Method(typeof(SyncInfo), "UpdateItemContainer");
		if (_apply == null)
			throw new MissingMethodException("inv sync apply");
		harmony.Patch(_apply,
			prefix: new HarmonyMethod(typeof(FixInvSync), nameof(KeepHeld)),
			postfix: new HarmonyMethod(typeof(FixInvSync), nameof(AfterApply)));
		Type sync = AccessTools.TypeByName("Together.ItemSync");
		Type info = sync != null ? sync.GetNestedType("ItemsContainerInfo", BindingFlags.Public | BindingFlags.NonPublic) : null;
		MethodInfo unload = AccessTools.Method(sync, "SafeUnloadItem", new[] { info });
		_force = AccessTools.Method(sync, "Container_ForceLoadItem", new[] { typeof(Item), typeof(Container) });
		if (unload == null)
			throw new MissingMethodException("inv sync unload");
		harmony.Patch(unload, prefix: new HarmonyMethod(typeof(FixInvSync), nameof(SkipLoose)));
		Plugin.Log.LogInfo("[KrokV5Opt] inv sync hooked");
	}

	// host only. walk the registry a few times because parenting one item can register another mid-loop.
	internal static void Refresh()
	{
		if (!Net.IsServer || !Ready() || NetObjectRegistry.SyncRegistry == null)
			return;
		int wrote = 0;
		int skipped = 0;
		Seen.Clear();
		// Seen stops a pass from publishing the same SyncInfo twice. a later pass picks up ones created during the write.
		for (int pass = 0; pass < 4; pass++)
		{
			Batch.Clear();
			foreach (SyncInfo info in NetObjectRegistry.SyncRegistry.Values)
			{
				if (info != null)
					Batch.Add(info);
			}
			int fresh = 0;
			for (int i = 0; i < Batch.Count; i++)
			{
				SyncInfo info = Batch[i];
				if (!Seen.Add(info))
					continue;
				fresh++;
				int result = Publish(info);
				if (result > 0)
					wrote++;
				else if (result < 0)
					skipped++;
			}
		if (fresh == 0)
			break;
		}
		Seen.Clear();
		Batch.Clear();
		Hits += wrote;
		Plugin.Log?.LogInfo("[KrokV5Opt] inv sync items=" + wrote + " skip=" + skipped);
		EvidenceInv.DumpHost();
	}

	// client only. items whose bag or body was not spawned yet get another UpdateItemContainer for up to 30s.
	internal static void Tick()
	{
		if (Pending.Count == 0 || Net.IsServer || _apply == null)
			return;
		float now = Time.realtimeSinceStartup;
		Done.Clear();
		// _ticking lets KeepHeld run stock during this reapply. without it, data==1 would skip the apply we just asked for.
		_ticking = true;
		try
		{
			foreach (KeyValuePair<int, Wait> pair in Pending)
			{
				SyncInfo info = pair.Value.Info;
				if (info == null || info.item == null || now > pair.Value.Until || Placed(info))
				{
					if (info != null && info.item != null)
						EvidenceInv.Retry(info, now > pair.Value.Until ? "expired" : "placed");
					Done.Add(pair.Key);
					continue;
				}
				if (TargetMissing(info))
				{
					EvidenceInv.Retry(info, "wait-" + Gap(info));
					continue;
				}
				try
				{
					if (!Parent(info))
					{
						EvidenceInv.Retry(info, "miss");
						continue;
					}
					Retries++;
					EvidenceInv.Retry(info, "ok");
					Done.Add(pair.Key);
				}
				catch (Exception ex)
				{
					if (Warns < 3)
					{
						Warns++;
						Plugin.Log?.LogWarning("[KrokV5Opt] inv retry failed: " + ex.Message);
					}
				}
			}
		}
		finally
		{
			_ticking = false;
		}
		for (int i = 0; i < Done.Count; i++)
			Pending.Remove(Done[i]);
	}

	// prefix on UpdateItemContainer. false skips it.
	// data 1 is "in a bag". stock unloads the slot before the bag load, and a refused load leaves the item on the ground.
	// server and the retry tick still run stock.
	static bool KeepHeld(SyncInfo __instance)
	{
		if (_ticking || Net.IsServer || __instance == null || __instance.go == null)
			return true;
		Item item;
		try
		{
			item = __instance.item;
		}
		catch (Exception)
		{
			return true;
		}
		// anything except a bag byte runs stock. a bag byte waits for Parent/ForceLoad.
		return item == null || __instance.container_data1.Value != 1;
	}

	// after stock UpdateItemContainer. if the item is not where the byte says, queue a retry.
	static void AfterApply(SyncInfo __instance)
	{
		if (_ticking || Net.IsServer || __instance == null || __instance.item == null || _apply == null)
			return;
		int id = __instance.item.GetInstanceID();
		if (__instance.container_data1.Value == 0 || Placed(__instance))
		{
			if (Pending.Remove(id) && __instance.container_data1.Value == 0)
				EvidenceInv.Retry(__instance, "clear");
			return;
		}
		if (Pending.ContainsKey(id))
			return;
		if (Pending.Count > 4000)
			Pending.Clear();
		Pending[id] = new Wait { Info = __instance, Until = Time.realtimeSinceStartup + 30f };
		EvidenceInv.Retry(__instance, "queue");
	}

	// prefix on SafeUnloadItem. false skips it when the info names no container. that call returns before it moves anything, but it still allocates.
	static bool SkipLoose(object ici)
	{
		if (ici == null || HasContainer(ici))
			return true;
		return false;
	}

	// host. read the live parent and store it. 1 = wrote and marked dirty, 0 = nothing to say, -1 = leave the stored byte alone.
	static int Publish(SyncInfo info)
	{
		Item item = info.item;
		if (item == null)
			return 0;
		byte storedData = info.container_data1.Value;
		ushort storedNet = info.container_netId.Value;
		if (FixContainerInfo.ParentLinkMissing(item))
		{
			EvidenceInv.Host(info, 0, 0, storedData, storedNet, "skip-link");
			return -1;
		}
		if (!ReadLive(item, out byte data, out ushort net))
		{
			EvidenceInv.Host(info, 0, 0, storedData, storedNet, "skip-read");
			return -1;
		}
		// parent exists but its sync id is still 0. storing that would tell clients the item is loose.
		if (net == 0 && item.transform.parent != null)
		{
			EvidenceInv.Host(info, data, net, storedData, storedNet, "skip-unreg");
			return -1;
		}
		bool changed = storedData != data || storedNet != net;
		info.container_data1.Value = data;
		info.container_netId.Value = net;
		FixContainerInfo.Remember(item, net, data);
		// already 0 and still loose. do not Touch, or every loose item dirties a field on each refresh.
		if (data == 0 && !changed)
		{
			EvidenceInv.Host(info, data, net, storedData, storedNet, "same");
			return 0;
		}
		EvidenceInv.Host(info, data, net, storedData, storedNet, "write");
		Touch(info, "container_data1");
		Touch(info, "container_netId");
		return 1;
	}

	// EntityFieldChanged(value, value) marks the field dirty without writing a fake 0 first.
	// a real failure disables Touch for the rest of the session. the stored bytes are already correct for the next full snapshot.
	static void Touch(SyncInfo info, string fieldName)
	{
		if (_touchDead)
			return;
		try
		{
			if (_touch == null)
			{
				_manager = AccessTools.Property(typeof(Net), "ServerEntityManager");
				foreach (MethodInfo method in typeof(ServerEntityManager).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
				{
					if (method.Name == "EntityFieldChanged" && method.IsGenericMethodDefinition)
					{
						_touch = method;
						break;
					}
				}
				if (_touch == null || _manager == null)
					throw new MissingMethodException("inv sync touch");
			}
			object manager = _manager.GetValue(null);
			object syncVar = AccessTools.Field(typeof(SyncInfo), fieldName).GetValue(info);
			if (manager == null || syncVar == null)
				return;
			object fieldId = AccessTools.Property(syncVar.GetType(), "FieldId").GetValue(syncVar);
			object value = AccessTools.Property(syncVar.GetType(), "Value").GetValue(syncVar);
			if (fieldId == null || value == null)
				return;
			object[] args = { info, fieldId, value, value, true };
			_touch.MakeGenericMethod(value.GetType()).Invoke(manager, args);
		}
		catch (Exception ex)
		{
			_touchDead = true;
			Plugin.Log.LogWarning("[KrokV5Opt] inv sync touch off: " + ex.Message);
		}
	}

	// client retry. data >= 100 is a body slot or worn limb. data 1 is a bag.
	static bool Parent(SyncInfo info)
	{
		byte data = info.container_data1.Value;
		ushort net = info.container_netId.Value;
		if (data == 0 || net == 0 || info.item == null)
			return false;
		knetid id = new knetid(net);
		// slot or worn gear. the body has to exist, then stock UpdateItemContainer can parent it.
		if (data >= 100)
		{
			if (!NetBody.TryGetNetBodyFromId(id, out _))
				return false;
			_apply.Invoke(info, null);
			return Placed(info);
		}
		SyncInfo bag = FindBag(net, out Container box);
		if (bag == null || bag.go == null)
			return false;
		// ForceLoad skips the "unload slot first" path that KeepHeld is avoiding.
		if (box != null && _force != null)
		{
			_force.Invoke(null, new object[] { info.item, box });
			return Placed(info);
		}
		info.item.transform.SetParent(bag.go.transform);
		return Placed(info);
	}

	// true when the transform parent matches the stored byte. data 0 counts as placed (it is loose on purpose).
	static bool Placed(SyncInfo info)
	{
		byte data = info.container_data1.Value;
		if (data == 0 || info.item == null)
			return data == 0;
		Transform parent = info.item.transform.parent;
		if (parent == null)
			return false;
		if (data >= 100)
			return parent.GetComponent<InventorySlot>() != null || parent.GetComponent<Limb>() != null;
		ushort net = info.container_netId.Value;
		if (net == 0)
			return false;
		if (ParentBagId(info.item) == net)
			return true;
		SyncInfo bag = FindBag(net, out _);
		if (bag == null || bag.go == null)
			return false;
		return parent == bag.go.transform || parent.IsChildOf(bag.go.transform);
	}

	// the bag or body named by the byte is not on this machine yet. keep waiting, do not parent to nothing.
	static bool TargetMissing(SyncInfo info)
	{
		byte data = info.container_data1.Value;
		if (data == 0)
			return false;
		ushort net = info.container_netId.Value;
		if (net == 0)
			return true;
		if (data >= 100)
			return !NetBody.TryGetNetBodyFromId(new knetid(net), out _);
		SyncInfo bag = FindBag(net, out _);
		return bag == null || bag.go == null;
	}

	// why the retry is still waiting. only used by the inv trace log.
	static string Gap(SyncInfo info)
	{
		ushort net = info.container_netId.Value;
		if (net == 0)
			return "nonet";
		bool listed = NetObjectRegistry.TryGetSyncInfo(new knetid(net), out SyncInfo direct) && direct != null;
		SyncInfo bag = FindBag(net, out Container box);
		if (!listed && bag == null)
			return "lookup";
		if (bag == null || bag.go == null)
			return "nogo";
		if (box == null)
			return "nocontainer";
		return listed ? "ready" : "scanned";
	}

	// id lookup first. a miss walks both registries, because the bag can be registered under the other map.
	static SyncInfo FindBag(ushort net, out Container box)
	{
		box = null;
		SyncInfo found = null;
		if (net != 0 && NetObjectRegistry.TryGetSyncInfo(new knetid(net), out SyncInfo direct) && direct != null)
			found = direct;
		if (found == null)
			found = ScanBag(net);
		if (found == null)
			return null;
		box = Box(found);
		return found;
	}

	// linear scan. TryGetSyncInfo misses some bags whose syncId is set but whose key is the unity object.
	static SyncInfo ScanBag(ushort net)
	{
		if (NetObjectRegistry.NetIdToSyncInfoDict != null)
		{
			foreach (SyncInfo info in NetObjectRegistry.NetIdToSyncInfoDict.Values)
			{
				if (SameBag(info, net))
					return info;
			}
		}
		if (NetObjectRegistry.SyncRegistry == null)
			return null;
		foreach (SyncInfo info in NetObjectRegistry.SyncRegistry.Values)
		{
			if (SameBag(info, net))
				return info;
		}
		return null;
	}

	// syncId throws if the entity has no id yet. that is a miss, not a failure.
	static bool SameBag(SyncInfo info, ushort net)
	{
		if (info == null)
			return false;
		try
		{
			return (ushort)info.syncId == net;
		}
		catch (Exception)
		{
			return false;
		}
	}

	// the Container is sometimes on the bag object, sometimes on a child.
	static Container Box(SyncInfo bag)
	{
		if (bag == null || bag.go == null)
			return null;
		Container own = bag.container;
		if (own != null)
			return own;
		Transform root = bag.go.transform;
		for (int i = 0; i < root.childCount; i++)
		{
			Container child = root.GetChild(i).GetComponent<Container>();
			if (child != null)
				return child;
		}
		return null;
	}

	// sync id of the bag this item is parented under right now. 0 if the parent is not a container.
	static ushort ParentBagId(Item item)
	{
		Transform parent = item.transform.parent;
		if (parent == null || parent.GetComponent<Container>() == null)
			return 0;
		if (!NetObjectRegistry.TryGetSyncInfo(parent.gameObject, out SyncInfo bag) || bag == null)
			return 0;
		try
		{
			return bag.syncId;
		}
		catch (Exception)
		{
			return 0;
		}
	}

	// ItemsContainerInfo.has_container. false means the unload names nothing.
	static bool HasContainer(object ici)
	{
		if (_hasContainer == null)
			_hasContainer = ici.GetType().GetField("has_container", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		return _hasContainer != null && _hasContainer.GetValue(ici) is bool on && on;
	}

	// ItemGetContainerInfo is the same read stock uses. containerExtraData1 is the parent kind, knetid is who.
	static bool ReadLive(Item item, out byte data, out ushort net)
	{
		data = 0;
		net = 0;
		object info = _getInfo.Invoke(null, new object[] { item });
		if (info == null)
			return false;
		object extra = _extra.GetValue(info);
		object knet = _knet.GetValue(info);
		if (!(extra is byte live) || knet == null)
			return false;
		object id = _id.GetValue(knet);
		if (!(id is ushort liveNet))
			return false;
		data = live;
		net = liveNet;
		return true;
	}

	// one-time reflection. a miss turns the host republish off. the client hooks from Arm stay.
	static bool Ready()
	{
		if (_ready)
			return true;
		if (_dead)
			return false;
		try
		{
			Type sync = AccessTools.TypeByName("Together.ItemSync");
			_getInfo = AccessTools.Method(sync, "ItemGetContainerInfo", new[] { typeof(Item) });
			_apply = AccessTools.Method(typeof(SyncInfo), "UpdateItemContainer");
			if (_getInfo == null || _apply == null)
				throw new MissingMethodException("inv sync read");
			_extra = AccessTools.Field(_getInfo.ReturnType, "containerExtraData1");
			_knet = AccessTools.Field(_getInfo.ReturnType, "knetid");
			_id = AccessTools.Field(typeof(knetid), "id");
			if (_extra == null || _knet == null || _id == null)
				throw new MissingFieldException("inv sync fields");
			_ready = true;
			return true;
		}
		catch (Exception ex)
		{
			_dead = true;
			Plugin.Log.LogWarning("[KrokV5Opt] inv sync off: " + ex.Message);
			return false;
		}
	}
}
