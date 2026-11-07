package ru.aokazantsev.pult.protocol

sealed interface OpenResult {
    data class Opened(val envelope: Envelope) : OpenResult

    data class Incompatible(val version: Long) : OpenResult

    data class Invalid(val reason: String) : OpenResult
}
