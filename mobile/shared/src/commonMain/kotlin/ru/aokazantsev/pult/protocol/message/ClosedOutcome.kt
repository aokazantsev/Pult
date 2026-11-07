package ru.aokazantsev.pult.protocol.message

import ru.aokazantsev.pult.protocol.Outcome

data class ClosedOutcome(
    val requestId: String,
    val kind: RequestKind,
    val thread: String,
    val afterSeq: Long,
    val summary: String,
    val outcome: Outcome,
)
