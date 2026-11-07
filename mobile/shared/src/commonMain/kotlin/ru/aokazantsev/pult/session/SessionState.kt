package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.pairing.PairInfo

data class SessionState(
    val connection: ConnectionState = ConnectionState.Idle,
    val content: SessionContent = SessionContent(),
    val pairInfo: PairInfo? = null,
    val dropped: Int = 0,
    val notice: String? = null,
)
