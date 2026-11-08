package ru.aokazantsev.pult.ui.thread

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import ru.aokazantsev.pult.session.ConnectionState

class SessionEndTextTest {

    @Test
    fun pairEndedShowsItsReason() {
        assertEquals("Сессию разорвали на ПК", sessionEndText(ConnectionState.PairEnded("Сессию разорвали на ПК")))
    }

    @Test
    fun incompatibleShowsItsReason() {
        assertEquals("версия", sessionEndText(ConnectionState.Incompatible("версия")))
    }

    @Test
    fun notPairedEndsThread() {
        assertEquals("Телефон не сопряжён с ПК", sessionEndText(ConnectionState.NotPaired))
    }

    @Test
    fun liveOrReconnectingSessionKeepsThread() {
        listOf(
            ConnectionState.Idle,
            ConnectionState.Connecting,
            ConnectionState.Connected,
            ConnectionState.Disconnected("сеть"),
        ).forEach { assertNull(sessionEndText(it)) }
    }
}
