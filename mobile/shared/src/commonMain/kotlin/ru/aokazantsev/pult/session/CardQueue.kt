package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.protocol.Envelope
import ru.aokazantsev.pult.protocol.InboundMessage
import ru.aokazantsev.pult.protocol.InboundParser

class CardQueue(private val processedLimit: Int = PROCESSED_LIMIT) {

    private val processedIds = LinkedHashSet<String>()

    var cards: List<RequestCard> = emptyList()
        private set

    fun accept(envelope: Envelope): Boolean {
        if (!processedIds.add(envelope.id)) return false
        if (processedIds.size > processedLimit) processedIds.remove(processedIds.first())
        when (val message = InboundParser.parseInner(envelope.inner)) {
            is InboundMessage.Request -> addRequest(message.requestId, message)
            is InboundMessage.Unknown -> addRequest("unknown:${envelope.id}", message)
            is InboundMessage.Resolved -> close(message)
            is InboundMessage.Status, is InboundMessage.Mode -> Unit
        }
        return true
    }

    fun markSending(key: String) = update(key) { it.copy(phase = CardPhase.Sending, error = null) }

    fun markSent(key: String) = update(key) {
        if (it.phase == CardPhase.Closed) it else it.copy(phase = CardPhase.Sent)
    }

    fun markFailed(key: String, error: String) = update(key) {
        if (it.phase == CardPhase.Closed) it else it.copy(phase = CardPhase.Pending, error = error)
    }

    fun clear() {
        cards = emptyList()
        processedIds.clear()
    }

    private fun addRequest(key: String, message: InboundMessage) {
        if (cards.any { it.key == key }) return
        cards = cards + RequestCard(key = key, message = message)
    }

    private fun close(resolved: InboundMessage.Resolved) = update(resolved.requestId) {
        if (it.phase == CardPhase.Closed) it else it.copy(phase = CardPhase.Closed, outcome = resolved.outcome, error = null)
    }

    private fun update(key: String, transform: (RequestCard) -> RequestCard) {
        cards = cards.map { if (it.key == key) transform(it) else it }
    }

    private companion object {
        const val PROCESSED_LIMIT = 2000
    }
}
