package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.protocol.message.FeedEntry

sealed interface TimelineItem {
    val key: String

    data class Entry(val entry: FeedEntry) : TimelineItem {
        override val key: String = "e${entry.seq}"
    }

    data class ToolStep(val call: FeedEntry.ToolCall, val result: FeedEntry.ToolResult?) : TimelineItem {
        override val key: String = "e${call.seq}"
    }

    data class Unrecognized(val firstSeq: Long, val count: Long) : TimelineItem {
        override val key: String = "e$firstSeq"
    }

    data class Gap(val after: Long, val missing: Long) : TimelineItem {
        override val key: String = "gap$after"
    }

    data object EarlyMissing : TimelineItem {
        override val key: String = "early"
    }

    data object LateMissing : TimelineItem {
        override val key: String = "late"
    }

    data class Card(val card: RequestCard) : TimelineItem {
        override val key: String = "card${card.requestId}"
    }
}
