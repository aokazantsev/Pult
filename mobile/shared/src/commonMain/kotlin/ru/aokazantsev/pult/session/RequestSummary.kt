package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.protocol.message.ProtocolText
import ru.aokazantsev.pult.protocol.message.Request
import ru.aokazantsev.pult.protocol.message.RequestKind

object RequestSummary {

    private const val LIMIT = 120

    fun of(request: Request): String {
        val text = when (request) {
            is Request.Permission -> "${request.tool}: ${request.summary}"
            is Request.Choice -> request.questions.first().question
            is Request.Text -> request.lastMessage
        }
        return ProtocolText.takeCodePoints(text.replace('\n', ' '), LIMIT)
    }

    fun kindOf(request: Request): RequestKind = when (request) {
        is Request.Permission -> RequestKind.Permission
        is Request.Choice -> RequestKind.Choice
        is Request.Text -> RequestKind.Text
    }

    fun placeholderTitle(title: String?): String =
        title?.takeIf { it.isNotEmpty() && !ProtocolText.isBlank(it) } ?: "…"
}
