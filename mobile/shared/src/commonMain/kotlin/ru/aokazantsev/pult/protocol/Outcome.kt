package ru.aokazantsev.pult.protocol

enum class Outcome(val wire: String) {
    Answered("answered"),
    Expired("expired"),
    ResolvedOnPc("resolved_on_pc"),
    Unknown("unknown");

    companion object {
        fun fromWire(value: String?): Outcome = entries.firstOrNull { it.wire == value } ?: Unknown
    }
}
