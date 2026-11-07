package ru.aokazantsev.pult.protocol.message

enum class RequestKind(val wire: String) {
    Permission("permission"),
    Choice("choice"),
    Text("text"),
    Other("");

    companion object {
        fun fromWire(value: String?): RequestKind = entries.firstOrNull { it.wire == value && it != Other } ?: Other
    }
}
