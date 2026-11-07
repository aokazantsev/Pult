package ru.aokazantsev.pult.protocol

import kotlinx.serialization.ExperimentalSerializationApi
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.JsonUnquotedLiteral

object StrictJson {

    fun parse(text: String): JsonElement = Reader(text).readDocument()

    fun parseObject(text: String): JsonObject =
        parse(text) as? JsonObject ?: throw StrictJsonException("not an object")

    fun parseObjectOrNull(text: String): JsonObject? = try {
        parseObject(text)
    } catch (_: StrictJsonException) {
        null
    }

    private const val MAX_DEPTH = 32

    @OptIn(ExperimentalSerializationApi::class)
    private class Reader(private val text: String) {
        private var position = 0

        fun readDocument(): JsonElement {
            val value = readValue(0)
            skipSpace()
            if (position != text.length) fail("trailing data")
            return value
        }

        private fun readValue(depth: Int): JsonElement {
            if (depth > MAX_DEPTH) fail("too deep")
            skipSpace()
            if (position >= text.length) fail("unexpected end")
            return when (val c = text[position]) {
                '{' -> readObject(depth)
                '[' -> readArray(depth)
                '"' -> JsonPrimitive(readString())
                't' -> literal("true", JsonUnquotedLiteral("true"))
                'f' -> literal("false", JsonUnquotedLiteral("false"))
                'n' -> literal("null", JsonNull)
                else -> if (c == '-' || c in '0'..'9') readNumber() else fail("unexpected character")
            }
        }

        private fun literal(word: String, value: JsonElement): JsonElement {
            if (!text.startsWith(word, position)) fail("bad literal")
            position += word.length
            return value
        }

        private fun readObject(depth: Int): JsonObject {
            position++
            val map = LinkedHashMap<String, JsonElement>()
            skipSpace()
            if (peek() == '}') {
                position++
                return JsonObject(map)
            }
            while (true) {
                skipSpace()
                if (peek() != '"') fail("key expected")
                val key = readString()
                if (key in map) fail("duplicate key")
                skipSpace()
                if (peek() != ':') fail("colon expected")
                position++
                map[key] = readValue(depth + 1)
                skipSpace()
                when (peek()) {
                    ',' -> position++
                    '}' -> {
                        position++
                        return JsonObject(map)
                    }
                    else -> fail("comma or brace expected")
                }
            }
        }

        private fun readArray(depth: Int): JsonArray {
            position++
            val items = ArrayList<JsonElement>()
            skipSpace()
            if (peek() == ']') {
                position++
                return JsonArray(items)
            }
            while (true) {
                items.add(readValue(depth + 1))
                skipSpace()
                when (peek()) {
                    ',' -> position++
                    ']' -> {
                        position++
                        return JsonArray(items)
                    }
                    else -> fail("comma or bracket expected")
                }
            }
        }

        private fun readString(): String {
            position++
            val out = StringBuilder()
            while (true) {
                if (position >= text.length) fail("unterminated string")
                val c = text[position++]
                when {
                    c == '"' -> return out.toString()
                    c == '\\' -> out.append(readEscape())
                    c < ' ' -> fail("control character in string")
                    else -> out.append(c)
                }
            }
        }

        private fun readEscape(): Char {
            if (position >= text.length) fail("unterminated escape")
            return when (text[position++]) {
                '"' -> '"'
                '\\' -> '\\'
                '/' -> '/'
                'b' -> '\b'
                'f' -> '\u000C'
                'n' -> '\n'
                'r' -> '\r'
                't' -> '\t'
                'u' -> {
                    if (position + 4 > text.length) fail("bad unicode escape")
                    val code = text.substring(position, position + 4).toIntOrNull(16) ?: fail("bad unicode escape")
                    position += 4
                    code.toChar()
                }
                else -> fail("bad escape")
            }
        }

        private fun readNumber(): JsonElement {
            val start = position
            if (text[position] == '-') position++
            if (position >= text.length) fail("bad number")
            if (text[position] == '0') {
                position++
            } else if (text[position] in '1'..'9') {
                digits()
            } else {
                fail("bad number")
            }
            if (position < text.length && text[position] == '.') {
                position++
                if (digits() == 0) fail("bad number")
            }
            if (position < text.length && (text[position] == 'e' || text[position] == 'E')) {
                position++
                if (position < text.length && (text[position] == '+' || text[position] == '-')) position++
                if (digits() == 0) fail("bad number")
            }
            return JsonUnquotedLiteral(text.substring(start, position))
        }

        private fun digits(): Int {
            val start = position
            while (position < text.length && text[position] in '0'..'9') position++
            return position - start
        }

        private fun peek(): Char = if (position < text.length) text[position] else fail("unexpected end")

        private fun skipSpace() {
            while (position < text.length && text[position].let { it == ' ' || it == '\n' || it == '\r' || it == '\t' }) {
                position++
            }
        }

        private fun fail(message: String): Nothing = throw StrictJsonException("$message at $position")
    }
}
