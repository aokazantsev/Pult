package ru.aokazantsev.pult.session

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import ru.aokazantsev.pult.protocol.message.FeedEntry
import ru.aokazantsev.pult.protocol.message.RequestKind
import ru.aokazantsev.pult.session.SessionFixtures.notice
import ru.aokazantsev.pult.session.SessionFixtures.permission

class TimelineBuilderTest {

    private fun call(seq: Long, id: String) =
        FeedEntry.ToolCall(seq, null, false, call = id, tool = "Bash", summary = "ls", input = emptyList())

    private fun result(seq: Long, id: String) =
        FeedEntry.ToolResult(seq, null, false, call = id, output = "ok", error = false)

    private fun unrecognized(seq: Long) = FeedEntry.Unrecognized(seq, null, false, count = 1)

    private fun card(id: String, afterSeq: Long) = RequestCard(
        requestId = id,
        thread = "t1",
        afterSeq = afterSeq,
        kind = RequestKind.Permission,
        summary = "s",
        request = permission(id, afterSeq = afterSeq),
        state = CardState.Open,
    )

    private fun build(entries: List<FeedEntry>, cards: List<RequestCard> = emptyList(), lastSeq: Long? = null): List<TimelineItem> {
        val store = FeedStore()
        store.add("t1", entries)
        return TimelineBuilder.build(store.slice("t1", lastSeq), cards)
    }

    @Test
    fun resultIsMergedUnderItsCall() {
        val items = build(listOf(call(1, "c1"), notice(2), result(3, "c1")))
        assertEquals(2, items.size)
        val step = assertIs<TimelineItem.ToolStep>(items[0])
        assertEquals(3L, step.result?.seq)
    }

    @Test
    fun resultWithoutKnownCallIsShownSeparately() {
        val items = build(listOf(result(5, "orphan")))
        assertIs<TimelineItem.Entry>(items.filterNot { it is TimelineItem.EarlyMissing }.single())
    }

    @Test
    fun adjacentUnrecognizedEntriesAreSummed() {
        val items = build(listOf(unrecognized(1), unrecognized(2), notice(3), unrecognized(4)))
        assertEquals(listOf(2L, 1L), items.filterIsInstance<TimelineItem.Unrecognized>().map { it.count })
    }

    @Test
    fun cardIsPlacedAfterItsSeq() {
        val items = build(listOf(notice(1), notice(2), notice(3)), listOf(card("r1", afterSeq = 2)))
        assertEquals(listOf("e1", "e2", "cardr1", "e3"), items.map { it.key })
    }

    @Test
    fun cardOlderThanStoredEntriesGoesFirstAndLateCardsGoLast() {
        val items = build(listOf(notice(10), notice(11)), listOf(card("r0", 3), card("r9", 50)))
        assertEquals(listOf("early", "cardr0", "e10", "e11", "cardr9"), items.map { it.key })
    }

    @Test
    fun cardStaysNextToItsEntryBeforeGapAndLateMarkers() {
        val items = build(listOf(notice(1), notice(5)), listOf(card("r1", 1), card("r5", 5)), lastSeq = 9)
        assertEquals(listOf("e1", "cardr1", "gap1", "e5", "cardr5", "late"), items.map { it.key })
    }

    @Test
    fun cardInsideGapGoesAfterGapMarker() {
        val items = build(listOf(notice(1), notice(5)), listOf(card("r3", 3)))
        assertEquals(listOf("e1", "gap1", "cardr3", "e5"), items.map { it.key })
    }

    @Test
    fun gapAndEdgeMarkersAreInserted() {
        val items = build(listOf(notice(3), notice(4), notice(8)), lastSeq = 12)
        assertEquals(
            listOf(TimelineItem.EarlyMissing, TimelineItem.Entry(notice(3)), TimelineItem.Entry(notice(4)), TimelineItem.Gap(4, 3), TimelineItem.Entry(notice(8)), TimelineItem.LateMissing),
            items,
        )
    }
}
