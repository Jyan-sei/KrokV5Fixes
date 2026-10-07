namespace KrokV5Optimization;

internal static class Report
{
	internal static string Line() =>
		FixUtilCompress.Evidence()
		+ " " + FixCompressWriter.Evidence()
		+ " " + FixUtilCompressDeflate.Evidence()
		+ " " + FixUtilDecompress.Evidence()
		+ " " + FixUtilDecompressDeflate.Evidence()
		+ " " + FixChunkBlock.Evidence()
		+ " " + FixChunkFluid.Evidence()
		+ " " + FixSkinSkip.Evidence()
		+ " " + FixGuiContent.Evidence()
		+ " " + FixGuiStyle.Evidence()
		+ " " + FixRectOffset.Evidence()
		+ " " + EvidenceContainerInfo.Evidence()
		+ " " + FixContainerInfo.Evidence()
		+ " " + FixSyringeHold.Evidence()
		+ " " + EvidenceNestedLiquid.Evidence()
		+ " " + EvidenceSyncInfo.Evidence()
		+ " " + EvidenceLiquidDictionary.Evidence()
		+ " " + EvidenceParticles.Evidence()
		+ " " + EvidenceStrings.Evidence()
		+ " " + FixScrapDesc.Evidence()
		+ " " + FixItemTooltip.Evidence()
		+ " " + FixInvDrag.Evidence()
		+ " " + FixSimRange.Evidence()
		+ " " + EvidenceFloatArrays.Evidence()
		+ " " + FixChunkHashes.Evidence()
		+ " " + FixWorldBlocks.Evidence()
		+ " " + FixLiquidWalk.Evidence()
		+ " " + FixGuiLayout.Evidence()
		+ " " + EvidenceBag.Evidence()
		+ " " + FixInvSync.Evidence()
		+ " " + EvidenceInv.Evidence();
}
