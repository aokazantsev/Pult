package ru.aokazantsev.pult.ui.thread

import kotlin.test.Test
import kotlin.test.assertEquals
import ru.aokazantsev.pult.protocol.message.ChoiceAnswer
import ru.aokazantsev.pult.protocol.message.ChoiceOption
import ru.aokazantsev.pult.protocol.message.ChoiceQuestion

class ChoiceFormStateTest {

    private fun question(multi: Boolean) = ChoiceQuestion(
        id = "q",
        header = null,
        question = "?",
        multi = multi,
        other = true,
        options = listOf(ChoiceOption("o1", "A", null), ChoiceOption("o2", "B", null)),
    )

    private val single = question(multi = false)
    private val multi = question(multi = true)

    @Test
    fun singleChoiceReplacesSelection() {
        val form = ChoiceFormState().toggle(single, "o1").toggle(single, "o2")
        assertEquals(listOf(ChoiceAnswer("q", listOf("o2"))), form.answers(listOf(single)))
    }

    @Test
    fun secondTapClearsSingleChoice() {
        val form = ChoiceFormState().toggle(single, "o1").toggle(single, "o1")
        assertEquals(listOf(ChoiceAnswer("q", emptyList())), form.answers(listOf(single)))
    }

    @Test
    fun optionAfterOtherDropsOtherInSingle() {
        val form = ChoiceFormState().withOther(single, "свой").toggle(single, "o1")
        assertEquals(listOf(ChoiceAnswer("q", listOf("o1"))), form.answers(listOf(single)))
    }

    @Test
    fun otherAfterOptionClearsSelectionInSingle() {
        val form = ChoiceFormState().toggle(single, "o1").withOther(single, "свой")
        assertEquals(listOf(ChoiceAnswer("q", emptyList(), "свой")), form.answers(listOf(single)))
    }

    @Test
    fun multiKeepsOtherWithOptions() {
        val form = ChoiceFormState().withOther(multi, "свой").toggle(multi, "o2").toggle(multi, "o1")
        assertEquals(listOf(ChoiceAnswer("q", listOf("o1", "o2"), "свой")), form.answers(listOf(multi)))
    }

    @Test
    fun otherIsCutByCodePointsWithoutSplittingPairs() {
        val emoji = "😀"
        val form = ChoiceFormState().withOther(multi, "a".repeat(1999) + emoji + emoji)
        assertEquals("a".repeat(1999) + emoji, form.other.getValue("q"))
    }

    @Test
    fun blankOtherIsNotSent() {
        val form = ChoiceFormState().withOther(multi, "  ").toggle(multi, "o1")
        assertEquals(listOf(ChoiceAnswer("q", listOf("o1"))), form.answers(listOf(multi)))
    }
}
