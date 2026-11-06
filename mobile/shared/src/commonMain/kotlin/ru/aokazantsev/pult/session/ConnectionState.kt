package ru.aokazantsev.pult.session

sealed interface ConnectionState {
    data object NotConfigured : ConnectionState
    data object Idle : ConnectionState
    data object Connecting : ConnectionState
    data object Connected : ConnectionState
    data class Disconnected(val reason: String) : ConnectionState
}
