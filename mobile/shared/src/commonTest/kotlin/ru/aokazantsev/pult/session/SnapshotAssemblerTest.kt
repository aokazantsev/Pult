package ru.aokazantsev.pult.session

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import ru.aokazantsev.pult.session.SessionFixtures.snapshot

class SnapshotAssemblerTest {

    @Test
    fun singlePartCompletesImmediately() {
        val step = SnapshotAssembler().accept(snapshot())
        assertEquals(1, assertIs<SnapshotStep.Complete>(step).parts.size)
    }

    @Test
    fun partsAreCollectedInOrder() {
        val assembler = SnapshotAssembler()
        assertEquals(SnapshotStep.Incomplete, assembler.accept(snapshot(part = 1, last = false)))
        val step = assembler.accept(snapshot(part = 2, last = true))
        assertEquals(listOf(1L, 2L), assertIs<SnapshotStep.Complete>(step).parts.map { it.part })
    }

    @Test
    fun skippedNumberDiscardsAssembly() {
        val assembler = SnapshotAssembler()
        assembler.accept(snapshot(part = 1, last = false))
        assertEquals(SnapshotStep.Discarded, assembler.accept(snapshot(part = 3, last = true)))
        assertEquals(SnapshotStep.Discarded, assembler.accept(snapshot(part = 2, last = true)))
    }

    @Test
    fun partOfAnotherSnapshotDiscardsAssembly() {
        val assembler = SnapshotAssembler()
        assembler.accept(snapshot(id = "a", part = 1, last = false))
        assertEquals(SnapshotStep.Discarded, assembler.accept(snapshot(id = "b", part = 2, last = true)))
    }

    @Test
    fun newFirstPartRestartsAssembly() {
        val assembler = SnapshotAssembler()
        assembler.accept(snapshot(id = "a", part = 1, last = false))
        assembler.accept(snapshot(id = "b", part = 1, last = false))
        val step = assembler.accept(snapshot(id = "b", part = 2, last = true))
        assertEquals(listOf("b", "b"), assertIs<SnapshotStep.Complete>(step).parts.map { it.snapshot })
    }

    @Test
    fun lonelyLaterPartIsDiscarded() {
        assertEquals(SnapshotStep.Discarded, SnapshotAssembler().accept(snapshot(part = 2)))
    }
}
