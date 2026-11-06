package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.protocol.InboundMessage
import ru.aokazantsev.pult.protocol.Outcome

data class RequestCard(
    val key: String,
    val message: InboundMessage,
    val phase: CardPhase = CardPhase.Pending,
    val outcome: Outcome? = null,
    val error: String? = null,
)

enum class CardPhase {
    Pending,
    Sending,
    Sent,
    Closed,
}
