package ru.aokazantsev.pult.protocol

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs

class InboundParserTest {

    private fun inner(json: String) = StrictJson.parseObject(json)

    @Test
    fun helloIsParsed() {
        assertEquals(
            InboundMessage.Hello(listOf(2L)),
            InboundParser.parseInner(inner("""{"type":"hello","protocol":[2],"capabilities":[]}""")),
        )
    }

    @Test
    fun permissionRequestIsParsed() {
        val message = InboundParser.parseInner(
            inner("""{"type":"permission_request","request_id":"r1","thread":"t","title":"T","tool":"Bash","summary":"ls","input":{"command":"ls"}}"""),
        )
        val permission = assertIs<InboundMessage.Permission>(message)
        assertEquals("r1", permission.requestId)
        assertEquals("Bash", permission.tool)
        assertEquals("""{"command":"ls"}""", permission.input)
    }

    @Test
    fun choiceRequestKeepsOptionsAndMulti() {
        val message = InboundParser.parseInner(
            inner("""{"type":"choice_request","request_id":"r2","question":"Q","options":[{"id":"a","label":"A"},{"id":"b"},{"label":"no id"}],"multi":true}"""),
        )
        val choice = assertIs<InboundMessage.Choice>(message)
        assertEquals(listOf(ChoiceOption("a", "A"), ChoiceOption("b", "b")), choice.options)
        assertEquals(true, choice.multi)
    }

    @Test
    fun textRequestIsParsed() {
        val message = InboundParser.parseInner(inner("""{"type":"text_request","request_id":"r3","last_message":"done"}"""))
        assertEquals("done", assertIs<InboundMessage.Text>(message).lastMessage)
    }

    @Test
    fun resolvedMapsOutcomes() {
        fun outcome(wire: String) = assertIs<InboundMessage.Resolved>(
            InboundParser.parseInner(inner("""{"type":"resolved","request_id":"r","outcome":"$wire"}""")),
        ).outcome
        assertEquals(Outcome.Answered, outcome("answered"))
        assertEquals(Outcome.Expired, outcome("expired"))
        assertEquals(Outcome.ResolvedOnPc, outcome("resolved_on_pc"))
        assertEquals(Outcome.Unknown, outcome("something_else"))
    }

    @Test
    fun requestWithoutRequestIdBecomesUnknown() {
        val message = InboundParser.parseInner(inner("""{"type":"permission_request","tool":"Bash"}"""))
        assertEquals(InboundMessage.Unknown("permission_request"), message)
    }

    @Test
    fun unknownTypeIsKept() {
        assertEquals(InboundMessage.Unknown("snapshot"), InboundParser.parseInner(inner("""{"type":"snapshot"}""")))
        assertEquals(InboundMessage.Unknown(""), InboundParser.parseInner(inner("""{"x":1}""")))
    }

    @Test
    fun statusAndModeAreParsed() {
        assertEquals(
            InboundMessage.Status("t", "working"),
            InboundParser.parseInner(inner("""{"type":"status","thread":"t","status":"working"}""")),
        )
        assertEquals(InboundMessage.Mode("pc"), InboundParser.parseInner(inner("""{"type":"mode","mode":"pc"}""")))
    }
}
