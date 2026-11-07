package ru.aokazantsev.pult.vectors

import java.math.BigInteger
import java.security.KeyFactory
import java.security.spec.ECPrivateKeySpec
import kotlinx.serialization.json.JsonObject
import ru.aokazantsev.pult.crypto.EcCodec
import ru.aokazantsev.pult.crypto.JcaDeviceKeys
import ru.aokazantsev.pult.vectors.Vectors.str

object FixedKeys {

    private val keys = Vectors.load("keys").getValue("keys") as JsonObject

    fun publicOf(name: String): String = keys.getValue(name).str("public")

    fun sec1Of(name: String): ByteArray =
        byteArrayOf(4) + Vectors.hex(keys.getValue(name).str("x")) + Vectors.hex(keys.getValue(name).str("y"))

    fun device(signName: String, agreeName: String): JcaDeviceKeys {
        val factory = KeyFactory.getInstance("EC")
        fun private(name: String) =
            factory.generatePrivate(ECPrivateKeySpec(BigInteger(keys.getValue(name).str("d"), 16), EcCodec.p256))
        return JcaDeviceKeys(
            signPrivate = private(signName),
            signPublic = sec1Of(signName),
            agreePrivate = private(agreeName),
            agreePublic = sec1Of(agreeName),
            agreementProvider = null,
        )
    }

    fun phone(): JcaDeviceKeys = device("phone_sign", "phone_agree")

    fun pc(): JcaDeviceKeys = device("pc_sign", "pc_agree")
}
