package ru.aokazantsev.pult.protocol

import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

object JsonFields {

    const val MAX_SAFE_INTEGER = 9007199254740991L

    private val identifier = Regex("[A-Za-z0-9_-]{1,64}")

    fun isIdentifier(value: String): Boolean = identifier.matches(value)

    fun canonicalInteger(element: JsonElement?): String? {
        val primitive = element as? JsonPrimitive ?: return null
        if (primitive.isString) return null
        val content = primitive.content
        if (content.isEmpty() || content.length > 16) return null
        if (content != "0" && content[0] !in '1'..'9') return null
        if (!content.all { it in '0'..'9' }) return null
        val value = content.toLongOrNull() ?: return null
        return if (value <= MAX_SAFE_INTEGER) content else null
    }

    fun integer(element: JsonElement?): Long? = canonicalInteger(element)?.toLong()

    fun string(element: JsonElement?): String? {
        val primitive = element as? JsonPrimitive ?: return null
        return if (primitive.isString) primitive.content else null
    }

    fun string(obj: JsonObject, key: String): String? = string(obj[key])
}
