package ru.aokazantsev.pult.session

sealed interface ConnectionState {
    data object NotPaired : ConnectionState
    data object Idle : ConnectionState
    data object Connecting : ConnectionState
    data object Connected : ConnectionState
    data class Disconnected(val reason: String) : ConnectionState
    data class Incompatible(val reason: String) : ConnectionState
    data class PairEnded(val reason: String) : ConnectionState
}
