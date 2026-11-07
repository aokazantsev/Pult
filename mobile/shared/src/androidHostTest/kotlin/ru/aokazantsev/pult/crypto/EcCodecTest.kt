package ru.aokazantsev.pult.crypto

import java.math.BigInteger
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith

class EcCodecTest {

    @Test
    fun coordinatesAreTrimmedAndPaddedTo32Bytes() {
        assertEquals(32, EcCodec.fixed32(BigInteger.ONE).size)
        assertEquals(32, EcCodec.fixed32(BigInteger.ONE.shiftLeft(255)).size)
        assertEquals(32, EcCodec.fixed32(BigInteger.ONE.shiftLeft(256).subtract(BigInteger.ONE)).size)
        assertEquals(1, EcCodec.fixed32(BigInteger.ONE)[31].toInt())
    }

    @Test
    fun derWithShortIntegersIsPaddedOnTheLeft() {
        val der = byteArrayOf(0x30, 0x09, 0x02, 0x02, 0x01, 0x02, 0x02, 0x03, 0x00, 0x00.toByte(), 0x81.toByte())
        val converted = EcCodec.derToP1363(der)
        assertEquals(64, converted.size)
        assertContentEquals(byteArrayOf(1, 2), converted.copyOfRange(30, 32))
        assertContentEquals(byteArrayOf(0, 0x81.toByte()), converted.copyOfRange(62, 64))
    }

    @Test
    fun malformedDerIsRejected() {
        assertFailsWith<IllegalArgumentException> { EcCodec.derToP1363(byteArrayOf()) }
        assertFailsWith<IllegalArgumentException> { EcCodec.derToP1363(byteArrayOf(0x30, 0x03, 0x02, 0x01, 0x01)) }
        assertFailsWith<IllegalArgumentException> {
            EcCodec.derToP1363(byteArrayOf(0x30, 0x08, 0x02, 0x01, 0x01, 0x02, 0x01, 0x01, 0x00, 0x00))
        }
        assertFailsWith<IllegalArgumentException> {
            EcCodec.derToP1363(byteArrayOf(0x30, 0x06, 0x02, 0x01, 0x80.toByte(), 0x02, 0x01, 0x01))
        }
    }
}
