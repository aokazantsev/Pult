package ru.aokazantsev.pult.session

import kotlin.test.Test
import kotlin.test.assertEquals

class HelloRequestPolicyTest {

    @Test
    fun repeatsAreSpacedByTenSeconds() {
        val policy = HelloRequestPolicy()
        assertEquals(true, policy.tryRequest(0))
        assertEquals(false, policy.tryRequest(9_999))
        assertEquals(true, policy.tryRequest(10_000))
    }

    @Test
    fun atMostThreePerStream() {
        val policy = HelloRequestPolicy()
        assertEquals(listOf(true, true, true, false), listOf(0L, 10_000L, 20_000L, 30_000L).map(policy::tryRequest))
        assertEquals(true, policy.exhausted)
    }

    @Test
    fun waitIsTheRestOfTheInterval() {
        val policy = HelloRequestPolicy()
        assertEquals(0L, policy.waitMs(0))
        policy.tryRequest(1_000)
        assertEquals(4_000L, policy.waitMs(7_000))
        assertEquals(0L, policy.waitMs(20_000))
    }

    @Test
    fun newStreamStartsOver() {
        val policy = HelloRequestPolicy()
        listOf(0L, 10_000L, 20_000L).forEach(policy::tryRequest)
        policy.reset()
        assertEquals(true, policy.tryRequest(20_001))
    }
}
