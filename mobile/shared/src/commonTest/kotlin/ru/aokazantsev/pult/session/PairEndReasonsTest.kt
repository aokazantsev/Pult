package ru.aokazantsev.pult.session

import kotlin.test.Test
import kotlin.test.assertEquals

class PairEndReasonsTest {

    private val idle = 4 * 3600

    @Test
    fun everyRowOfTheReasonTableHasItsText() {
        val expected = listOf(
            Triple("user", "pc", "Сессию разорвали на ПК"),
            Triple("user", "phone", "Телефон отвязан"),
            Triple("idle", "pc", "Сессия закрыта: нет событий 4 ч"),
            Triple("replaced", "pc", "На ПК начата новая сессия"),
            Triple("pairing_rejected", "pc", "ПК отклонил сопряжение: проверка не прошла"),
            Triple("no_hello", "phone", "Сопряжение не завершено, отсканируйте новый QR"),
            Triple("pc_offline", "server", "ПК был без связи дольше 5 минут"),
        )
        expected.forEach { (reason, by, text) -> assertEquals(text, PairEndReasons.describe(reason, by, idle), "$reason/$by") }
    }

    @Test
    fun missingReasonIsTheGoneText() {
        assertEquals("Сессии больше нет: её разорвала другая сторона", PairEndReasons.GONE)
    }

    @Test
    fun unknownReasonIsShownSanitizedAndShort() {
        val text = PairEndReasons.describe("x‮y\nz" + "a".repeat(100), "pc", idle)
        assertEquals("Сессия разорвана (xy z" + "a".repeat(60) + ")", text)
    }

    @Test
    fun missingReasonHasNoEmptyBrackets() {
        assertEquals("Сессия разорвана", PairEndReasons.describe(null, null, idle))
    }

    @Test
    fun idleDurationIsFormattedInWords() {
        assertEquals("15 мин", IdleDuration.format(900))
        assertEquals("4 ч", IdleDuration.format(4 * 3600))
        assertEquals("1 ч 30 мин", IdleDuration.format(5400))
        assertEquals("36 ч", IdleDuration.format(36 * 3600))
    }
}
