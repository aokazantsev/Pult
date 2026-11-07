package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.protocol.Outcome
import ru.aokazantsev.pult.protocol.message.ClosedOutcome
import ru.aokazantsev.pult.protocol.message.FeedPosition
import ru.aokazantsev.pult.protocol.message.PcMessage
import ru.aokazantsev.pult.protocol.message.PultMode
import ru.aokazantsev.pult.protocol.message.Request
import ru.aokazantsev.pult.protocol.message.ThreadInfo
import ru.aokazantsev.pult.protocol.message.ThreadStatus

class SessionModel {

    internal val threads = LinkedHashMap<String, ThreadInfo>()
    internal val cards = LinkedHashMap<String, RequestCard>()
    internal val feeds = FeedStore()
    private val drafts = HashMap<String, String>()
    private val unknownTypes = ArrayList<String>()

    var mode: PultMode? = null
        private set

    var snapshotApplied: Boolean = false
        private set

    fun apply(message: PcMessage) {
        when (message) {
            is PcMessage.ThreadUpdate -> applyThread(message.info)
            is PcMessage.Feed -> {
                ensureThread(message.thread, title = null)
                feeds.add(message.thread, message.entries)
            }
            is Request -> applyRequest(message)
            is PcMessage.Resolved -> cards[message.requestId]?.let { close(it, message.outcome) }
            is PcMessage.Mode -> mode = message.mode
            is PcMessage.Unknown -> rememberUnknown(message.type)
            is PcMessage.Snapshot, is PcMessage.Hello -> Unit
        }
    }

    fun applySnapshot(parts: List<PcMessage.Snapshot>) {
        mode = parts.first().mode
        val incoming = LinkedHashMap<String, ThreadInfo>()
        parts.forEach { part -> part.threads.forEach { incoming[it.thread] = it } }
        threads.clear()
        threads.putAll(incoming)
        feeds.retainOnly(incoming.keys)
        drafts.keys.retainAll(incoming.keys)
        cards.entries.removeAll { it.value.thread !in incoming }
        val openIds = HashSet<String>()
        parts.flatMap { it.requests }.forEach { request ->
            openIds.add(request.requestId)
            ensureThread(request.thread, request.title)
            val existing = cards[request.requestId]
            if (existing == null || existing.state == CardState.Missing) {
                cards[request.requestId] = (existing ?: newCard(request)).copy(state = CardState.Open, request = request)
            }
        }
        val closedIds = HashSet<String>()
        parts.flatMap { it.outcomes }.forEach { outcome ->
            if (outcome.thread !in incoming) return@forEach
            closedIds.add(outcome.requestId)
            val existing = cards[outcome.requestId]
            if (existing == null) {
                cards[outcome.requestId] = closedCard(outcome)
            } else if (existing.state != CardState.Closed) {
                close(existing, outcome.outcome)
            }
        }
        cards.replaceAll { id, card ->
            if (card.isWaiting && id !in openIds && id !in closedIds) card.copy(state = CardState.Missing) else card
        }
        snapshotApplied = true
    }

    fun markSending(requestId: String, text: String?) {
        val card = cards[requestId] ?: return
        if (card.state != CardState.Open) return
        cards[requestId] = card.copy(state = CardState.Sending, error = null, sentText = text ?: card.sentText)
        if (text != null) drafts.remove(card.thread)
    }

    fun markSent(requestId: String) {
        val card = cards[requestId] ?: return
        if (card.state == CardState.Sending) cards[requestId] = card.copy(state = CardState.Sent)
    }

    fun markFailed(requestId: String, error: String) {
        val card = cards[requestId] ?: return
        if (card.state != CardState.Sending) return
        cards[requestId] = card.copy(state = CardState.Open, error = error)
        val sent = card.sentText
        if (card.request is Request.Text && sent != null) drafts[card.thread] = sent
    }

    fun feedPositions(): List<FeedPosition> = threads.keys.map { FeedPosition(it, feeds.position(it) ?: 0L) }

    fun reset() {
        threads.clear()
        cards.clear()
        feeds.clear()
        drafts.clear()
        unknownTypes.clear()
        mode = null
        snapshotApplied = false
    }

    fun content(): SessionContent {
        val byThread = cards.values.groupBy { it.thread }
        return SessionContent(
            mode = mode,
            threads = ThreadTree.build(threads.values.toList(), cards.values.toList()),
            slices = threads.values.associate { it.thread to feeds.slice(it.thread, it.lastSeq) },
            cards = byThread,
            drafts = drafts.toMap(),
            unknownTypes = unknownTypes.toList(),
            snapshotApplied = snapshotApplied,
        )
    }

    private fun applyThread(info: ThreadInfo) {
        val previous = threads[info.thread]
        threads[info.thread] = info.copy(lastSeq = info.lastSeq ?: previous?.lastSeq)
    }

    private fun applyRequest(request: Request) {
        ensureThread(request.thread, request.title)
        val existing = cards[request.requestId]
        if (existing == null) {
            cards[request.requestId] = newCard(request)
        } else if (existing.state == CardState.Missing) {
            cards[request.requestId] = existing.copy(state = CardState.Open, request = request)
        }
    }

    private fun newCard(request: Request): RequestCard = RequestCard(
        requestId = request.requestId,
        thread = request.thread,
        afterSeq = request.afterSeq,
        kind = RequestSummary.kindOf(request),
        summary = RequestSummary.of(request),
        request = request,
        state = CardState.Open,
    )

    private fun closedCard(outcome: ClosedOutcome): RequestCard = RequestCard(
        requestId = outcome.requestId,
        thread = outcome.thread,
        afterSeq = outcome.afterSeq,
        kind = outcome.kind,
        summary = outcome.summary,
        request = null,
        state = CardState.Closed,
        outcome = outcome.outcome,
    )

    private fun close(card: RequestCard, outcome: Outcome) {
        if (card.state == CardState.Closed) return
        cards[card.requestId] = card.copy(state = CardState.Closed, outcome = outcome, error = null)
        val sent = card.sentText
        if (card.request !is Request.Text || sent == null) return
        if (outcome != Outcome.Answered) {
            drafts[card.thread] = sent
        } else if (drafts[card.thread] == sent) {
            drafts.remove(card.thread)
        }
    }

    private fun ensureThread(thread: String, title: String?) {
        if (thread in threads) return
        threads[thread] = ThreadInfo(
            thread = thread,
            title = RequestSummary.placeholderTitle(title),
            folder = "",
            status = ThreadStatus.Unknown,
            pcDialog = false,
        )
    }

    private fun rememberUnknown(type: String) {
        unknownTypes.add(type)
        if (unknownTypes.size > UNKNOWN_LIMIT) unknownTypes.removeAt(0)
    }

    private companion object {
        const val UNKNOWN_LIMIT = 20
    }
}
