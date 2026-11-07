package ru.aokazantsev.pult.protocol.message

import ru.aokazantsev.pult.protocol.Outcome

sealed interface PcMessage {

    data class ThreadUpdate(val info: ThreadInfo) : PcMessage

    data class Feed(val thread: String, val entries: List<FeedEntry>) : PcMessage

    data class Resolved(val requestId: String, val outcome: Outcome) : PcMessage

    data class Mode(val mode: PultMode) : PcMessage

    data class Hello(val protocol: List<Long>, val capabilities: List<String>) : PcMessage

    data class Snapshot(
        val snapshot: String,
        val part: Long,
        val last: Boolean,
        val mode: PultMode?,
        val threads: List<ThreadInfo>,
        val requests: List<Request>,
        val outcomes: List<ClosedOutcome>,
    ) : PcMessage

    data class Unknown(val type: String) : PcMessage
}
