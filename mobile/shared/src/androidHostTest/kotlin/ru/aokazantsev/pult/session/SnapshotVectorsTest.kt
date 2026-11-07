package ru.aokazantsev.pult.session

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import ru.aokazantsev.pult.protocol.JsonFields
import ru.aokazantsev.pult.protocol.Outcome
import ru.aokazantsev.pult.protocol.message.MessageParser
import ru.aokazantsev.pult.protocol.message.ParseResult
import ru.aokazantsev.pult.protocol.message.PcMessage
import ru.aokazantsev.pult.protocol.message.PultMode
import ru.aokazantsev.pult.protocol.message.RequestKind
import ru.aokazantsev.pult.protocol.message.ThreadInfo
import ru.aokazantsev.pult.protocol.message.ThreadStatus
import ru.aokazantsev.pult.vectors.Vectors

class SnapshotVectorsTest {

    private val cases = Vectors.load("messages").getValue("snapshot_cases").jsonArray

    @Test
    fun everyCaseProducesTheExpectedState() {
        cases.forEach { case ->
            val obj = case.jsonObject
            val name = JsonFields.string(obj, "name")!!
            val model = modelFrom(obj.getValue("before").jsonObject)
            val assembler = SnapshotAssembler()
            var completed: List<PcMessage.Snapshot>? = null
            obj.getValue("parts").jsonArray.forEach { part ->
                val parsed = assertIs<ParseResult.Accepted>(MessageParser.parseFromPc(part.jsonObject), name)
                val step = assembler.accept(assertIs<PcMessage.Snapshot>(parsed.message))
                if (step is SnapshotStep.Complete) completed = step.parts
            }
            completed?.let(model::applySnapshot)
            val actual: JsonElement = if (completed == null) JsonNull else stateOf(model)
            assertEquals(obj.getValue("after"), actual, name)
        }
    }

    private fun modelFrom(state: JsonObject): SessionModel {
        val model = SessionModel()
        model.apply(PcMessage.Mode(PultMode.fromWire(JsonFields.string(state, "mode"))))
        state.getValue("threads").jsonObject.forEach { (thread, lastSeq) ->
            model.threads[thread] = ThreadInfo(
                thread = thread,
                title = "t",
                folder = "",
                status = ThreadStatus.Working,
                pcDialog = false,
                lastSeq = JsonFields.integer(lastSeq),
            )
        }
        state.getValue("cards").jsonObject.forEach { (id, card) ->
            val fields = card.jsonObject
            val cardState = when (JsonFields.string(fields, "state")) {
                "open" -> CardState.Open
                "sending" -> CardState.Sending
                "sent" -> CardState.Sent
                "closed" -> CardState.Closed
                else -> CardState.Missing
            }
            model.cards[id] = RequestCard(
                requestId = id,
                thread = JsonFields.string(fields, "thread")!!,
                afterSeq = 0,
                kind = RequestKind.Permission,
                summary = "s",
                request = null,
                state = cardState,
                outcome = JsonFields.string(fields, "outcome")?.let { Outcome.fromWire(it) },
            )
        }
        return model
    }

    private fun stateOf(model: SessionModel): JsonObject = JsonObject(
        mapOf(
            "mode" to JsonPrimitive(model.mode?.wire.orEmpty()),
            "threads" to JsonObject(model.threads.mapValues { JsonPrimitive(it.value.lastSeq) }),
            "cards" to JsonObject(
                model.cards.mapValues { (_, card) ->
                    val fields = linkedMapOf<String, JsonElement>(
                        "thread" to JsonPrimitive(card.thread),
                        "state" to JsonPrimitive(card.state.name.lowercase()),
                    )
                    if (card.state == CardState.Closed) fields["outcome"] = JsonPrimitive(card.outcome?.wire)
                    JsonObject(fields)
                },
            ),
        ),
    )
}
