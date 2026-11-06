package ru.aokazantsev.pult.protocol

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class SseParserTest {

    private fun feedAll(parser: SseParser, vararg lines: String): List<SseEvent> = lines.mapNotNull(parser::feed)

    @Test
    fun messageEventIsDispatchedOnBlankLine() {
        val events = feedAll(SseParser(), "id: 17", "event: message", "data: {\"a\":1}", "")
        assertEquals(listOf(SseEvent("17", "message", "{\"a\":1}")), events)
    }

    @Test
    fun keepaliveCommentsAreIgnored() {
        val events = feedAll(SseParser(), ": keepalive", "", ": keepalive", "")
        assertEquals(emptyList(), events)
    }

    @Test
    fun eventWithoutNameIsMessage() {
        assertEquals("message", feedAll(SseParser(), "data: x", "").single().event)
    }

    @Test
    fun serverEventKeepsName() {
        val event = feedAll(SseParser(), "event: server", "data: {\"state\":\"open\"}", "").single()
        assertEquals("server", event.event)
        assertNull(event.id)
    }

    @Test
    fun multilineDataIsJoined() {
        assertEquals("a\nb", feedAll(SseParser(), "data: a", "data: b", "").single().data)
    }

    @Test
    fun stateIsResetBetweenEvents() {
        val events = feedAll(SseParser(), "id: 1", "event: server", "data: x", "", "data: y", "")
        assertEquals(SseEvent(null, "message", "y"), events[1])
    }
}
