package ru.aokazantsev.pult.protocol.message

sealed interface ParseResult {
    data class Accepted(val message: PcMessage) : ParseResult

    data class Rejected(val reason: String) : ParseResult
}
