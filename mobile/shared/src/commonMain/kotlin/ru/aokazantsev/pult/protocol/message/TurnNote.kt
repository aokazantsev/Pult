package ru.aokazantsev.pult.protocol.message

enum class TurnNote(val wire: String) {
    ContinuationCap("continuation_cap");

    companion object {
        fun fromWire(value: String?): TurnNote? = entries.firstOrNull { it.wire == value }
    }
}
