package ru.aokazantsev.pult.access

import android.app.KeyguardManager
import android.content.Context
import android.hardware.biometrics.BiometricManager
import android.hardware.biometrics.BiometricPrompt
import android.os.CancellationSignal

class AndroidDeviceAuthenticator(private val context: Context) : DeviceAuthenticator {

    override fun isDeviceSecure(): Boolean =
        context.getSystemService(KeyguardManager::class.java)?.isDeviceSecure == true

    override fun authenticate(onResult: (AuthResult) -> Unit) {
        val prompt = BiometricPrompt.Builder(context)
            .setTitle("Pult")
            .setSubtitle("Подтвердите, что это вы")
            .setAllowedAuthenticators(BiometricManager.Authenticators.BIOMETRIC_STRONG or BiometricManager.Authenticators.DEVICE_CREDENTIAL)
            .build()
        prompt.authenticate(
            CancellationSignal(),
            context.mainExecutor,
            object : BiometricPrompt.AuthenticationCallback() {
                override fun onAuthenticationSucceeded(result: BiometricPrompt.AuthenticationResult) {
                    onResult(AuthResult.Success)
                }

                override fun onAuthenticationError(errorCode: Int, errString: CharSequence?) {
                    onResult(AuthResult.Failed)
                }
            },
        )
    }
}
