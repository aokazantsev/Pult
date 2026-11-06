package ru.aokazantsev.pult.protocol

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull

object InboundParser {

    fun parseEnvelope(data: String): Envelope? {
        val root = runCatching { Json.parseToJsonElement(data) as? JsonObject }.getOrNull() ?: return null
        val id = root.string("id") ?: return null
        val inner = root["inner"] as? JsonObject ?: return null
        return Envelope(
            id = id,
            from = root.string("from").orEmpty(),
            ttl = (root["ttl"] as? JsonPrimitive)?.intOrNull ?: 0,
            inner = inner,
        )
    }

    fun parseInner(inner: JsonObject): InboundMessage {
        val type = inner.string("type") ?: return InboundMessage.Unknown(type = "")
        return when (type) {
            "permission_request" -> permission(inner)
            "choice_request" -> choice(inner)
            "text_request" -> text(inner)
            "resolved" -> resolved(inner)
            "status" -> status(inner)
            "mode" -> inner.string("mode")?.let(InboundMessage::Mode)
            else -> null
        } ?: InboundMessage.Unknown(type)
    }

    private fun permission(inner: JsonObject): InboundMessage? {
        val requestId = inner.string("request_id") ?: return null
        return InboundMessage.Permission(
            requestId = requestId,
            thread = inner.string("thread").orEmpty(),
            title = inner.string("title").orEmpty(),
            tool = inner.string("tool").orEmpty(),
            summary = inner.string("summary").orEmpty(),
            input = inner["input"].asDisplayText(),
        )
    }

    private fun choice(inner: JsonObject): InboundMessage? {
        val requestId = inner.string("request_id") ?: return null
        val options = (inner["options"] as? JsonArray).orEmpty().mapNotNull { element ->
            val option = element as? JsonObject ?: return@mapNotNull null
            val id = option.string("id") ?: return@mapNotNull null
            ChoiceOption(id = id, label = option.string("label") ?: id)
        }
        return InboundMessage.Choice(
            requestId = requestId,
            thread = inner.string("thread").orEmpty(),
            title = inner.string("title").orEmpty(),
            question = inner.string("question").orEmpty(),
            options = options,
            multi = (inner["multi"] as? JsonPrimitive)?.booleanOrNull ?: false,
        )
    }

    private fun text(inner: JsonObject): InboundMessage? {
        val requestId = inner.string("request_id") ?: return null
        return InboundMessage.Text(
            requestId = requestId,
            thread = inner.string("thread").orEmpty(),
            title = inner.string("title").orEmpty(),
            lastMessage = inner.string("last_message").orEmpty(),
        )
    }

    private fun resolved(inner: JsonObject): InboundMessage? {
        val requestId = inner.string("request_id") ?: return null
        return InboundMessage.Resolved(requestId = requestId, outcome = Outcome.fromWire(inner.string("outcome")))
    }

    private fun status(inner: JsonObject): InboundMessage? {
        val thread = inner.string("thread") ?: return null
        return InboundMessage.Status(thread = thread, status = inner.string("status").orEmpty())
    }

    private fun JsonObject.string(key: String): String? = (this[key] as? JsonPrimitive)?.contentOrNull

    private fun JsonElement?.asDisplayText(): String = when (this) {
        null -> ""
        is JsonPrimitive -> contentOrNull.orEmpty()
        else -> toString()
    }
}
