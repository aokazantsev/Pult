package ru.aokazantsev.pult.protocol.message

enum class ThreadStatus(val wire: String) {
    Unknown("unknown"),
    Working("working"),
    Done("done"),
    Error("error"),
    Ended("ended");

    companion object {
        fun fromWire(value: String?): ThreadStatus = entries.firstOrNull { it.wire == value } ?: Unknown
    }
}
