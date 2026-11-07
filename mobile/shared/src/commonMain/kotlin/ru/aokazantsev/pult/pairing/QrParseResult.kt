package ru.aokazantsev.pult.pairing

sealed interface QrParseResult {
    data class Parsed(val payload: QrPayload) : QrParseResult

    data class Rejected(val reason: String) : QrParseResult
}
