package ru.aokazantsev.pult.protocol

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import ru.aokazantsev.pult.protocol.message.ChoiceAnswer
import ru.aokazantsev.pult.protocol.message.FeedPosition

object OutboundMessages {

    const val PROTOCOL_VERSION = 2L

    const val CAPABILITY_CHOICE = "choice"

    const val TEXT_LIMIT = 8000

    fun permissionResponse(requestId: String, allow: Boolean, reason: String? = null): JsonObject {
        val fields = linkedMapOf(
            "type" to JsonPrimitive("permission_response"),
            "request_id" to JsonPrimitive(requestId),
            "decision" to JsonPrimitive(if (allow) "allow" else "deny"),
        )
        if (!reason.isNullOrBlank()) fields["reason"] = JsonPrimitive(reason)
        return JsonObject(fields)
    }

    fun choiceResponse(requestId: String, answers: List<ChoiceAnswer>): JsonObject = JsonObject(
        mapOf(
            "type" to JsonPrimitive("choice_response"),
            "request_id" to JsonPrimitive(requestId),
            "answers" to JsonArray(answers.map(::answer)),
        ),
    )

    fun textResponse(requestId: String, text: String): JsonObject = JsonObject(
        mapOf(
            "type" to JsonPrimitive("text_response"),
            "request_id" to JsonPrimitive(requestId),
            "text" to JsonPrimitive(text),
        ),
    )

    fun hello(feeds: List<FeedPosition>): JsonObject = JsonObject(
        mapOf(
            "type" to JsonPrimitive("hello"),
            "protocol" to JsonArray(listOf(JsonPrimitive(PROTOCOL_VERSION))),
            "capabilities" to JsonArray(listOf(JsonPrimitive(CAPABILITY_CHOICE))),
            "feeds" to JsonArray(
                feeds.map {
                    JsonObject(mapOf("thread" to JsonPrimitive(it.thread), "seq" to JsonPrimitive(it.seq)))
                },
            ),
        ),
    )

    private fun answer(answer: ChoiceAnswer): JsonObject {
        val fields = linkedMapOf(
            "question" to JsonPrimitive(answer.question),
            "selected" to JsonArray(answer.selected.map(::JsonPrimitive)),
        )
        answer.other?.let { fields["other"] = JsonPrimitive(it) }
        return JsonObject(fields)
    }
}
