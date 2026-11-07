package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.protocol.message.PultMode

data class SessionContent(
    val mode: PultMode? = null,
    val threads: List<ThreadRow> = emptyList(),
    val slices: Map<String, FeedSlice> = emptyMap(),
    val cards: Map<String, List<RequestCard>> = emptyMap(),
    val drafts: Map<String, String> = emptyMap(),
    val unknownTypes: List<String> = emptyList(),
    val snapshotApplied: Boolean = false,
) {

    fun row(thread: String): ThreadRow? = threads.firstOrNull { it.info.thread == thread }

    fun timeline(thread: String): List<TimelineItem> =
        TimelineBuilder.build(slices[thread] ?: FeedSlice.Empty, cards[thread].orEmpty())
}
