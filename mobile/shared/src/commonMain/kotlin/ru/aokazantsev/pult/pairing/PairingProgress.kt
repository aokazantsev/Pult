package ru.aokazantsev.pult.pairing

sealed interface PairingProgress {
    data object Idle : PairingProgress

    data class Working(val step: String) : PairingProgress

    data class Failed(val reason: String) : PairingProgress
}
