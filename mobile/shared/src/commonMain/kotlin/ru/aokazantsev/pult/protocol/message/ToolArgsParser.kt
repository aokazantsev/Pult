package ru.aokazantsev.pult.protocol.message

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

internal object ToolArgsParser {

    fun parse(element: JsonElement): List<ToolArg> {
        val obj = element as? JsonObject ?: FieldReader.reject("input not an object")
        return obj.entries.map { (name, value) ->
            when (value) {
                is JsonArray, is JsonObject, JsonNull -> FieldReader.reject("input value not flat")
                is JsonPrimitive -> {
                    if (value.isString && ProtocolText.codePointLength(value.content) > VALUE_LIMIT) {
                        FieldReader.reject("input value too long")
                    }
                    ToolArg(name, value.content)
                }
            }
        }
    }

    private const val VALUE_LIMIT = 1000
}
