package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.protocol.Outcome
import ru.aokazantsev.pult.protocol.message.Request
import ru.aokazantsev.pult.protocol.message.RequestKind

data class RequestCard(
    val requestId: String,
    val thread: String,
    val afterSeq: Long,
    val kind: RequestKind,
    val summary: String,
    val request: Request?,
    val state: CardState,
    val outcome: Outcome? = null,
    val error: String? = null,
    val sentText: String? = null,
) {

    val isWaiting: Boolean
        get() = state == CardState.Open || state == CardState.Sending || state == CardState.Sent

    val awaitsAction: Boolean
        get() = isWaiting && when (val current = request) {
            is Request.Permission -> true
            is Request.Choice -> current.answerable
            is Request.Text, null -> false
        }

    val awaitsText: Boolean
        get() = isWaiting && request is Request.Text
}
