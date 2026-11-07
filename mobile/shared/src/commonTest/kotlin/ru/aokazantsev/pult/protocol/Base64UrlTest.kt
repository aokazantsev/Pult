package ru.aokazantsev.pult.protocol

import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertNull

class Base64UrlTest {

    @Test
    fun roundTripsAllLengths() {
        for (size in 0..40) {
            val data = ByteArray(size) { (it * 37 + 11).toByte() }
            assertContentEquals(data, Base64Url.decode(Base64Url.encode(data)))
        }
    }

    @Test
    fun encodesWithUrlAlphabetAndNoPadding() {
        assertEquals("-_8", Base64Url.encode(byteArrayOf(0xFB.toByte(), 0xFF.toByte())))
        assertEquals("AA", Base64Url.encode(byteArrayOf(0)))
    }

    @Test
    fun nonCanonicalInputIsRejected() {
        assertNull(Base64Url.decode("AB"))
        assertNull(Base64Url.decode("AA="))
        assertNull(Base64Url.decode("+_8"))
        assertNull(Base64Url.decode("A"))
        assertNull(Base64Url.decode("AAAAA"))
        assertNull(Base64Url.decode("AA\n"))
        assertNull(Base64Url.decode("AAAAB"))
    }
}
