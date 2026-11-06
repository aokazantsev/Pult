package ru.aokazantsev.pult.session

data class SessionState(
    val connection: ConnectionState = ConnectionState.Idle,
    val cards: List<RequestCard> = emptyList(),
)
