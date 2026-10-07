# KrokV5Optimization

Fixes for Together on Casualties Unknown. The memory list is stuff the game kept allocating and throwing away. The bug list is desync and other broken behavior.

# Memory

## /Fixes/FixUtilCompress.cs

/Fixes/FixUtilDecompress.cs, /Fixes/FixUtilCompressDeflate.cs, /Fixes/FixUtilDecompressDeflate.cs, /CompressPool.cs, /ResettableDeflate.cs

**Zipping a packet.** Every zip and unzip built a brand new zipper, for both zip formats, and the old one stayed stuck in memory for the whole session. One zipper is kept for each format and reused, and a payload that is not actually zipped is handed back to the game.

## /Fixes/FixCompressWriter.cs

/CompressPool.cs, /ResettableDeflate.cs

**Copying the zipped bytes.** After a packet was zipped, the result was copied into a fresh byte array and the old buffer was dropped. The zipped bytes are written into the packet buffer that already exists.

## /Fixes/FixChunkBlock.cs

/Fixes/FixChunkFluid.cs, /ChunkWire.cs, /Fixes/FixUtilCompressDeflate.cs, /CompressPool.cs, /ResettableDeflate.cs

**Sending a map chunk.** The block layer and the liquid layer each built a new zipper for that one chunk. They use the shared zipper, and the packet other players get is the same.

## /Fixes/FixChunkHashes.cs

**Chunk hash grid.** Every server frame threw away the grid of chunk hashes and allocated a new one. Zero already means empty, so the grid that exists is wiped and kept.

## /Fixes/FixWorldBlocks.cs

**World maps on a new layer.** Starting a layer allocated a new block map and a new liquid map the size of the whole world, even when the world was the same size. Same width and height keep the old maps and clear them, and a real size change still allocates.

## /Fixes/FixParticles.cs

**Liquid particles.** Drawing liquid built a fresh list of particles, copied it into a fresh array, and threw both away. One array is kept per liquid, and only the filled part is shown.

## /Fixes/FixSkinSkip.cs

**Button skin rebuild.** The multiplayer button skin was rebuilt on every UI pass, which is what created the throwaway text styles and padding. The rebuild is skipped when the scale, the screen size, and the recalc flag have not changed, and no menu is open.

## /Fixes/FixGuiStyle.cs

/ImguiScan.cs

**Text styles.** Every UI pass copied a text style and threw the copy away. Each place keeps one copy, and it is rebuilt only when the skin actually changes.

## /Fixes/FixRectOffset.cs

/ImguiScan.cs

**Padding boxes.** Every UI pass built new padding boxes even when the four numbers had not changed. The same left, right, top, and bottom share one box.

## /Fixes/FixGuiContent.cs

/ImguiScan.cs

**Text labels.** Every UI pass built new text labels, once while laying out and again while painting. Each place keeps one label and writes the new words into it.

## /Fixes/FixGuiLayout.cs

**HUD drawn twice.** The in-game HUD was drawn twice inside one UI event, and the second draw built a layout and threw it away. That second call is skipped, while the layout pass and the visible draw still each run once.

## /Fixes/FixLiquidWalk.cs

**Liquid lists.** Filling, pouring, and reading a container walked the liquid list in a way that allocated a small object every call. A plain counted loop reads and writes the same list.

## /Fixes/FixInvDrag.cs

**Bag weight and tags.** Asking a bag how full it is, or whether a dragged item fits, allocated a small object and sometimes a whole list of tags. The weight is a counted loop over the bag's children, and the tag check compares the tags directly.

## /Fixes/FixContainerInfo.cs

**Where an item is sitting.** Every synced item built a small note about its parent and threw it away, even when the item had not moved. If the parent has not changed and the last answer had a real network id, that answer is written again and the note is not rebuilt.

## /Fixes/FixScrapDesc.cs

**Scrap eater text.** The scrap eater rewrote its description every frame, even when the percent on screen had not changed, and each rewrite left the old sentence behind. The sentence is kept until the rounded percent changes.

## /Fixes/FixItemTooltip.cs

**Item hover text.** Every inventory slot rebuilt its hover paragraph every frame, and the change check itself built a fresh display name. The last paragraph is reused until the fields that show up in it change, and that check skips the display name.

# Bugs

## /Fixes/FixSyncRegister.cs

**Container lookup lied.** The first time an item's container was looked up, the game created the network record and then said the lookup failed, so the item was saved as belonging to nobody. The lookup returns the record it just created, so the parent id is the container's id.

## /Fixes/FixHostPickup.cs

**Host item pickup.** Dragging an item onto the host's inventory or containers did not register it for the network, so a later sweep could still call it loose on the ground. The item is registered right after the pickup, and the first update already says it is in the container's slot.

## /Fixes/FixContainerBroke.cs

**Broken container on a client.** When a container broke, every machine spilled its own copy, and clients were left with a local item they could see and could not pick up. On a client, children that were never registered are removed before that spill, and synced children still spill so the slot update can put them back.

## /Fixes/FixCraftOrder.cs

**Host crafted twice.** On the host, a craft already spent the ingredients and spawned the result, then asked the server to craft it again, and the server denied it because the ingredients were gone. The host skips that second request, and another player's client still asks the server to craft for them.

## /Fixes/FixChoicePrompt.cs

**Tiny accept and deny.** Accept and Deny on a choice prompt used the tiny button skin, so the buttons were hard to hit. The prompt draws with the large skin, then the small skin is put back for the rest of the frame.

## /Fixes/FixSyringeHold.cs

**Syringe on the ground.** Injecting a syringe changes how full it is without moving it, but other players only heard about the liquid, so their copy fell on the ground and could not be picked up. When the fill or the liquid changes, the same packet is sent again with the slot filled in.

## /Fixes/FixLateBaseline.cs

**Joiner got an empty snapshot.** A joining player was told which objects exist but not their current values, and once the server had been up a while "I have received nothing" was treated as "I already have the latest," so worn gear and slots never arrived. The first packet to that player includes every current value, and after that the packets stay small updates, with a matching object updated instead of spawned twice.

## /Fixes/FixRejoinBaseline.cs

**Snapshot before the world finished.** That full snapshot was taken when the player was accepted, before the layer finished loading and before their saved items were on the new body. When they report that world generation is finished, they get one more full snapshot, which updates objects they already have and creates the ones they are missing.

## /Fixes/FixInvSync.cs

**Empty slot or empty bag.** A stored "not inside anything" was repeated to everyone else, and it was also written when a slot or bag still held the item but the body link was missing, so other players saw an empty bag. While the item is still in that parent the empty value is left alone, the host rereads the real parent when someone finishes loading, and the client keeps trying to put the item back until the bag or body exists.

## /Fixes/FixSimRange.cs

**Enemies stopped far from the host.** Spiders and grabber plants stopped as soon as the host's chunk renderer turned off, about two chunks from the host camera, even when another player was standing on them. Each player keeps the chunk they stand in and the eight around it, and in multiplayer that area is the only simulation test those enemies use.

## /Fixes/FixMenuFps.cs

**Main menu ran uncapped.** The main menu ran as fast as the machine would go, because that screen never counts as a menu being open. While no world is loaded the frame cap is 60 and vsync is off, and entering a world puts both back to the video settings.
