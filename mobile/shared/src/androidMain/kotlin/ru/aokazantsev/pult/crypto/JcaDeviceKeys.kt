package ru.aokazantsev.pult.crypto

import java.security.PrivateKey
import java.security.Signature
import javax.crypto.KeyAgreement

class JcaDeviceKeys(
    private val signPrivate: PrivateKey,
    override val signPublic: ByteArray,
    private val agreePrivate: PrivateKey,
    override val agreePublic: ByteArray,
    private val agreementProvider: String?,
) : DeviceKeys {

    override fun sign(data: ByteArray): ByteArray {
        val signature = Signature.getInstance("SHA256withECDSA")
        signature.initSign(signPrivate)
        signature.update(data)
        return EcCodec.derToP1363(signature.sign())
    }

    override fun agree(peerAgreePublic: ByteArray): ByteArray {
        val agreement = if (agreementProvider != null) {
            KeyAgreement.getInstance("ECDH", agreementProvider)
        } else {
            KeyAgreement.getInstance("ECDH")
        }
        agreement.init(agreePrivate)
        agreement.doPhase(EcCodec.sec1ToPublic(peerAgreePublic), true)
        return agreement.generateSecret()
    }
}
