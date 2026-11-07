package ru.aokazantsev.pult.vectors

import java.io.File
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject

object Vectors {

    fun load(name: String): JsonObject {
        val file = listOf("../../protocol/vectors/$name.json", "protocol/vectors/$name.json")
            .map(::File).first { it.exists() }
        return Json.parseToJsonElement(file.readText(Charsets.UTF_8)).jsonObject
    }

    fun hex(text: String): ByteArray = ByteArray(text.length / 2) { text.substring(it * 2, it * 2 + 2).toInt(16).toByte() }

    fun JsonElement.str(key: String): String = (jsonObject.getValue(key) as JsonPrimitive).content

    fun JsonElement.list(key: String): JsonArray = jsonObject.getValue(key).jsonArray

    fun JsonElement.objectText(key: String): String = jsonObject.getValue(key).toString()
}
