package ru.aokazantsev.pult.protocol.message

sealed interface Request : PcMessage {
    val requestId: String
    val thread: String
    val title: String
    val afterSeq: Long

    data class Permission(
        override val requestId: String,
        override val thread: String,
        override val title: String,
        override val afterSeq: Long,
        val tool: String,
        val summary: String,
        val input: List<ToolArg>,
    ) : Request

    data class Choice(
        override val requestId: String,
        override val thread: String,
        override val title: String,
        override val afterSeq: Long,
        val answerable: Boolean,
        val questions: List<ChoiceQuestion>,
    ) : Request

    data class Text(
        override val requestId: String,
        override val thread: String,
        override val title: String,
        override val afterSeq: Long,
        val lastMessage: String,
    ) : Request
}
