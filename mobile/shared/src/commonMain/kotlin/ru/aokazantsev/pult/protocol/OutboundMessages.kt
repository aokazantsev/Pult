package ru.aokazantsev.pult.protocol

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

object OutboundMessages {

    const val PROTOCOL_VERSION = 2L

    fun permissionResponse(requestId: String, allow: Boolean, reason: String = ""): JsonObject = JsonObject(
        mapOf(
            "type" to JsonPrimitive("permission_response"),
            "request_id" to JsonPrimitive(requestId),
            "decision" to JsonPrimitive(if (allow) "allow" else "deny"),
            "reason" to JsonPrimitive(reason),
        ),
    )

    fun choiceResponse(requestId: String, selected: List<String>): JsonObject = JsonObject(
        mapOf(
            "type" to JsonPrimitive("choice_response"),
            "request_id" to JsonPrimitive(requestId),
            "selected" to JsonArray(selected.map(::JsonPrimitive)),
        ),
    )

    fun textResponse(requestId: String, text: String): JsonObject = JsonObject(
        mapOf(
            "type" to JsonPrimitive("text_response"),
            "request_id" to JsonPrimitive(requestId),
            "text" to JsonPrimitive(text),
        ),
    )

    fun hello(): JsonObject = JsonObject(
        mapOf(
            "type" to JsonPrimitive("hello"),
            "protocol" to JsonArray(listOf(JsonPrimitive(PROTOCOL_VERSION))),
            "capabilities" to JsonArray(emptyList()),
        ),
    )
}
