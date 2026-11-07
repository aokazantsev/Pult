package ru.aokazantsev.pult.protocol.message

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertTrue
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import ru.aokazantsev.pult.protocol.JsonFields
import ru.aokazantsev.pult.protocol.OutboundMessages
import ru.aokazantsev.pult.protocol.StrictJson
import ru.aokazantsev.pult.vectors.Vectors
import ru.aokazantsev.pult.vectors.Vectors.list
import ru.aokazantsev.pult.vectors.Vectors.str

class MessageVectorsTest {

    private val vectors = Vectors.load("messages")

    @Test
    fun everyValidMessageForThePhoneIsAccepted() {
        val cases = vectors.list("valid").filter { it.str("to") == "phone" }
        assertTrue(cases.isNotEmpty())
        cases.forEach { case ->
            val result = MessageParser.parseFromPc(case.jsonObject.getValue("message").jsonObject)
            assertIs<ParseResult.Accepted>(result, "${case.str("name")}: $result")
        }
    }

    @Test
    fun unknownTypeBecomesUnknownMessage() {
        val case = vectors.list("valid").first { it.str("name") == "unknown type is a card, not a reject" }
        val accepted = assertIs<ParseResult.Accepted>(MessageParser.parseFromPc(case.jsonObject.getValue("message").jsonObject))
        assertIs<PcMessage.Unknown>(accepted.message)
    }

    @Test
    fun everyRejectCaseForThePhoneIsRejected() {
        vectors.list("reject").filter { it.str("to") == "phone" }.forEach { case ->
            val obj = case.jsonObject
            val message = obj["text"]?.let { StrictJson.parseObjectOrNull((it as JsonPrimitive).content) }
                ?: obj["message"]?.jsonObject
            if (message != null) {
                assertIs<ParseResult.Rejected>(MessageParser.parseFromPc(message), case.str("name"))
            } else {
                assertTrue(obj.containsKey("text"), case.str("name"))
            }
        }
    }

    @Test
    fun threadDescriptionKeepsAllFields() {
        val message = validMessage("thread child")
        val thread = assertIs<PcMessage.ThreadUpdate>(message).info
        assertEquals("t2", thread.thread)
        assertTrue(thread.parent != null)
    }

    @Test
    fun feedEveryKindIsParsed() {
        val feed = assertIs<PcMessage.Feed>(validMessage("feed every kind"))
        val kinds = feed.entries.map { it::class.simpleName }.toSet()
        assertEquals(
            setOf("User", "Assistant", "ToolCall", "ToolResult", "Peer", "Notice", "Unrecognized"),
            kinds,
        )
    }

    @Test
    fun futureValuesAreKeptWithoutRejecting() {
        val future = assertIs<PcMessage.ThreadUpdate>(validMessage("thread future status")).info
        assertEquals(ThreadStatus.Unknown, future.status)
        val outcome = assertIs<PcMessage.Resolved>(validMessage("resolved future outcome"))
        assertEquals(ru.aokazantsev.pult.protocol.Outcome.Unknown, outcome.outcome)
        val peer = assertIs<PcMessage.Feed>(validMessage("peer with unknown direction is kept")).entries.single()
        assertEquals(null, assertIs<FeedEntry.Peer>(peer).direction)
    }

    @Test
    fun phoneMessagesAreBuiltEqualToTheVectors() {
        vectors.list("valid").filter { it.str("to") == "pc" }.forEach { case ->
            val expected = case.jsonObject.getValue("message").jsonObject
            val built = when (JsonFields.string(expected, "type")) {
                "permission_response" -> OutboundMessages.permissionResponse(
                    requestId = JsonFields.string(expected, "request_id")!!,
                    allow = JsonFields.string(expected, "decision") == "allow",
                    reason = JsonFields.string(expected, "reason"),
                )
                "choice_response" -> OutboundMessages.choiceResponse(
                    requestId = JsonFields.string(expected, "request_id")!!,
                    answers = answersOf(expected.getValue("answers").jsonArray),
                )
                "text_response" -> OutboundMessages.textResponse(
                    requestId = JsonFields.string(expected, "request_id")!!,
                    text = JsonFields.string(expected, "text")!!,
                )
                "hello" -> OutboundMessages.hello(
                    expected.getValue("feeds").jsonArray.map {
                        FeedPosition(JsonFields.string(it.jsonObject, "thread")!!, JsonFields.integer(it.jsonObject["seq"])!!)
                    },
                )
                else -> error("unexpected type in ${case.str("name")}")
            }
            assertEquals(expected, built, case.str("name"))
        }
    }

    private fun answersOf(array: JsonArray): List<ChoiceAnswer> = array.map { element ->
        val obj = element.jsonObject
        ChoiceAnswer(
            question = JsonFields.string(obj, "question")!!,
            selected = obj.getValue("selected").jsonArray.map { (it as JsonPrimitive).content },
            other = JsonFields.string(obj, "other"),
        )
    }

    private fun validMessage(name: String): PcMessage {
        val case = vectors.list("valid").first { it.str("name") == name }
        val obj: JsonObject = case.jsonObject.getValue("message").jsonObject
        return assertIs<ParseResult.Accepted>(MessageParser.parseFromPc(obj), name).message
    }
}
