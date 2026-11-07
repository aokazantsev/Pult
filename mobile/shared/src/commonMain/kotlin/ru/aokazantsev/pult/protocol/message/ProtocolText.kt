package ru.aokazantsev.pult.protocol.message

import kotlinx.serialization.json.JsonElement

object ProtocolText {

    private val whiteSpace: Set<Char> = buildSet {
        addAll('\u0009'..'\u000D')
        add(' ')
        add('\u0085')
        add(' ')
        add(' ')
        addAll(' '..' ')
        add(' ')
        add(' ')
        add(' ')
        add(' ')
        add('　')
    }

    fun isWhiteSpace(char: Char): Boolean = char in whiteSpace

    fun isBlank(value: String): Boolean = value.all(::isWhiteSpace)

    fun codePointLength(value: String): Int {
        var count = 0
        var index = 0
        while (index < value.length) {
            val char = value[index]
            index += if (char.isHighSurrogate() && index + 1 < value.length && value[index + 1].isLowSurrogate()) 2 else 1
            count++
        }
        return count
    }

    fun takeCodePoints(value: String, limit: Int): String {
        var count = 0
        var index = 0
        while (index < value.length && count < limit) {
            val char = value[index]
            index += if (char.isHighSurrogate() && index + 1 < value.length && value[index + 1].isLowSurrogate()) 2 else 1
            count++
        }
        return value.substring(0, index)
    }

    fun serializedBytes(element: JsonElement): Int = element.toString().encodeToByteArray().size
}
