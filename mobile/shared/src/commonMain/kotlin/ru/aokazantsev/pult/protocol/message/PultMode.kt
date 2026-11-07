package ru.aokazantsev.pult.protocol.message

enum class PultMode(val wire: String) {
    Phone("phone"),
    Pc("pc"),
    Unknown("");

    companion object {
        fun fromWire(value: String?): PultMode = entries.firstOrNull { it.wire == value && it != Unknown } ?: Unknown
    }
}
