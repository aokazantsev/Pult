package ru.aokazantsev.pult.scanner

import com.google.zxing.BarcodeFormat
import com.google.zxing.EncodeHintType
import com.google.zxing.qrcode.QRCodeWriter
import java.awt.image.BufferedImage
import java.io.File
import javax.imageio.ImageIO
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import ru.aokazantsev.pult.vectors.Vectors
import ru.aokazantsev.pult.vectors.Vectors.str

class QrFrameDecoderTest {

    private val decoder = QrFrameDecoder()

    @Test
    fun decodesTheVectorQrFromALuminanceFrame() {
        val text = Vectors.load("pairing").str("qr")
        val frame = luminanceFrame(text, scale = 4, rowPadding = 24)
        assertEquals(text, decoder.decodeLuminance(frame.data, frame.rowStride, frame.width, frame.height))
    }

    @Test
    fun blankFrameYieldsNothing() {
        val data = ByteArray(200 * 200) { 0x80.toByte() }
        assertNull(decoder.decodeLuminance(data, 200, 200, 200))
    }

    @Test
    fun writesPngForManualCameraCheckWhenRequested() {
        val source = System.getenv("PULT_QR_TEXT_FILE") ?: return
        val target = System.getenv("PULT_QR_PNG") ?: return
        val text = File(source).readText(Charsets.UTF_8)
        val frame = luminanceFrame(text, scale = 8, rowPadding = 0)
        val image = BufferedImage(frame.width, frame.height, BufferedImage.TYPE_BYTE_GRAY)
        for (y in 0 until frame.height) {
            for (x in 0 until frame.width) {
                val level = frame.data[y * frame.rowStride + x].toInt() and 0xFF
                image.raster.setSample(x, y, 0, level)
            }
        }
        ImageIO.write(image, "png", File(target))
    }

    private class Frame(val data: ByteArray, val rowStride: Int, val width: Int, val height: Int)

    private fun luminanceFrame(text: String, scale: Int, rowPadding: Int): Frame {
        val matrix = QRCodeWriter().encode(
            text, BarcodeFormat.QR_CODE, 0, 0, mapOf(EncodeHintType.MARGIN to 4),
        )
        val width = matrix.width * scale
        val height = matrix.height * scale
        val rowStride = width + rowPadding
        val data = ByteArray(rowStride * height) { 0xFF.toByte() }
        for (y in 0 until height) {
            for (x in 0 until width) {
                if (matrix.get(x / scale, y / scale)) data[y * rowStride + x] = 0
            }
        }
        return Frame(data, rowStride, width, height)
    }
}
