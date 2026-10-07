using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Together;
using UnityEngine;

namespace KrokV5Optimization;

/// <summary>
/// WriteContainerInfo built a new ItemsContainerInfo even when the parent had not changed.
/// reuse the last answer when the parent is unchanged and container_netId is not 0.
/// a slot or bag with a null syncBody used to store container_data1 0 without unparenting, and the full snapshot repeated that 0.
/// while the item is still in that slot or bag, keep the last real parent. a real drop has no parent, so it still publishes 0. worn gear is left alone.
/// </summary>
[HarmonyPatch(typeof(SyncInfo), "WriteContainerInfo")]
internal static class FixContainerInfo
{
	private struct Hold
	{
		internal int Parent;
		internal ushort Net;
		internal byte Data;
		internal bool Wait;
		internal bool Known;
	}

	private struct Shot
	{
		internal bool Ok;
		internal byte Data;
		internal ushort Net;
		internal string Via;
	}

	internal static long Hits;
	internal static long ParentHolds;

	private static readonly Dictionary<int, Hold> Cache = new Dictionary<int, Hold>();
	private static FieldInfo _netField;
	private static FieldInfo _dataField;
	private static Func<object, ushort> _readNet;
	private static Action<object, ushort> _writeNet;
	private static Func<object, byte> _readData;
	private static Action<object, byte> _writeData;
	private static bool _ready;
	private static bool _dead;

	// this file's counters on the 10s report.
	internal static string Evidence() => "containerHold=" + Hits + " parentHold=" + ParentHolds;

	// false skips WriteContainerInfo. true lets stock build a fresh ItemsContainerInfo.
	static bool Prefix(SyncInfo __instance, out Shot __state)
	{
		__state = default;
		if (!Ready() || __instance.item == null)
			return true;
		object net = _netField.GetValue(__instance);
		object data = _dataField.GetValue(__instance);
		if (net != null && data != null)
		{
			__state.Ok = true;
			__state.Data = __instance.container_data1.Value;
			__state.Net = __instance.container_netId.Value;
		}
		// null syncBody would make stock store 0. skip the write and leave the last real parent.
		if (ParentLinkMissing(__instance.item))
		{
			__state.Via = "link";
			ParentHolds++;
			return false;
		}
		Item item = __instance.item;
		int id = item.GetInstanceID();
		int parent = ParentId(item);
		// parent moved, or the last net id was 0. stock has to read the container again.
		if (!Cache.TryGetValue(id, out Hold hold) || !hold.Known || hold.Wait || hold.Parent != parent)
		{
			__state.Via = "original";
			return true;
		}
		if (net == null || data == null)
			return true;
		_writeNet(net, hold.Net);
		_writeData(data, hold.Data);
		Hits++;
		__state.Via = "cache";
		return false;
	}

	// stock already wrote the fields. remember them so the next unchanged parent can skip the alloc.
	static void Postfix(SyncInfo __instance, Shot __state)
	{
		if (!Ready() || __instance.item == null)
			return;
		Item item = __instance.item;
		object netBox = _netField.GetValue(__instance);
		object dataBox = _dataField.GetValue(__instance);
		if (netBox == null || dataBox == null)
			return;
		ushort net = _readNet(netBox);
		byte data = _readData(dataBox);
		int parent = ParentId(item);
		// container_netId 0 with a parent means the container was not registered yet. caching that leaves the client unable to parent.
		bool wait = net == 0 && parent != 0;
		if (Cache.Count > 20000)
			Cache.Clear();
		Cache[item.GetInstanceID()] = new Hold { Parent = parent, Net = net, Data = data, Wait = wait, Known = true };
		if (__state.Ok)
			EvidenceInv.Wrote(__instance, __state.Data, __state.Net, __instance.container_data1.Value, __instance.container_netId.Value, __state.Via ?? "original");
	}

	// one-time lookup of the SyncVar fields. a miss turns the cache off for the session.
	private static bool Ready()
	{
		if (_ready)
			return true;
		if (_dead)
			return false;
		try
		{
			_netField = AccessTools.Field(typeof(SyncInfo), "container_netId");
			_dataField = AccessTools.Field(typeof(SyncInfo), "container_data1");
			if (_netField == null || _dataField == null)
				throw new MissingFieldException("container sync fields");
			_readNet = ReadKnet(_netField.FieldType);
			_writeNet = WriteKnet(_netField.FieldType);
			_readData = ReadByte(_dataField.FieldType);
			_writeData = WriteByte(_dataField.FieldType);
			_ready = true;
			return true;
		}
		catch (Exception ex)
		{
			_dead = true;
			Plugin.Log.LogWarning("[KrokV5Opt] container cache off: " + ex.Message);
			return false;
		}
	}

	// FixInvSync calls this after it stores a parent, so the next WriteContainerInfo does not rebuild the note.
	internal static void Remember(Item item, ushort net, byte data)
	{
		if (item == null || !Ready())
			return;
		int parent = ParentId(item);
		if (Cache.Count > 20000)
			Cache.Clear();
		Cache[item.GetInstanceID()] = new Hold
		{
			Parent = parent,
			Net = net,
			Data = data,
			Wait = net == 0 && parent != 0,
			Known = true
		};
	}

	// slot or bag, body exists, syncBody is null. worn gear and a real drop do not match.
	internal static bool ParentLinkMissing(Item item)
	{
		Transform parent = item.transform.parent;
		if (parent == null)
			return false;
		InventorySlot slot = parent.GetComponent<InventorySlot>();
		if (slot != null)
			return slot.body != null && slot.body.TryGetNetBody(out NetBody body) && body.syncBody == null;
		if (parent.GetComponent<Container>() == null)
			return false;
		NetBody owner = item.GetComponentInParent<NetBody>();
		return owner != null && owner.syncBody == null;
	}

	// 0 means no parent, which is a real drop. an instance id is enough; we only compare it to last time.
	private static int ParentId(Item item)
	{
		Transform parent = item.transform.parent;
		return parent == null ? 0 : parent.GetInstanceID();
	}

	// SyncVar<knetid>.Value boxes. this delegate reads the ushort without that box.
	private static Func<object, ushort> ReadKnet(Type syncVar)
	{
		MethodInfo getter = syncVar.GetProperty("Value").GetGetMethod();
		FieldInfo id = typeof(knetid).GetField("id");
		var method = new DynamicMethod("krok_read_knet", typeof(ushort), new[] { typeof(object) }, typeof(FixContainerInfo).Module, true);
		ILGenerator il = method.GetILGenerator();
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Castclass, syncVar);
		il.Emit(OpCodes.Callvirt, getter);
		il.Emit(OpCodes.Ldfld, id);
		il.Emit(OpCodes.Ret);
		return (Func<object, ushort>)method.CreateDelegate(typeof(Func<object, ushort>));
	}

	// writes SyncVar<knetid> from a ushort. used when the cache hits, so stock never runs.
	private static Action<object, ushort> WriteKnet(Type syncVar)
	{
		MethodInfo setter = syncVar.GetProperty("Value").GetSetMethod();
		ConstructorInfo ctor = typeof(knetid).GetConstructor(new[] { typeof(ushort) });
		var method = new DynamicMethod("krok_write_knet", typeof(void), new[] { typeof(object), typeof(ushort) }, typeof(FixContainerInfo).Module, true);
		ILGenerator il = method.GetILGenerator();
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Castclass, syncVar);
		il.Emit(OpCodes.Ldarg_1);
		il.Emit(OpCodes.Newobj, ctor);
		il.Emit(OpCodes.Callvirt, setter);
		il.Emit(OpCodes.Ret);
		return (Action<object, ushort>)method.CreateDelegate(typeof(Action<object, ushort>));
	}

	// SyncVar<byte>.Value, unboxed.
	private static Func<object, byte> ReadByte(Type syncVar)
	{
		MethodInfo getter = syncVar.GetProperty("Value").GetGetMethod();
		var method = new DynamicMethod("krok_read_byte", typeof(byte), new[] { typeof(object) }, typeof(FixContainerInfo).Module, true);
		ILGenerator il = method.GetILGenerator();
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Castclass, syncVar);
		il.Emit(OpCodes.Callvirt, getter);
		il.Emit(OpCodes.Ret);
		return (Func<object, byte>)method.CreateDelegate(typeof(Func<object, byte>));
	}

	// SyncVar<byte> setter, unboxed. container_data1 is 0 loose, 1 bag, 100+ a body slot.
	private static Action<object, byte> WriteByte(Type syncVar)
	{
		MethodInfo setter = syncVar.GetProperty("Value").GetSetMethod();
		var method = new DynamicMethod("krok_write_byte", typeof(void), new[] { typeof(object), typeof(byte) }, typeof(FixContainerInfo).Module, true);
		ILGenerator il = method.GetILGenerator();
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Castclass, syncVar);
		il.Emit(OpCodes.Ldarg_1);
		il.Emit(OpCodes.Callvirt, setter);
		il.Emit(OpCodes.Ret);
		return (Action<object, byte>)method.CreateDelegate(typeof(Action<object, byte>));
	}
}
