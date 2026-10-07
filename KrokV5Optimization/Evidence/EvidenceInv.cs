using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Together;
using UnityEngine;

namespace KrokV5Optimization;

// container-byte trace for an empty bag after rejoin. quiet unless Probe.InvTrace.
// inv host / hostmap: stored parent at load. inv write: a later store.
// inv client / clientmap: local apply. inv retry: rejoin parent attempt.
internal static class EvidenceInv
{
	private struct Stamp
	{
		internal uint Pack;
		internal int Parent;
	}

	internal static long Lines;

	private static readonly Dictionary<int, Stamp> Seen = new Dictionary<int, Stamp>();
	private static readonly Dictionary<ushort, uint> Map = new Dictionary<ushort, uint>();
	private static readonly Dictionary<string, int> Writes = new Dictionary<string, int>();
	private static readonly Dictionary<int, string> Retries = new Dictionary<int, string>();
	private static readonly List<ushort> Order = new List<ushort>(256);
	private static readonly StringBuilder Text = new StringBuilder(256);

	private static PropertyInfo _syncProp;
	private static FieldInfo _syncField;
	private static FieldInfo _rawId;
	private static bool _sidDead;
	private static bool _dirty;
	private static float _next;

	internal static string Evidence() => "invTrace=" + Lines;

	private static bool On => Plugin.InvTrace != null && Plugin.InvTrace.Value;

	internal static void Arm(Harmony harmony)
	{
		MethodInfo update = AccessTools.Method(typeof(SyncInfo), "Update", Type.EmptyTypes);
		if (update == null)
			throw new MissingMethodException("inv trace");
		harmony.Patch(update, postfix: new HarmonyMethod(typeof(EvidenceInv), nameof(AfterSync)));
		Plugin.Log.LogInfo("[KrokV5Opt] inv trace hooked");
	}

	internal static void Tick()
	{
		if (!On || Net.IsServer || !_dirty || Time.realtimeSinceStartup < _next)
			return;
		_next = Time.realtimeSinceStartup + 1f;
		_dirty = false;
		Emit("inv clientmap", Map);
	}

	internal static void DumpHost()
	{
		if (!On || NetObjectRegistry.SyncRegistry == null)
			return;
		var shot = new Dictionary<ushort, uint>();
		foreach (SyncInfo info in NetObjectRegistry.SyncRegistry.Values)
		{
			if (info == null || info.item == null)
				continue;
			ushort sid = Sid(info);
			if (sid == 0)
				continue;
			shot[sid] = Pack(info.container_data1.Value, info.container_netId.Value);
		}
		Emit("inv hostmap", shot);
	}

	internal static void Host(SyncInfo info, byte live, ushort liveNet, byte stored, ushort storedNet, string why)
	{
		if (!On || info == null || info.item == null)
			return;
		bool unread = why == "skip-link" || why == "skip-read";
		bool quiet = why == "same" && live == 0 && liveNet == 0 && stored == 0 && storedNet == 0 && !Held(info.item) && info.item.GetComponent<Container>() == null;
		if (quiet)
			return;
		Say("inv host sync=" + Sid(info)
			+ " item=" + Name(info.item)
			+ " parent=" + Place(info.item)
			+ " live=" + (unread ? "unread" : live + "/" + liveNet)
			+ " stored=" + stored + "/" + storedNet
			+ " do=" + why);
	}

	internal static void Wrote(SyncInfo info, byte fromData, ushort fromNet, byte toData, ushort toNet, string via)
	{
		if (!On || info == null || info.item == null)
			return;
		bool changed = fromData != toData || fromNet != toNet;
		bool held = Held(info.item);
		if (!changed && via == "cache")
			return;
		if (!changed && !held && fromData == 0 && fromNet == 0 && toData == 0 && toNet == 0)
			return;
		ushort sid = Sid(info);
		string key = sid + "|" + via + "|" + fromData + "/" + fromNet + ">" + toData + "/" + toNet + "|" + ParentId(info.item);
		if (Writes.TryGetValue(key, out int count))
		{
			if (count >= 8)
				return;
			Writes[key] = count + 1;
		}
		else
		{
			if (Writes.Count > 8000)
				Writes.Clear();
			Writes[key] = 1;
		}
		Say("inv write sync=" + sid
			+ " item=" + Name(info.item)
			+ " parent=" + Place(info.item)
			+ " from=" + fromData + "/" + fromNet
			+ " to=" + toData + "/" + toNet
			+ " via=" + via);
	}

	internal static void Retry(SyncInfo info, string why)
	{
		if (!On || info == null || info.item == null)
			return;
		int key = Key(info);
		string sig = why + "|" + info.container_data1.Value + "/" + info.container_netId.Value + "|" + ParentId(info.item);
		if ((why == "wait" || why == "miss") && Retries.TryGetValue(key, out string prev) && prev == sig)
			return;
		if (Retries.Count > 8000)
			Retries.Clear();
		Retries[key] = sig;
		Say("inv retry sync=" + Sid(info)
			+ " item=" + Name(info.item)
			+ " parent=" + Place(info.item)
			+ " data=" + info.container_data1.Value + "/" + info.container_netId.Value
			+ " do=" + why);
	}

	static void AfterSync(SyncInfo __instance)
	{
		if (!On)
			return;
		// SyncInfo.item throws before the unity object exists. a throw here marks the entity built with no object.
		if (Net.IsServer || __instance == null || __instance.go == null)
			return;
		Item item;
		try
		{
			item = __instance.item;
		}
		catch (Exception)
		{
			return;
		}
		if (item == null)
			return;
		byte data = __instance.container_data1.Value;
		ushort net = __instance.container_netId.Value;
		int parent = ParentId(item);
		uint pack = Pack(data, net);
		int key = Sid(__instance);
		if (key == 0)
			key = -item.GetInstanceID();
		bool seen = Seen.TryGetValue(key, out Stamp prev);
		if (seen && prev.Pack == pack && prev.Parent == parent)
			return;
		if (Seen.Count > 8000)
			Seen.Clear();
		Seen[key] = new Stamp { Pack = pack, Parent = parent };
		ushort sid = Sid(__instance);
		if (sid != 0 && (!seen || prev.Pack != pack))
		{
			Map[sid] = pack;
			_dirty = true;
		}
		bool show = data != 0 || net != 0 || Held(item) || item.GetComponent<Container>() != null;
		if (!show && seen)
			show = true;
		if (!show)
			return;
		Say("inv client sync=" + sid
			+ " item=" + Name(item)
			+ " parent=" + Place(item)
			+ " data=" + data + "/" + net);
	}

	private static void Emit(string tag, Dictionary<ushort, uint> map)
	{
		Say(tag + " items=" + map.Count);
		if (map.Count == 0)
			return;
		Order.Clear();
		foreach (ushort id in map.Keys)
			Order.Add(id);
		Order.Sort();
		Text.Length = 0;
		for (int i = 0; i < Order.Count; i++)
		{
			ushort id = Order[i];
			uint pack = map[id];
			string piece = "id=" + id + ":" + (pack >> 16) + "/" + (ushort)pack;
			if (Text.Length > 0 && Text.Length + piece.Length > 700)
			{
				Say(tag + " " + Text);
				Text.Length = 0;
			}
			if (Text.Length > 0)
				Text.Append(' ');
			Text.Append(piece);
		}
		if (Text.Length > 0)
			Say(tag + " " + Text);
	}

	private static void Say(string line)
	{
		if (!On)
			return;
		Lines++;
		Plugin.Log.LogInfo("[KrokV5Opt] " + line);
	}

	private static uint Pack(byte data, ushort net) => ((uint)data << 16) | net;

	private static int Key(SyncInfo info)
	{
		ushort sid = Sid(info);
		return sid != 0 ? sid : -info.item.GetInstanceID();
	}

	private static int ParentId(Item item)
	{
		Transform parent = item.transform.parent;
		return parent == null ? 0 : parent.GetInstanceID();
	}

	private static bool Held(Item item)
	{
		Transform parent = item.transform.parent;
		if (parent == null)
			return false;
		return parent.GetComponent<InventorySlot>() != null
			|| parent.GetComponent<Limb>() != null
			|| parent.GetComponent<Container>() != null;
	}

	private static string Place(Item item)
	{
		Transform parent = item.transform.parent;
		if (parent == null)
			return "none";
		string kind = "other";
		if (parent.GetComponent<InventorySlot>() != null)
			kind = "slot";
		else if (parent.GetComponent<Limb>() != null)
			kind = "worn";
		else if (parent.GetComponent<Container>() != null)
			kind = "bag";
		string name = parent.name;
		if (string.IsNullOrEmpty(name))
			name = "?";
		if (name.Length > 80)
			name = name.Substring(0, 80);
		return kind + ":" + name;
	}

	private static string Name(Item item)
	{
		if (!string.IsNullOrEmpty(item.id))
			return item.id;
		return string.IsNullOrEmpty(item.name) ? "?" : item.name;
	}

	private static ushort Sid(SyncInfo info)
	{
		if (_sidDead || info == null)
			return 0;
		try
		{
			if (_syncProp == null && _syncField == null)
			{
				_syncProp = AccessTools.Property(typeof(SyncInfo), "syncId");
				if (_syncProp == null)
					_syncField = AccessTools.Field(typeof(SyncInfo), "syncId");
				if (_syncProp == null && _syncField == null)
				{
					_sidDead = true;
					return 0;
				}
			}
			object knet = _syncProp != null ? _syncProp.GetValue(info) : _syncField.GetValue(info);
			if (knet == null)
				return 0;
			if (_rawId == null)
				_rawId = knet.GetType().GetField("id");
			object raw = _rawId != null ? _rawId.GetValue(knet) : null;
			return raw is ushort id ? id : (ushort)0;
		}
		catch (Exception ex)
		{
			_sidDead = true;
			Plugin.Log.LogWarning("[KrokV5Opt] inv trace id off: " + ex.Message);
			return 0;
		}
	}
}
