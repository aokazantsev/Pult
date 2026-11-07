package ru.aokazantsev.pult.navigation

import kotlin.test.Test
import kotlin.test.assertEquals

class ScanExitTest {

    @Test
    fun returnsOnceWhilePairedStaysTrue() {
        val exit = ScanExit()
        assertEquals(false, exit.onPairedChanged(false))
        assertEquals(true, exit.onPairedChanged(true))
        assertEquals(false, exit.onPairedChanged(true))
    }

    @Test
    fun returnsAgainAfterUnpairAndNewPairing() {
        val exit = ScanExit()
        exit.onPairedChanged(true)
        exit.onPairedChanged(false)
        assertEquals(true, exit.onPairedChanged(true))
    }
}
