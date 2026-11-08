package ru.aokazantsev.pult.ui.thread

import ru.aokazantsev.pult.session.ConnectionState

fun sessionEndText(connection: ConnectionState): String? = when (connection) {
    is ConnectionState.PairEnded -> connection.reason
    is ConnectionState.Incompatible -> connection.reason
    ConnectionState.NotPaired -> "Телефон не сопряжён с ПК"
    else -> null
}
