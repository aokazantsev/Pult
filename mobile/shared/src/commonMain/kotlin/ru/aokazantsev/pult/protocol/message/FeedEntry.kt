package ru.aokazantsev.pult.protocol.message

sealed interface FeedEntry {
    val seq: Long
    val at: Long?
    val truncated: Boolean

    data class User(
        override val seq: Long,
        override val at: Long?,
        override val truncated: Boolean,
        val text: String,
        val viaPhone: Boolean,
        val requestId: String?,
    ) : FeedEntry

    data class Assistant(
        override val seq: Long,
        override val at: Long?,
        override val truncated: Boolean,
        val text: String,
    ) : FeedEntry

    data class ToolCall(
        override val seq: Long,
        override val at: Long?,
        override val truncated: Boolean,
        val call: String,
        val tool: String,
        val summary: String,
        val input: List<ToolArg>,
    ) : FeedEntry

    data class ToolResult(
        override val seq: Long,
        override val at: Long?,
        override val truncated: Boolean,
        val call: String,
        val output: String,
        val error: Boolean,
    ) : FeedEntry

    data class Peer(
        override val seq: Long,
        override val at: Long?,
        override val truncated: Boolean,
        val direction: PeerDirection?,
        val peerTitle: String,
        val text: String,
        val peerThread: String?,
    ) : FeedEntry

    data class Notice(
        override val seq: Long,
        override val at: Long?,
        override val truncated: Boolean,
        val text: String,
    ) : FeedEntry

    data class Unrecognized(
        override val seq: Long,
        override val at: Long?,
        override val truncated: Boolean,
        val count: Long,
    ) : FeedEntry
}
