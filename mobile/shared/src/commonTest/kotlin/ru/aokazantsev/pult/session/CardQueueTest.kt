package ru.aokazantsev.pult.session

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertTrue
import ru.aokazantsev.pult.protocol.Envelope
import ru.aokazantsev.pult.protocol.InboundMessage
import ru.aokazantsev.pult.protocol.StrictJson
import ru.aokazantsev.pult.protocol.Outcome

class CardQueueTest {

    private fun envelope(id: String, inner: String) =
        Envelope(id = id, from = "pc", ttl = 120, inner = StrictJson.parseObject(inner))

    private fun permission(id: String, requestId: String) =
        envelope(id, """{"type":"permission_request","request_id":"$requestId","tool":"Bash","summary":"ls"}""")

    private fun resolved(id: String, requestId: String, outcome: String) =
        envelope(id, """{"type":"resolved","request_id":"$requestId","outcome":"$outcome"}""")

    @Test
    fun requestCreatesPendingCard() {
        val queue = CardQueue()
        assertTrue(queue.accept(permission("e1", "r1")))
        val card = queue.cards.single()
        assertEquals("r1", card.key)
        assertEquals(CardPhase.Pending, card.phase)
        assertIs<InboundMessage.Permission>(card.message)
    }

    @Test
    fun repeatedEnvelopeIdIsDropped() {
        val queue = CardQueue()
        assertTrue(queue.accept(permission("e1", "r1")))
        assertFalse(queue.accept(permission("e1", "r1")))
        assertEquals(1, queue.cards.size)
    }

    @Test
    fun repeatedRequestIdUnderNewEnvelopeDoesNotDuplicateCard() {
        val queue = CardQueue()
        queue.accept(permission("e1", "r1"))
        queue.accept(permission("e2", "r1"))
        assertEquals(1, queue.cards.size)
    }

    @Test
    fun processedIdsAreRememberedAfterResolved() {
        val queue = CardQueue()
        queue.accept(permission("e1", "r1"))
        queue.accept(resolved("e2", "r1", "expired"))
        assertFalse(queue.accept(permission("e1", "r1")))
        assertEquals(CardPhase.Closed, queue.cards.single().phase)
    }

    @Test
    fun resolvedClosesCardWithOutcome() {
        val queue = CardQueue()
        queue.accept(permission("e1", "r1"))
        queue.accept(resolved("e2", "r1", "resolved_on_pc"))
        val card = queue.cards.single()
        assertEquals(CardPhase.Closed, card.phase)
        assertEquals(Outcome.ResolvedOnPc, card.outcome)
    }

    @Test
    fun firstResolvedIsFinal() {
        val queue = CardQueue()
        queue.accept(permission("e1", "r1"))
        queue.accept(resolved("e2", "r1", "answered"))
        queue.accept(resolved("e3", "r1", "expired"))
        assertEquals(Outcome.Answered, queue.cards.single().outcome)
    }

    @Test
    fun resolvedForUnknownRequestIsIgnored() {
        val queue = CardQueue()
        queue.accept(resolved("e1", "ghost", "expired"))
        assertEquals(emptyList(), queue.cards)
    }

    @Test
    fun sendingFlowEndsInSentThenClosed() {
        val queue = CardQueue()
        queue.accept(permission("e1", "r1"))
        queue.markSending("r1")
        assertEquals(CardPhase.Sending, queue.cards.single().phase)
        queue.markSent("r1")
        assertEquals(CardPhase.Sent, queue.cards.single().phase)
        queue.accept(resolved("e2", "r1", "answered"))
        assertEquals(CardPhase.Closed, queue.cards.single().phase)
    }

    @Test
    fun failedSendReturnsCardToPendingWithError() {
        val queue = CardQueue()
        queue.accept(permission("e1", "r1"))
        queue.markSending("r1")
        queue.markFailed("r1", "нет сети")
        val card = queue.cards.single()
        assertEquals(CardPhase.Pending, card.phase)
        assertEquals("нет сети", card.error)
    }

    @Test
    fun lateSentDoesNotReopenClosedCard() {
        val queue = CardQueue()
        queue.accept(permission("e1", "r1"))
        queue.accept(resolved("e2", "r1", "expired"))
        queue.markSent("r1")
        queue.markFailed("r1", "x")
        assertEquals(CardPhase.Closed, queue.cards.single().phase)
    }

    @Test
    fun unknownTypeBecomesCardWithoutActions() {
        val queue = CardQueue()
        queue.accept(envelope("e9", """{"type":"snapshot"}"""))
        assertIs<InboundMessage.Unknown>(queue.cards.single().message)
    }

    @Test
    fun statusAndModeProduceNoCards() {
        val queue = CardQueue()
        queue.accept(envelope("e1", """{"type":"status","thread":"t","status":"working"}"""))
        queue.accept(envelope("e2", """{"type":"mode","mode":"phone"}"""))
        assertEquals(emptyList(), queue.cards)
    }

    @Test
    fun processedMemoryIsBounded() {
        val queue = CardQueue(processedLimit = 2)
        queue.accept(envelope("e1", """{"type":"mode","mode":"pc"}"""))
        queue.accept(envelope("e2", """{"type":"mode","mode":"pc"}"""))
        queue.accept(envelope("e3", """{"type":"mode","mode":"pc"}"""))
        assertTrue(queue.accept(envelope("e1", """{"type":"mode","mode":"pc"}""")))
        assertFalse(queue.accept(envelope("e3", """{"type":"mode","mode":"pc"}""")))
    }

    @Test
    fun clearForgetsEverything() {
        val queue = CardQueue()
        queue.accept(permission("e1", "r1"))
        queue.clear()
        assertEquals(emptyList(), queue.cards)
        assertTrue(queue.accept(permission("e1", "r1")))
    }
}
