package ru.aokazantsev.pult.session

import kotlin.test.Test
import kotlin.test.assertEquals

class FeedStoreTest {

    @Test
    fun keepsNewestFiveHundredPerThread() {
        val store = FeedStore()
        store.add("t1", (1L..501L).map(SessionFixtures::notice))
        val slice = store.slice("t1", lastSeq = null)
        assertEquals((2L..501L).toList(), slice.entries.map { it.seq })
        assertEquals(listOf<FeedMarker>(FeedMarker.Start), slice.markers)
        assertEquals(501L, store.position("t1"))
    }

    @Test
    fun evictionAcrossBatchesKeepsNewest() {
        val store = FeedStore()
        store.add("t1", (300L..700L).map(SessionFixtures::notice))
        store.add("t1", (1L..200L).map(SessionFixtures::notice))
        assertEquals(500, store.slice("t1", null).entries.size)
        assertEquals(((102L..200L) + (300L..700L)).toList(), store.slice("t1", null).entries.map { it.seq })
    }
}
