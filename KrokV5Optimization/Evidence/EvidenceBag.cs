using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LiteNetLib.Utils;
using Together;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KrokV5Optimization;

// bag load/refuse probe. lines only when Probe.InvTrace is on.
internal static class EvidenceBag
{
	internal static long Loads;
	internal static long Refuses;

	private static readonly Dictionary<int, string> SyncSeen = new Dictionary<int, string>();
	private static FieldInfo _bagItem;
	private static FieldInfo _netId;
	private static MethodInfo _tryGetItem;
	private static knetid _reqItem;
	private static knetid _reqBag;
	private static bool _reqOk;

	internal static string Evidence() => "bagLoad=" + Loads + " bagRefuse=" + Refuses;

	internal static void Arm(Harmony harmony)
	{
		Type sync = AccessTools.TypeByName("Together.ItemSync");
		Type info = sync != null ? sync.GetNestedType("ItemsContainerInfo", BindingFlags.Public | BindingFlags.NonPublic) : null;
		Patch(harmony, AccessTools.Method(typeof(Container), "LoadItem"), nameof(LoadPrefix), nameof(LoadPostfix));
		Patch(harmony, AccessTools.Method(sync, "ServerReceiver__ItemChangeContainer"), nameof(ServerPrefix), nameof(ServerPostfix));
		Patch(harmony, AccessTools.Method(sync, "Client_AnnounceContainerChange"), nameof(Announce), null);
		Patch(harmony, AccessTools.Method(sync, "SafeUnloadItem", new[] { info }), null, nameof(Unload));
		Patch(harmony, AccessTools.Method(typeof(SyncInfo), "UpdateItemContainer"), null, nameof(Sync));
		MethodInfo click = AccessTools.Method(typeof(PlayerCamera), "TryPerformInventoryAction");
		if (click == null)
			Plugin.Log.LogWarning("[KrokV5Opt] bag probe missing click");
		else
			harmony.Patch(click, prefix: new HarmonyMethod(typeof(EvidenceBag), nameof(Click)) { priority = Priority.First });
	}

	private static void Patch(Harmony harmony, MethodInfo method, string prefix, string postfix)
	{
		if (method == null)
		{
			Plugin.Log.LogWarning("[KrokV5Opt] bag probe missing " + (prefix ?? postfix));
			return;
		}
		harmony.Patch(
			method,
			prefix: prefix == null ? null : new HarmonyMethod(typeof(EvidenceBag), prefix),
			postfix: postfix == null ? null : new HarmonyMethod(typeof(EvidenceBag), postfix));
	}

	static void LoadPrefix(Container __instance, Item item, out string __state)
	{
		__state = null;
		if (__instance == null || item == null)
			return;
		Loads++;
		string why = Why(__instance, item);
		if (why != "ok")
			Refuses++;
		__state = why;
		float dist = Vector2.Distance(item.transform.position, __instance.transform.position);
		Line("load side=" + Side()
			+ " why=" + why
			+ " item=" + Who(item)
			+ " into=" + Bag(__instance)
			+ " w=" + item.totalWeight.ToString("0.###")
			+ " hold=" + __instance.GetHoldingWeight().ToString("0.###")
			+ " max=" + __instance.maxWeight.ToString("0.###")
			+ " per=" + __instance.maxWeightPerItem.ToString("0.###")
			+ " tags=" + Tags(item)
			+ " restrict=" + Restrict(__instance)
			+ " dist=" + dist.ToString("0.0"));
	}

	static void LoadPostfix(Container __instance, Item item, string __state)
	{
		if (__instance == null || item == null)
			return;
		Transform parent = item.transform.parent;
		bool stuck = parent == __instance.transform;
		Line("load result side=" + Side()
			+ " why=" + __state
			+ " stuck=" + stuck
			+ " item=" + Who(item)
			+ " parent=" + (parent != null ? parent.name : "none"));
	}

	static void ServerPrefix(knetid clientId, NetDataReader reader)
	{
		_reqOk = false;
		if (reader == null)
			return;
		try
		{
			int pos = reader.Position;
			bool load = reader.GetBool();
			knetid itemId = default;
			knetid bagId = default;
			MyLiteNetLibExtensions.Get(reader, out itemId);
			MyLiteNetLibExtensions.Get(reader, out bagId);
			reader.SetPosition(pos);
			_reqItem = itemId;
			_reqBag = bagId;
			_reqOk = true;
			string item = Resolve(itemId, out _, out Item held)
				? Who(held)
				: "missing sync=" + NetOf(itemId);
			string bag = "missing sync=" + NetOf(bagId);
			if (Resolve(bagId, out SyncInfo bagSi, out Item bagItem))
				bag = Who(bagItem) + " isContainer=" + (bagSi != null && bagSi.container != null);
			Line("server side=" + Side()
				+ " from=" + NetOf(clientId)
				+ " load=" + load
				+ " item=" + item
				+ " into=" + bag);
		}
		catch (Exception ex)
		{
			Line("server peek failed " + ex.Message);
		}
	}

	static void ServerPostfix()
	{
		if (!_reqOk)
			return;
		if (!Resolve(_reqItem, out SyncInfo _, out Item item) || item == null)
		{
			Line("server after item sync=" + NetOf(_reqItem) + " gone");
			return;
		}
		Transform parent = item.transform.parent;
		Container bag = parent != null ? parent.GetComponent<Container>() : null;
		Line("server after item=" + Who(item)
			+ " parent=" + (parent != null ? parent.name : "none")
			+ " bag=" + (bag != null ? Bag(bag) : "-"));
	}

	static void Announce(SyncInfo si)
	{
		if (si == null || si.item == null)
		{
			Line("announce side=" + Side() + " empty");
			return;
		}
		object info = ContainerInfo(si.item);
		string why = "send";
		if (Flag(info, "is_in_locplr_surface_inv"))
			why = "skip-surface";
		else if (Flag(info, "is_wearing") && Field(info, "limb") is Limb limb && BodyLocal(limb))
			why = "skip-worn";
		string into = "-";
		if (Field(info, "container_si") is SyncInfo parent && parent.item != null)
			into = Who(parent.item);
		Line("announce side=" + Side()
			+ " why=" + why
			+ " item=" + Who(si.item)
			+ " has=" + Flag(info, "has_container")
			+ " into=" + into);
	}

	static void Unload(object ici)
	{
		Item item = Field(ici, "item") as Item;
		if (item == null || !Flag(ici, "has_container"))
			return;
		string from = "bag " + (Field(ici, "container") is Container bag ? Bag(bag) : "?");
		if (Flag(ici, "is_wearing"))
			from = "worn";
		else if (Field(ici, "inv_slot") != null)
			from = "slot";
		Line("unload side=" + Side() + " from=" + from + " item=" + Who(item));
	}

	static void Click(PlayerCamera __instance, RaycastResult hit, List<RaycastResult> uiCasts)
	{
		if (__instance == null || __instance.dragItem == null || !Net.IsRunning)
			return;
		string hitName = "?";
		bool button = false;
		GameObject go = hit.gameObject;
		if (go != null)
		{
			hitName = go.name;
			if (go.TryGetComponent(out InvButton inv) && uiCasts != null)
				button = inv.Overlaps(uiCasts);
		}
		Line("click side=" + Side()
			+ " drag=" + Who(__instance.dragItem)
			+ " hit=" + hitName
			+ " invButton=" + button);
	}

	static void Sync(SyncInfo __instance)
	{
		if (Plugin.InvTrace == null || !Plugin.InvTrace.Value)
			return;
		if (__instance == null || __instance.go == null)
			return;
		Item item;
		try
		{
			item = __instance.item;
		}
		catch (NullReferenceException)
		{
			return;
		}
		if (item == null)
			return;
		Transform parent = item.transform.parent;
		Container bag = parent != null ? parent.GetComponent<Container>() : null;
		byte data = __instance.container_data1.Value;
		ushort net = __instance.container_netId.Value;
		Item owner = bag != null ? BagItem(bag) : null;
		bool foliage = owner != null && owner.id != null
			&& owner.id.IndexOf("foliagebag", StringComparison.OrdinalIgnoreCase) >= 0;
		bool zeroWhileHeld = parent != null && data == 0;
		bool bagMismatch = bag != null && data != 1;
		if (!foliage && !zeroWhileHeld && !bagMismatch)
			return;
		string bagLabel = owner != null ? Who(owner) : "-";
		string line = "sync side=" + Side()
			+ " item=" + Who(item)
			+ " data=" + data
			+ " net=" + net
			+ " parent=" + (parent != null ? parent.name : "none")
			+ " bag=" + bagLabel;
		int id = item.GetInstanceID();
		if (SyncSeen.TryGetValue(id, out string prev) && prev == line)
			return;
		if (SyncSeen.Count > 4000)
			SyncSeen.Clear();
		SyncSeen[id] = line;
		Line(line);
	}

	private static string Why(Container bag, Item item)
	{
		Item owner = BagItem(bag);
		if (item.TryGetComponent(out Container inner) && inner.GetHoldingWeight() > 0f)
			return "stack-weight";
		if (owner != null && owner.TryGetParentContainer(out Container _))
			return "bag-nested";
		if (owner != null && item == owner)
			return "self";
		if (!bag.CanHoldItem(item))
		{
			if (item.totalWeight > bag.maxWeightPerItem)
				return "per-item";
			if (bag.GetHoldingWeight() + item.totalWeight > bag.maxWeight)
				return "full";
			if (bag.tagRestriction != null && bag.tagRestriction.Length != 0)
				return "tags";
			return "canhold";
		}
		if (Vector2.Distance(item.transform.position, bag.transform.position) >= 10f)
			return "far";
		return "ok";
	}

	private static bool Resolve(knetid id, out SyncInfo si, out Item item)
	{
		si = null;
		item = null;
		if (_tryGetItem == null)
		{
			Type sync = AccessTools.TypeByName("Together.ItemSync");
			_tryGetItem = AccessTools.Method(
				sync,
				"TryGetItem",
				new[] { typeof(knetid), typeof(SyncInfo).MakeByRefType(), typeof(Item).MakeByRefType() });
		}
		if (_tryGetItem == null)
			return false;
		object[] args = { id, null, null };
		object ok = _tryGetItem.Invoke(null, args);
		if (!(ok is bool found) || !found)
			return false;
		si = args[1] as SyncInfo;
		item = args[2] as Item;
		return item != null;
	}

	private static Item BagItem(Container bag)
	{
		if (bag == null)
			return null;
		if (_bagItem == null)
			_bagItem = AccessTools.Field(typeof(Container), "mItem");
		Item item = _bagItem != null ? _bagItem.GetValue(bag) as Item : null;
		return item != null ? item : bag.GetComponent<Item>();
	}

	private static int NetOf(knetid id)
	{
		if (_netId == null)
			_netId = typeof(knetid).GetField("id");
		object value = _netId != null ? _netId.GetValue(id) : null;
		return value is ushort number ? number : 0;
	}

	private static string Who(Item item)
	{
		if (item == null)
			return "?";
		return item.id + " sync=" + ItemSyncId.Of(item);
	}

	private static string Bag(Container bag)
	{
		Item item = BagItem(bag);
		return item != null ? Who(item) : bag.name;
	}

	private static string Tags(Item item)
	{
		string[] tags = item.Stats != null ? item.Stats.GetTags() : null;
		if (tags == null || tags.Length == 0)
			return "-";
		return string.Join(",", tags);
	}

	private static string Restrict(Container bag)
	{
		if (bag.tagRestriction == null || bag.tagRestriction.Length == 0)
			return "-";
		return string.Join(",", bag.tagRestriction);
	}

	private static object ContainerInfo(Item item)
	{
		Type sync = AccessTools.TypeByName("Together.ItemSync");
		MethodInfo method = AccessTools.Method(sync, "ItemGetContainerInfo", new[] { typeof(Item) });
		return method != null ? method.Invoke(null, new object[] { item }) : null;
	}

	private static object Field(object box, string name)
	{
		if (box == null)
			return null;
		FieldInfo info = box.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		return info != null ? info.GetValue(box) : null;
	}

	private static bool Flag(object box, string name)
	{
		return Field(box, name) is true;
	}

	private static bool BodyLocal(Limb limb)
	{
		MethodInfo method = AccessTools.Method(AccessTools.TypeByName("Together.Util_MPExtensions"), "IsBodyLocal", new[] { typeof(Limb) });
		return method != null && method.Invoke(null, new object[] { limb }) is true;
	}

	private static string Side()
	{
		if (!Net.IsRunning)
			return "offline";
		return Net.IsServer ? "host" : "client";
	}

	private static void Line(string text)
	{
		if (Plugin.InvTrace == null || !Plugin.InvTrace.Value)
			return;
		Plugin.Log.LogInfo("[KrokV5Opt] bag " + text);
	}
}
