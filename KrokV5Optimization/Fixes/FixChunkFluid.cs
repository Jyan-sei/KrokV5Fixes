using HarmonyLib;
using Together;

namespace KrokV5Optimization;

/// <summary>
/// the fluid chunk send built a new DeflateStream, same as the block send.
/// zip with the shared deflater and return the caller's packet buffer.
/// </summary>
[HarmonyPatch(typeof(WorldChunkSync), nameof(WorldChunkSync._Server_PackFluidChunk))]
internal static class FixChunkFluid
{
	internal static long Hits;

	// this file's counters on the 10s report.
	internal static string Evidence() => "chunkFluid=" + Hits;

	// false skips stock _Server_PackFluidChunk and returns our writer. true lets stock build its own.
	static bool Prefix(ref Vector2UInt8 syncchunk_coordinate, ref object __result)
	{
		if (Plugin.CompressPooling != null && !Plugin.CompressPooling.Value)
			return true;
		try
		{
			if (!Try(syncchunk_coordinate, out object writer))
				return true;
			__result = writer;
			return false;
		}
		catch (System.Exception ex)
		{
			ChunkWire.Note(ex);
			return true;
		}
	}

	// same pack as blocks, message id is the fluid chunk. does not send; the caller sends the writer we return.
	private static bool Try(Vector2UInt8 coord, out object writer)
	{
		writer = null;
		byte[,] fluid = WorldChunkSync.fluid;
		if (fluid == null || !ChunkWire.CanWrite())
			return false;

		ChunkWire.Pack(fluid, coord.x * ChunkWire.Tiles, coord.y * ChunkWire.Tiles);
		if (!FixUtilCompressDeflate.Try(ChunkWire.Raw, out byte[] compressed) || compressed == null)
			return false;

		writer = ChunkWire.Begin(NetmsgId.CLIENT_WorldFluidTilemapChunk);
		if (writer == null || !ChunkWire.PutByte(writer, coord.x) || !ChunkWire.PutByte(writer, coord.y) || !ChunkWire.PutBytes(writer, compressed))
		{
			writer = null;
			return false;
		}
		Hits++;
		return true;
	}
}
