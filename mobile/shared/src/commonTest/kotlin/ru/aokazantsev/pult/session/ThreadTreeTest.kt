package ru.aokazantsev.pult.session

import kotlin.test.Test
import kotlin.test.assertEquals
import ru.aokazantsev.pult.protocol.message.RequestKind
import ru.aokazantsev.pult.session.SessionFixtures.permission
import ru.aokazantsev.pult.session.SessionFixtures.text
import ru.aokazantsev.pult.session.SessionFixtures.thread

class ThreadTreeTest {

    private fun card(request: ru.aokazantsev.pult.protocol.message.Request, state: CardState = CardState.Open) = RequestCard(
        requestId = request.requestId,
        thread = request.thread,
        afterSeq = 0,
        kind = RequestKind.Permission,
        summary = "s",
        request = request,
        state = state,
    )

    @Test
    fun childrenFollowTheirParent() {
        val rows = ThreadTree.build(
            listOf(thread("a"), thread("b"), thread("a1", parent = "a"), thread("a2", parent = "a"), thread("a11", parent = "a1")),
            emptyList(),
        )
        assertEquals(listOf("a" to 0, "a1" to 1, "a11" to 2, "a2" to 1, "b" to 0), rows.map { it.info.thread to it.depth })
    }

    @Test
    fun orphanWithMissingParentIsTopLevel() {
        val rows = ThreadTree.build(listOf(thread("x", parent = "gone")), emptyList())
        assertEquals(listOf("x" to 0), rows.map { it.info.thread to it.depth })
    }

    @Test
    fun parentCycleDoesNotLoseThreads() {
        val rows = ThreadTree.build(listOf(thread("a", parent = "b"), thread("b", parent = "a")), emptyList())
        assertEquals(setOf("a", "b"), rows.map { it.info.thread }.toSet())
        assertEquals(2, rows.size)
    }

    @Test
    fun marksComeFromOpenCardsOnly() {
        val rows = ThreadTree.build(
            listOf(thread("a"), thread("b")),
            listOf(
                card(permission("r1", "a")),
                card(text("r2", "b")),
                card(permission("r3", "b"), CardState.Closed),
            ),
        )
        assertEquals(ThreadMarks(awaitsAction = true), rows.first { it.info.thread == "a" }.marks)
        assertEquals(ThreadMarks(awaitsText = true), rows.first { it.info.thread == "b" }.marks)
    }

    @Test
    fun missingCardGivesNoMark() {
        val rows = ThreadTree.build(listOf(thread("a")), listOf(card(permission("r1", "a"), CardState.Missing)))
        assertEquals(ThreadMarks(), rows.single().marks)
    }

    @Test
    fun parentSummarizesChildMarks() {
        val rows = ThreadTree.build(
            listOf(thread("p"), thread("c", parent = "p"), thread("g", parent = "c")),
            listOf(card(text("r1", "g"), CardState.Sent)),
        )
        assertEquals(ThreadMarks(childAwaitsText = true), rows.first { it.info.thread == "p" }.marks)
        assertEquals(ThreadMarks(childAwaitsText = true), rows.first { it.info.thread == "c" }.marks)
        assertEquals(ThreadMarks(awaitsText = true), rows.first { it.info.thread == "g" }.marks)
    }

    @Test
    fun choiceWithoutActionsDoesNotAwaitAction() {
        val choice = ru.aokazantsev.pult.protocol.message.Request.Choice(
            requestId = "r1",
            thread = "a",
            title = "t",
            afterSeq = 0,
            answerable = false,
            questions = emptyList(),
        )
        val rows = ThreadTree.build(listOf(thread("a")), listOf(card(choice)))
        assertEquals(ThreadMarks(), rows.single().marks)
    }
}
