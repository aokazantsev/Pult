package ru.aokazantsev.pult.session

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import ru.aokazantsev.pult.protocol.message.ChoiceOption
import ru.aokazantsev.pult.protocol.message.ChoiceQuestion
import ru.aokazantsev.pult.protocol.message.ProtocolText
import ru.aokazantsev.pult.protocol.message.Request

class RequestSummaryTest {

    @Test
    fun permissionIsJoinedOnOneLineAndCutByCodePoints() {
        val emoji = "\uD83D\uDE00"
        val request = Request.Permission(
            requestId = "r1",
            thread = "t1",
            title = "t",
            afterSeq = 0,
            tool = "Bash",
            summary = "a\nb" + "x".repeat(110) + emoji + "y".repeat(20),
            input = emptyList(),
        )
        val summary = RequestSummary.of(request)
        assertTrue('\n' !in summary)
        assertEquals(120, ProtocolText.codePointLength(summary))
        assertTrue(summary.endsWith(emoji))
    }

    @Test
    fun choiceTakesFirstQuestion() {
        fun question(id: String, text: String) =
            ChoiceQuestion(id, null, text, multi = false, other = false, options = listOf(ChoiceOption("o", "A", null)))
        val request = Request.Choice("r1", "t1", "t", 0, answerable = true, questions = listOf(question("q1", "first"), question("q2", "second")))
        assertEquals("first", RequestSummary.of(request))
    }
}
