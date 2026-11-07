package ru.aokazantsev.pult.protocol.message

enum class PeerDirection(val wire: String) {
    In("in"),
    Out("out");

    companion object {
        fun fromWire(value: String?): PeerDirection? = entries.firstOrNull { it.wire == value }
    }
}
