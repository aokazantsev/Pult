package ru.aokazantsev.pult.session

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import ru.aokazantsev.pult.protocol.Outcome
import ru.aokazantsev.pult.protocol.message.FeedPosition
import ru.aokazantsev.pult.protocol.message.PcMessage
import ru.aokazantsev.pult.session.SessionFixtures.notice
import ru.aokazantsev.pult.session.SessionFixtures.permission
import ru.aokazantsev.pult.session.SessionFixtures.text
import ru.aokazantsev.pult.session.SessionFixtures.thread

class SessionModelTest {

    private fun modelWithThread(): SessionModel = SessionModel().also {
        it.apply(PcMessage.ThreadUpdate(thread("t1")))
    }

    @Test
    fun requestForUnknownThreadCreatesPlaceholder() {
        val model = SessionModel()
        model.apply(permission("r1", thread = "t9"))
        assertEquals("title t9", model.threads.getValue("t9").title)
        assertEquals(CardState.Open, model.cards.getValue("r1").state)
    }

    @Test
    fun repeatedRequestDoesNotReopenSentOrClosedCard() {
        val model = modelWithThread()
        model.apply(permission("r1"))
        model.markSending("r1", null)
        model.markSent("r1")
        model.apply(permission("r1"))
        assertEquals(CardState.Sent, model.cards.getValue("r1").state)
        model.apply(PcMessage.Resolved("r1", Outcome.Answered))
        model.apply(permission("r1"))
        assertEquals(CardState.Closed, model.cards.getValue("r1").state)
    }

    @Test
    fun firstResolvedIsFinal() {
        val model = modelWithThread()
        model.apply(permission("r1"))
        model.apply(PcMessage.Resolved("r1", Outcome.ResolvedOnPc))
        model.apply(PcMessage.Resolved("r1", Outcome.Expired))
        assertEquals(Outcome.ResolvedOnPc, model.cards.getValue("r1").outcome)
    }

    @Test
    fun failedSendReturnsCardToOpenWithError() {
        val model = modelWithThread()
        model.apply(permission("r1"))
        model.markSending("r1", null)
        model.markFailed("r1", "обрыв")
        val card = model.cards.getValue("r1")
        assertEquals(CardState.Open, card.state)
        assertEquals("обрыв", card.error)
    }

    @Test
    fun unansweredTextWindowReturnsSentTextAsDraft() {
        val model = modelWithThread()
        model.apply(text("r1"))
        model.markSending("r1", "привет")
        model.markSent("r1")
        model.apply(PcMessage.Resolved("r1", Outcome.Expired))
        assertEquals("привет", model.content().drafts["t1"])
    }

    @Test
    fun answeredTextWindowLeavesNoDraft() {
        val model = modelWithThread()
        model.apply(text("r1"))
        model.markSending("r1", "привет")
        model.apply(PcMessage.Resolved("r1", Outcome.Answered))
        assertNull(model.content().drafts["t1"])
    }

    @Test
    fun failedTextSendKeepsTextAsDraftUntilAnswered() {
        val model = modelWithThread()
        model.apply(text("r1"))
        model.markSending("r1", "привет")
        model.markFailed("r1", "обрыв")
        assertEquals("привет", model.content().drafts["t1"])
        model.apply(PcMessage.Resolved("r1", Outcome.Answered))
        assertNull(model.content().drafts["t1"])
    }

    @Test
    fun snapshotOutcomeReturnsSentTextAsDraft() {
        val model = modelWithThread()
        model.apply(text("r1"))
        model.markSending("r1", "привет")
        model.markSent("r1")
        model.applySnapshot(
            listOf(
                SessionFixtures.snapshot(threads = listOf(thread("t1", lastSeq = 0))).copy(
                    outcomes = listOf(
                        ru.aokazantsev.pult.protocol.message.ClosedOutcome(
                            requestId = "r1",
                            kind = ru.aokazantsev.pult.protocol.message.RequestKind.Text,
                            thread = "t1",
                            afterSeq = 0,
                            summary = "done?",
                            outcome = Outcome.Expired,
                        ),
                    ),
                ),
            ),
        )
        assertEquals(CardState.Closed, model.cards.getValue("r1").state)
        assertEquals("привет", model.content().drafts["t1"])
    }

    @Test
    fun sendingNewTextDropsOldDraft() {
        val model = modelWithThread()
        model.apply(text("r1"))
        model.markSending("r1", "первый")
        model.apply(PcMessage.Resolved("r1", Outcome.Expired))
        model.apply(text("r2"))
        model.markSending("r2", "второй")
        assertNull(model.content().drafts["t1"])
    }

    @Test
    fun threadUpdateKeepsLastSeqFromSnapshot() {
        val model = SessionModel()
        model.applySnapshot(listOf(SessionFixtures.snapshot(threads = listOf(thread("t1", lastSeq = 7)))))
        model.apply(PcMessage.ThreadUpdate(thread("t1")))
        assertEquals(7L, model.threads.getValue("t1").lastSeq)
    }

    @Test
    fun feedPositionsListEveryThreadWithItsContiguousEnd() {
        val model = SessionModel()
        model.apply(PcMessage.ThreadUpdate(thread("t1")))
        model.apply(PcMessage.ThreadUpdate(thread("t2")))
        model.apply(PcMessage.Feed("t1", listOf(notice(1), notice(2), notice(3), notice(7))))
        assertEquals(listOf(FeedPosition("t1", 3), FeedPosition("t2", 0)), model.feedPositions())
    }

    @Test
    fun feedPositionJumpsToNewestWhenGapExceedsTail() {
        val model = SessionModel()
        model.apply(PcMessage.Feed("t1", listOf(notice(1), notice(2))))
        model.apply(PcMessage.Feed("t1", (300L..320L).map(SessionFixtures::notice)))
        assertEquals(listOf(FeedPosition("t1", 320)), model.feedPositions())
    }

    @Test
    fun resetErasesFeedsCardsAndDrafts() {
        val model = modelWithThread()
        model.apply(PcMessage.Feed("t1", listOf(notice(1))))
        model.apply(text("r1"))
        model.markSending("r1", "x")
        model.apply(PcMessage.Resolved("r1", Outcome.Expired))
        model.reset()
        val content = model.content()
        assertEquals(emptyList(), content.threads)
        assertEquals(emptyMap(), content.drafts)
        assertEquals(emptyMap(), content.cards)
        assertEquals(emptyList(), model.feedPositions())
    }

    @Test
    fun unknownMessageTypesAreRemembered() {
        val model = SessionModel()
        model.apply(PcMessage.Unknown("future"))
        assertEquals(listOf("future"), model.content().unknownTypes)
    }
}
