package ru.aokazantsev.pult.protocol

sealed interface InboundMessage {

    sealed interface Request : InboundMessage {
        val requestId: String
        val thread: String
        val title: String
    }

    data class Permission(
        override val requestId: String,
        override val thread: String,
        override val title: String,
        val tool: String,
        val summary: String,
        val input: String,
    ) : Request

    data class Choice(
        override val requestId: String,
        override val thread: String,
        override val title: String,
        val question: String,
        val options: List<ChoiceOption>,
        val multi: Boolean,
    ) : Request

    data class Text(
        override val requestId: String,
        override val thread: String,
        override val title: String,
        val lastMessage: String,
    ) : Request

    data class Resolved(val requestId: String, val outcome: Outcome) : InboundMessage

    data class Status(val thread: String, val status: String) : InboundMessage

    data class Mode(val mode: String) : InboundMessage

    data class Hello(val protocol: List<Long>) : InboundMessage

    data class Unknown(val type: String) : InboundMessage
}
