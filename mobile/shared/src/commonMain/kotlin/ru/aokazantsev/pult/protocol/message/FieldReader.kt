package ru.aokazantsev.pult.protocol.message

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import ru.aokazantsev.pult.protocol.JsonFields

internal class FieldReader(private val obj: JsonObject) {

    fun has(key: String): Boolean = key in obj

    fun raw(key: String): JsonElement? = obj[key]

    fun ident(key: String): String = identOrNull(key) ?: reject("missing $key")

    fun identOrNull(key: String): String? {
        val element = obj[key] ?: return null
        val value = JsonFields.string(element)
        if (value == null || !JsonFields.isIdentifier(value)) reject("bad id $key")
        return value
    }

    fun text(key: String, limit: Int, nonEmpty: Boolean = false): String =
        textOrNull(key, limit, nonEmpty) ?: reject("missing $key")

    fun textOrNull(key: String, limit: Int, nonEmpty: Boolean = false): String? {
        val element = obj[key] ?: return null
        val value = JsonFields.string(element) ?: reject("not a string $key")
        if (ProtocolText.codePointLength(value) > limit) reject("too long $key")
        if (nonEmpty && ProtocolText.isBlank(value)) reject("empty $key")
        return value
    }

    fun integer(key: String, minimum: Long = 0): Long = integerOrNull(key, minimum) ?: reject("missing $key")

    fun integerOrNull(key: String, minimum: Long = 0): Long? {
        val element = obj[key] ?: return null
        val value = JsonFields.integer(element) ?: reject("bad int $key")
        if (value < minimum) reject("bad int $key")
        return value
    }

    fun boolean(key: String): Boolean = booleanOrNull(key) ?: reject("missing $key")

    fun booleanOrNull(key: String): Boolean? {
        val element = obj[key] ?: return null
        val primitive = element as? JsonPrimitive
        if (primitive == null || primitive.isString) reject("not bool $key")
        return when (primitive.content) {
            "true" -> true
            "false" -> false
            else -> reject("not bool $key")
        }
    }

    fun array(key: String, minimum: Int = 0, maximum: Int = Int.MAX_VALUE): JsonArray =
        arrayOrNull(key, minimum, maximum) ?: reject("missing $key")

    fun arrayOrNull(key: String, minimum: Int = 0, maximum: Int = Int.MAX_VALUE): JsonArray? {
        val element = obj[key] ?: return null
        val array = element as? JsonArray ?: reject("bad array $key")
        if (array.size < minimum || array.size > maximum) reject("bad array $key")
        return array
    }

    fun objectOrNull(key: String): FieldReader? {
        val element = obj[key] ?: return null
        return item(element, key)
    }

    companion object {

        const val ITEM_BYTES = 16384

        fun item(element: JsonElement, label: String): FieldReader {
            val value = element as? JsonObject ?: reject("not an object $label")
            if (ProtocolText.serializedBytes(value) > ITEM_BYTES) reject("item too large $label")
            return FieldReader(value)
        }

        fun reject(reason: String): Nothing = throw MessageRejected(reason)

        fun rejectNulls(element: JsonElement) {
            when (element) {
                is JsonNull -> reject("null")
                is JsonObject -> element.values.forEach(::rejectNulls)
                is JsonArray -> element.forEach(::rejectNulls)
                else -> Unit
            }
        }
    }
}
